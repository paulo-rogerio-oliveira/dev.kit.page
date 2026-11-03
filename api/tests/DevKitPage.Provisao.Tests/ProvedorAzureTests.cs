using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using DevKitPage.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao.Tests;

/// <summary>O Resource Manager de mentira: responde pela rota (método + caminho sem a query) e guarda cada pedido.</summary>
internal sealed class ArmDeMentira : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _respostas = new(StringComparer.Ordinal);

    public List<(HttpMethod Metodo, Uri Uri, string? Autorizacao, string Corpo)> Pedidos { get; } = [];

    public Exception? Lanca { get; set; }

    public ArmDeMentira Quando(HttpMethod metodo, string caminho, HttpStatusCode status, string corpo = "")
    {
        var chave = $"{metodo} {caminho}";
        if (!_respostas.TryGetValue(chave, out var fila))
            _respostas[chave] = fila = new Queue<Func<HttpResponseMessage>>();
        fila.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        Pedidos.Add((pedido.Method, pedido.RequestUri!, pedido.Headers.Authorization?.ToString(),
            pedido.Content is null ? string.Empty : await pedido.Content.ReadAsStringAsync(ct)));
        if (Lanca is not null)
            throw Lanca;
        var chave = $"{pedido.Method} {pedido.RequestUri!.AbsolutePath}";
        if (!_respostas.TryGetValue(chave, out var fila) || fila.Count == 0)
            throw new InvalidOperationException($"Pedido não esperado: {chave}");
        return fila.Count > 1 ? fila.Dequeue()() : fila.Peek()();
    }
}

/// <summary>A credencial de mentira: devolve um token fixo, ou falha como a identidade gerenciada fora do Azure.</summary>
internal sealed class CredencialDeMentira(Exception? falha = null) : TokenCredential
{
    public const string Token = "token-da-identidade-que-nao-pode-vazar";

    public List<string[]> Escopos { get; } = [];

    public override AccessToken GetToken(TokenRequestContext contexto, CancellationToken ct) => GetTokenAsync(contexto, ct).AsTask().Result;

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext contexto, CancellationToken ct)
    {
        Escopos.Add(contexto.Scopes);
        return falha is null ? ValueTask.FromResult(new AccessToken(Token, DateTimeOffset.UtcNow.AddHours(1))) : throw falha;
    }
}

/// <summary>
/// O provedor do Azure do trabalhador contra um Resource Manager de mentira: o caminho feliz com o token da identidade, a
/// repetição que não recria a conta, o 202 em andamento, cada erro no resultado certo, a remoção que só apaga o grupo vazio e
/// do próprio usuário, e o token que nunca sai na mensagem.
/// </summary>
public sealed class ProvedorAzureTests
{
    private const string Assinatura = "0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0";
    private const string Grupo = "/subscriptions/" + Assinatura + "/resourcegroups/rg-devkit-u7";
    private const string Conta = Grupo + "/providers/Microsoft.Storage/storageAccounts/dkabcdefghijklmnop";
    private const string Fila = Conta + "/queueServices/default/queues/comandos";

    private static readonly AlvoNaNuvem Alvo = new("rg-devkit-u7", "dkabcdefghijklmnop", "comandos", "brazilsouth",
        RegrasDaProvisao.Etiquetas("teste", 7, 1));

    private readonly ArmDeMentira _arm = new();
    private CredencialDeMentira _credencial = new();

    private ProvedorAzure Provedor(string assinatura = Assinatura)
        => new(new HttpClient(_arm), _credencial, Options.Create(new OpcoesDoAzure { AssinaturaId = assinatura }), NullLogger<ProvedorAzure>.Instance);

    private static string Estado(string estado) => $$$"""{"properties":{"provisioningState":"{{{estado}}}"}}""";

    private static string Erro(string codigo, string mensagem = "detalhe") => $$$"""{"error":{"code":"{{{codigo}}}","message":"{{{mensagem}}}"}}""";

    [Fact]
    public async Task Cria_o_grupo_a_conta_e_a_fila_com_o_token_da_identidade()
    {
        _arm.Quando(HttpMethod.Put, Grupo, HttpStatusCode.Created)
            .Quando(HttpMethod.Get, Conta, HttpStatusCode.NotFound, Erro("ResourceNotFound"))
            .Quando(HttpMethod.Put, Conta, HttpStatusCode.OK, Estado("Succeeded"))
            .Quando(HttpMethod.Put, Fila, HttpStatusCode.OK);

        var resultado = await Provedor().GarantirFilaAsync(Alvo, default);

        Assert.Equal(SituacaoNaNuvem.Pronto, resultado.Situacao);
        Assert.Equal([HttpMethod.Put, HttpMethod.Get, HttpMethod.Put, HttpMethod.Put], _arm.Pedidos.Select(p => p.Metodo));
        Assert.All(_arm.Pedidos, p =>
        {
            Assert.Equal("management.azure.com", p.Uri.Host);
            Assert.Equal($"Bearer {CredencialDeMentira.Token}", p.Autorizacao);
            Assert.Contains("api-version=", p.Uri.Query, StringComparison.Ordinal);
        });
        Assert.All(_credencial.Escopos, e => Assert.Equal(["https://management.azure.com/.default"], e));

        using var grupo = JsonDocument.Parse(_arm.Pedidos[0].Corpo);
        Assert.Equal("brazilsouth", grupo.RootElement.GetProperty("location").GetString());
        Assert.Equal("7", grupo.RootElement.GetProperty("tags").GetProperty("devkit-usuario").GetString());

        using var conta = JsonDocument.Parse(_arm.Pedidos[2].Corpo);
        var propriedades = conta.RootElement.GetProperty("properties");
        Assert.Equal("Standard_LRS", conta.RootElement.GetProperty("sku").GetProperty("name").GetString());
        Assert.Equal("StorageV2", conta.RootElement.GetProperty("kind").GetString());
        Assert.Equal("TLS1_2", propriedades.GetProperty("minimumTlsVersion").GetString());
        Assert.False(propriedades.GetProperty("allowBlobPublicAccess").GetBoolean());
        Assert.True(propriedades.GetProperty("supportsHttpsTrafficOnly").GetBoolean());
    }

    [Fact]
    public async Task A_repeticao_nao_recria_a_conta_que_ja_existe()
    {
        _arm.Quando(HttpMethod.Put, Grupo, HttpStatusCode.OK)
            .Quando(HttpMethod.Get, Conta, HttpStatusCode.OK, Estado("Succeeded"))
            .Quando(HttpMethod.Put, Fila, HttpStatusCode.OK);

        Assert.Equal(SituacaoNaNuvem.Pronto, (await Provedor().GarantirFilaAsync(Alvo, default)).Situacao);
        Assert.DoesNotContain(_arm.Pedidos, p => p.Metodo == HttpMethod.Put && p.Uri.AbsolutePath == Conta);
    }

    [Theory]
    [InlineData(HttpStatusCode.Accepted, "")]
    [InlineData(HttpStatusCode.OK, """{"properties":{"provisioningState":"Creating"}}""")]
    public async Task A_conta_ainda_sendo_criada_e_em_andamento_sem_pedir_a_fila(HttpStatusCode status, string corpo)
    {
        _arm.Quando(HttpMethod.Put, Grupo, HttpStatusCode.OK)
            .Quando(HttpMethod.Get, Conta, HttpStatusCode.NotFound)
            .Quando(HttpMethod.Put, Conta, status, corpo);

        Assert.Equal(SituacaoNaNuvem.EmAndamento, (await Provedor().GarantirFilaAsync(Alvo, default)).Situacao);
        Assert.DoesNotContain(_arm.Pedidos, p => p.Uri.AbsolutePath == Fila);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "AuthorizationFailed", SituacaoNaNuvem.FalhaDefinitiva, "não tem permissão")]
    [InlineData(HttpStatusCode.Unauthorized, "InvalidAuthenticationToken", SituacaoNaNuvem.FalhaDefinitiva, "não tem permissão")]
    [InlineData(HttpStatusCode.Conflict, "StorageAccountAlreadyTaken", SituacaoNaNuvem.FalhaDefinitiva, "já é de outro")]
    [InlineData(HttpStatusCode.Conflict, "AnotherOperationInProgress", SituacaoNaNuvem.FalhaTransitoria, "409")]
    [InlineData(HttpStatusCode.BadRequest, "LocationNotAvailableForResourceType", SituacaoNaNuvem.FalhaDefinitiva, "recusou")]
    [InlineData(HttpStatusCode.TooManyRequests, "TooManyRequests", SituacaoNaNuvem.FalhaTransitoria, "429")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable", SituacaoNaNuvem.FalhaTransitoria, "503")]
    public async Task Cada_erro_do_azure_vira_o_resultado_certo(HttpStatusCode status, string codigo, SituacaoNaNuvem esperada, string trecho)
    {
        _arm.Quando(HttpMethod.Put, Grupo, HttpStatusCode.OK)
            .Quando(HttpMethod.Get, Conta, HttpStatusCode.NotFound)
            .Quando(HttpMethod.Put, Conta, status, Erro(codigo));

        var resultado = await Provedor().GarantirFilaAsync(Alvo, default);

        Assert.Equal(esperada, resultado.Situacao);
        Assert.Contains(trecho, resultado.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain(CredencialDeMentira.Token, resultado.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rede_e_tempo_esgotado_sao_transitorios()
    {
        _arm.Lanca = new HttpRequestException("conexão recusada");
        Assert.Equal(SituacaoNaNuvem.FalhaTransitoria, (await Provedor().GarantirFilaAsync(Alvo, default)).Situacao);

        _arm.Lanca = new TaskCanceledException("timeout");
        Assert.Equal(SituacaoNaNuvem.FalhaTransitoria, (await Provedor().GarantirFilaAsync(Alvo, default)).Situacao);
    }

    [Fact]
    public async Task Sem_a_identidade_gerenciada_e_definitivo_e_o_token_que_falha_e_transitorio()
    {
        _credencial = new CredencialDeMentira(new CredentialUnavailableException("sem identidade"));
        var semIdentidade = await Provedor().GarantirFilaAsync(Alvo, default);
        Assert.Equal(SituacaoNaNuvem.FalhaDefinitiva, semIdentidade.Situacao);
        Assert.Contains("IdentidadeClientId", semIdentidade.Mensagem, StringComparison.Ordinal);

        _credencial = new CredencialDeMentira(new AuthenticationFailedException("IMDS fora"));
        Assert.Equal(SituacaoNaNuvem.FalhaTransitoria, (await Provedor().GarantirFilaAsync(Alvo, default)).Situacao);
        Assert.Empty(_arm.Pedidos);
    }

    [Fact]
    public async Task Sem_assinatura_ou_com_nome_fora_da_regra_nada_vai_ao_azure()
    {
        Assert.Equal(SituacaoNaNuvem.FalhaDefinitiva, (await Provedor(assinatura: " ").GarantirFilaAsync(Alvo, default)).Situacao);
        Assert.Equal(SituacaoNaNuvem.FalhaDefinitiva, (await Provedor().GarantirFilaAsync(Alvo with { Conta = "Conta-Invalida" }, default)).Situacao);
        Assert.Equal(SituacaoNaNuvem.FalhaDefinitiva, (await Provedor().RemoverFilaAsync(Alvo with { Fila = "-x" }, default)).Situacao);
        Assert.Empty(_arm.Pedidos);
    }

    [Fact]
    public async Task A_remocao_apaga_a_conta_e_o_grupo_vazio_do_usuario_e_acompanha_ate_sumir()
    {
        _arm.Quando(HttpMethod.Delete, Conta, HttpStatusCode.OK)
            .Quando(HttpMethod.Get, Grupo, HttpStatusCode.OK, """{"properties":{"provisioningState":"Succeeded"},"tags":{"devkit-usuario":"7"}}""")
            .Quando(HttpMethod.Get, Grupo + "/resources", HttpStatusCode.OK, """{"value":[]}""")
            .Quando(HttpMethod.Delete, Grupo, HttpStatusCode.Accepted);

        Assert.Equal(SituacaoNaNuvem.EmAndamento, (await Provedor().RemoverFilaAsync(Alvo, default)).Situacao);
        Assert.Contains(_arm.Pedidos, p => p.Metodo == HttpMethod.Delete && p.Uri.AbsolutePath == Grupo);

        // A volta seguinte: a conta já não existe e o grupo também não — pronto.
        var depois = new ArmDeMentira()
            .Quando(HttpMethod.Delete, Conta, HttpStatusCode.NotFound, Erro("ResourceGroupNotFound"))
            .Quando(HttpMethod.Get, Grupo, HttpStatusCode.NotFound, Erro("ResourceGroupNotFound"));
        var provedor = new ProvedorAzure(new HttpClient(depois), _credencial, Options.Create(new OpcoesDoAzure { AssinaturaId = Assinatura }), NullLogger<ProvedorAzure>.Instance);
        Assert.Equal(SituacaoNaNuvem.Pronto, (await provedor.RemoverFilaAsync(Alvo, default)).Situacao);
    }

    [Fact]
    public async Task O_grupo_com_outros_recursos_fica_e_o_de_outro_dono_nao_e_apagado()
    {
        _arm.Quando(HttpMethod.Delete, Conta, HttpStatusCode.NoContent)
            .Quando(HttpMethod.Get, Grupo, HttpStatusCode.OK, """{"tags":{"devkit-usuario":"7"}}""")
            .Quando(HttpMethod.Get, Grupo + "/resources", HttpStatusCode.OK, """{"value":[{"name":"meu-app-service"}]}""");

        var comOutros = await Provedor().RemoverFilaAsync(Alvo, default);
        Assert.Equal(SituacaoNaNuvem.Pronto, comOutros.Situacao);
        Assert.Contains("fica", comOutros.Mensagem, StringComparison.Ordinal);

        var alheio = new ArmDeMentira()
            .Quando(HttpMethod.Delete, Conta, HttpStatusCode.OK)
            .Quando(HttpMethod.Get, Grupo, HttpStatusCode.OK, """{"tags":{"devkit-usuario":"8"}}""");
        var provedor = new ProvedorAzure(new HttpClient(alheio), _credencial, Options.Create(new OpcoesDoAzure { AssinaturaId = Assinatura }), NullLogger<ProvedorAzure>.Instance);
        Assert.Equal(SituacaoNaNuvem.Pronto, (await provedor.RemoverFilaAsync(Alvo, default)).Situacao);

        Assert.DoesNotContain(_arm.Pedidos.Concat(alheio.Pedidos), p => p.Metodo == HttpMethod.Delete && p.Uri.AbsolutePath == Grupo);
    }

    [Fact]
    public async Task A_conta_removida_que_ainda_aparece_no_grupo_e_em_andamento()
    {
        _arm.Quando(HttpMethod.Delete, Conta, HttpStatusCode.OK)
            .Quando(HttpMethod.Get, Grupo, HttpStatusCode.OK, """{"tags":{"devkit-usuario":"7"}}""")
            .Quando(HttpMethod.Get, Grupo + "/resources", HttpStatusCode.OK, """{"value":[{"name":"dkabcdefghijklmnop"}]}""");

        Assert.Equal(SituacaoNaNuvem.EmAndamento, (await Provedor().RemoverFilaAsync(Alvo, default)).Situacao);
        Assert.DoesNotContain(_arm.Pedidos, p => p.Metodo == HttpMethod.Delete && p.Uri.AbsolutePath == Grupo);
    }
}
