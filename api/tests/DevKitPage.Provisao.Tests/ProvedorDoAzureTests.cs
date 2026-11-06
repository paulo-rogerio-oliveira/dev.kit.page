using System.Net;
using System.Text.Json;
using Azure.Core;
using DevKitPage.Core;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao.Tests;

/// <summary>
/// O provedor do Azure contra um ARM de mentira (nenhum teste sai para a rede): os três PUT idempotentes com o token da
/// identidade e as tags, a operação longa acompanhada só no host do ARM, o DELETE que aceita o 404, e a CLASSIFICAÇÃO das
/// falhas — 429/5xx/rede transitórias, 403 e o nome tomado definitivas, sem o id da assinatura na mensagem. E a composição:
/// sem segredo, com a identidade gerenciada em produção, e sem subir mal configurada.
/// </summary>
public sealed class ProvedorDoAzureTests
{
    private const string Assinatura = "3f2b9c0e-5d7a-4c1b-9e2f-1a2b3c4d5e6f";
    private static readonly FilaNaNuvem Fila = new(7, 42, "rg-devkit-u42", "dkq16abcd1234", "comandos", "brazilsouth");

    /// <summary>O token de mentira: o ARM falso confere que ele chega em todo pedido.</summary>
    private sealed class CredencialFalsa : TokenCredential
    {
        public int Pedidos { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Assert.Equal(["https://management.azure.com/.default"], requestContext.Scopes);
            Pedidos++;
            return new AccessToken("token-da-identidade", DateTimeOffset.UtcNow.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    /// <summary>O ARM falso: responde na ordem que o teste montou e guarda o que recebeu.</summary>
    private sealed class ArmFalso : HttpMessageHandler
    {
        public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Respostas { get; } = new();
        public List<(HttpMethod Metodo, string Url, string Corpo, string? Token)> Recebidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken ct)
        {
            var corpo = pedido.Content is null ? string.Empty : await pedido.Content.ReadAsStringAsync(ct);
            Recebidos.Add((pedido.Method, pedido.RequestUri!.ToString(), corpo, pedido.Headers.Authorization?.Parameter));
            return Respostas.Count > 0 ? Respostas.Dequeue()(pedido) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }

        public void Responder(HttpStatusCode status, string corpo = "{}", Action<HttpResponseMessage>? ajustar = null)
            => Respostas.Enqueue(_ =>
            {
                var resposta = new HttpResponseMessage(status) { Content = new StringContent(corpo) };
                ajustar?.Invoke(resposta);
                return resposta;
            });
    }

    private static (ProvedorDoAzure Provedor, ArmFalso Arm, CredencialFalsa Credencial) Montar()
    {
        var arm = new ArmFalso();
        var credencial = new CredencialFalsa();
        var http = new HttpClient(arm) { BaseAddress = new Uri("https://management.azure.com/") };
        var opcoes = Options.Create(new OpcoesDoWorker { AssinaturaId = Assinatura, EsperaDaOperacaoSegundos = 0, MaximoDeConsultas = 3 });
        return (new ProvedorDoAzure(http, credencial, opcoes), arm, credencial);
    }

    private static string Erro(string codigo, string mensagem) => JsonSerializer.Serialize(new { error = new { code = codigo, message = mensagem } });

    [Fact]
    public async Task Cria_o_grupo_a_conta_e_a_fila_com_o_token_e_as_tags()
    {
        var (provedor, arm, credencial) = Montar();

        var criado = await provedor.CriarFilaAsync(Fila, default);

        Assert.Equal(3, arm.Recebidos.Count);
        Assert.All(arm.Recebidos, r => Assert.Equal((HttpMethod.Put, "token-da-identidade"), (r.Metodo, r.Token)));
        Assert.Equal(3, credencial.Pedidos);
        var conta = $"https://management.azure.com/subscriptions/{Assinatura}/resourceGroups/rg-devkit-u42/providers/Microsoft.Storage/storageAccounts/dkq16abcd1234";
        Assert.Equal(
        [
            $"https://management.azure.com/subscriptions/{Assinatura}/resourcegroups/rg-devkit-u42?api-version=2021-04-01",
            $"{conta}?api-version=2023-05-01",
            $"{conta}/queueServices/default/queues/comandos?api-version=2023-05-01",
        ], arm.Recebidos.Select(r => r.Url));

        using var grupo = JsonDocument.Parse(arm.Recebidos[0].Corpo);
        Assert.Equal("brazilsouth", grupo.RootElement.GetProperty("location").GetString());
        Assert.Equal("42", grupo.RootElement.GetProperty("tags").GetProperty("devkit-usuario").GetString());
        using var armazenamento = JsonDocument.Parse(arm.Recebidos[1].Corpo);
        Assert.Equal("Standard_LRS", armazenamento.RootElement.GetProperty("sku").GetProperty("name").GetString());
        Assert.False(armazenamento.RootElement.GetProperty("properties").GetProperty("allowBlobPublicAccess").GetBoolean());
        Assert.Equal("TLS1_2", armazenamento.RootElement.GetProperty("properties").GetProperty("minimumTlsVersion").GetString());

        Assert.Equal($"/subscriptions/{Assinatura}/resourceGroups/rg-devkit-u42/providers/Microsoft.Storage/storageAccounts/dkq16abcd1234", criado.IdNoAzure);
        Assert.Equal("https://dkq16abcd1234.queue.core.windows.net/comandos", criado.Endpoint);
    }

    [Fact]
    public async Task A_conta_em_criacao_202_e_acompanhada_no_location_ate_terminar()
    {
        var (provedor, arm, _) = Montar();
        var operacao = new Uri("https://management.azure.com/subscriptions/s/providers/Microsoft.Storage/locations/brazilsouth/asyncoperations/op1");
        arm.Responder(HttpStatusCode.Created);
        arm.Responder(HttpStatusCode.Accepted, ajustar: r => r.Headers.Location = operacao);
        arm.Responder(HttpStatusCode.Accepted, ajustar: r => r.Headers.Location = operacao);
        arm.Responder(HttpStatusCode.OK, "{\"properties\":{\"provisioningState\":\"Succeeded\"}}");
        arm.Responder(HttpStatusCode.OK);

        await provedor.CriarFilaAsync(Fila, default);

        Assert.Equal([HttpMethod.Put, HttpMethod.Put, HttpMethod.Get, HttpMethod.Get, HttpMethod.Put], arm.Recebidos.Select(r => r.Metodo));
        Assert.Equal(operacao.ToString(), arm.Recebidos[2].Url);
    }

    [Fact]
    public async Task A_operacao_que_nao_termina_nas_consultas_e_transitoria_e_o_location_de_outro_host_nao_e_seguido()
    {
        var (provedor, arm, _) = Montar();
        var operacao = new Uri("https://management.azure.com/op");
        arm.Responder(HttpStatusCode.OK);
        for (var i = 0; i < 4; i++)
            arm.Responder(HttpStatusCode.Accepted, ajustar: r => r.Headers.Location = operacao);

        var esgotada = await Assert.ThrowsAsync<FalhaNaNuvem>(() => provedor.CriarFilaAsync(Fila, default));
        Assert.True(esgotada.Transitoria);

        var (outro, armDoOutro, _) = Montar();
        armDoOutro.Responder(HttpStatusCode.OK);
        armDoOutro.Responder(HttpStatusCode.Accepted, ajustar: r => r.Headers.Location = new Uri("https://coletor.exemplo.test/op"));

        var desviada = await Assert.ThrowsAsync<FalhaNaNuvem>(() => outro.CriarFilaAsync(Fila, default));
        Assert.False(desviada.Transitoria);
        Assert.DoesNotContain(armDoOutro.Recebidos, r => r.Url.Contains("exemplo.test", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "TooManyRequests", true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ServiceUnavailable", true)]
    [InlineData(HttpStatusCode.RequestTimeout, "RequestTimeout", true)]
    [InlineData(HttpStatusCode.Forbidden, "AuthorizationFailed", false)]
    [InlineData(HttpStatusCode.BadRequest, "InvalidResourceLocation", false)]
    public async Task A_falha_do_arm_sai_classificada_sem_o_id_da_assinatura(HttpStatusCode status, string codigo, bool transitoria)
    {
        var (provedor, arm, _) = Montar();
        arm.Responder(status, Erro(codigo, $"The client has no authorization over /subscriptions/{Assinatura}/resourcegroups/rg-devkit-u42."));

        var falha = await Assert.ThrowsAsync<FalhaNaNuvem>(() => provedor.CriarFilaAsync(Fila, default));

        Assert.Equal(transitoria, falha.Transitoria);
        Assert.Contains(codigo, falha.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Assinatura, falha.Message, StringComparison.Ordinal);
        Assert.Single(arm.Recebidos);
    }

    [Fact]
    public async Task O_nome_tomado_por_outra_conta_e_definitivo_e_diz_o_que_fazer()
    {
        var (provedor, arm, _) = Montar();
        arm.Responder(HttpStatusCode.OK);
        arm.Responder(HttpStatusCode.Conflict, Erro("StorageAccountAlreadyTaken", "The storage account named dkq16abcd1234 is already taken."));

        var falha = await Assert.ThrowsAsync<FalhaNaNuvem>(() => provedor.CriarFilaAsync(Fila, default));

        Assert.False(falha.Transitoria);
        Assert.Contains("outro nome é sorteado", falha.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_resposta_do_azure_a_falha_e_transitoria()
    {
        var (provedor, arm, _) = Montar();
        arm.Respostas.Enqueue(_ => throw new HttpRequestException("A conexão foi recusada."));

        Assert.True((await Assert.ThrowsAsync<FalhaNaNuvem>(() => provedor.CriarFilaAsync(Fila, default))).Transitoria);
    }

    [Fact]
    public async Task Remover_apaga_a_conta_e_a_que_ja_nao_existe_tambem_e_sucesso()
    {
        var (provedor, arm, _) = Montar();
        arm.Responder(HttpStatusCode.NotFound, Erro("ResourceGroupNotFound", "Resource group not found."));

        await provedor.RemoverFilaAsync(Fila, default);

        var (metodo, url, _, _) = Assert.Single(arm.Recebidos);
        Assert.Equal(HttpMethod.Delete, metodo);
        Assert.EndsWith("/storageAccounts/dkq16abcd1234?api-version=2023-05-01", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sem_a_assinatura_o_provedor_falha_de_vez_sem_chamar_o_arm()
    {
        var arm = new ArmFalso();
        var provedor = new ProvedorDoAzure(new HttpClient(arm) { BaseAddress = new Uri("https://management.azure.com/") }, new CredencialFalsa(),
            Options.Create(new OpcoesDoWorker()));

        Assert.False((await Assert.ThrowsAsync<FalhaNaNuvem>(() => provedor.CriarFilaAsync(Fila, default))).Transitoria);
        Assert.Empty(arm.Recebidos);
    }

    [Fact]
    public void O_worker_nao_sobe_simulado_em_producao_nem_sem_assinatura_nem_com_provedor_desconhecido()
    {
        var producao = new HostingEnvironment { EnvironmentName = "Production" };
        var desenvolvimento = new HostingEnvironment { EnvironmentName = "Development" };

        Assert.Throws<InvalidOperationException>(() => ComposicaoDoWorker.Validar(new OpcoesDoWorker { Provedor = "simulado" }, producao));
        Assert.Throws<InvalidOperationException>(() => ComposicaoDoWorker.Validar(new OpcoesDoWorker { Provedor = "azure" }, producao));
        Assert.Throws<InvalidOperationException>(() => ComposicaoDoWorker.Validar(new OpcoesDoWorker { Provedor = "aws", AssinaturaId = Assinatura }, desenvolvimento));
        ComposicaoDoWorker.Validar(new OpcoesDoWorker { Provedor = "simulado" }, desenvolvimento);
        ComposicaoDoWorker.Validar(new OpcoesDoWorker { AssinaturaId = Assinatura }, producao);
    }

    [Fact]
    public void Em_producao_a_credencial_e_a_identidade_gerenciada_nunca_um_segredo()
    {
        var producao = new HostingEnvironment { EnvironmentName = "Production" };

        Assert.IsType<Azure.Identity.ManagedIdentityCredential>(ComposicaoDoWorker.Credencial(new OpcoesDoWorker { AssinaturaId = Assinatura }, producao));
        Assert.IsType<Azure.Identity.ManagedIdentityCredential>(
            ComposicaoDoWorker.Credencial(new OpcoesDoWorker { AssinaturaId = Assinatura, IdentidadeClientId = "8d1f4c2e-0000-4000-8000-000000000001" }, producao));
        // E não há onde pôr um: as opções do worker não têm segredo nem chave.
        Assert.DoesNotContain(typeof(OpcoesDoWorker).GetProperties(), p => p.Name.Contains("Segredo", StringComparison.OrdinalIgnoreCase)
            || p.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Chave", StringComparison.OrdinalIgnoreCase));
    }

    private static readonly AplicativoNaNuvem AppService =
        new(8, 42, Contracts.V1.ProvisaoV1.TipoAppService, "rg-devkit-u42", "app-u16-api-pedidos-7f3k", "asp-u16-api-pedidos-7f3k", "brazilsouth", "B1");

    private static readonly AplicativoNaNuvem SiteEstatico =
        new(9, 42, Contracts.V1.ProvisaoV1.TipoStaticWebApp, "rg-devkit-u42", "swa-u16-site-docs-2m9q", string.Empty, "eastus2", "Free");

    private const string Web = $"https://management.azure.com/subscriptions/{Assinatura}/resourceGroups/rg-devkit-u42/providers/Microsoft.Web";

    [Fact]
    public async Task O_app_service_cria_o_plano_linux_e_o_site_so_https_no_plano_e_le_o_endereco()
    {
        var (provedor, arm, _) = Montar();

        await provedor.CriarPlanoAsync(AppService, default);
        arm.Responder(HttpStatusCode.OK);
        arm.Responder(HttpStatusCode.OK);
        arm.Responder(HttpStatusCode.OK, "{\"properties\":{\"defaultHostName\":\"app-u16-api-pedidos-7f3k.azurewebsites.net\"}}");
        var criado = await provedor.CriarAplicativoAsync(AppService, default);

        Assert.Equal(
        [
            (HttpMethod.Put, $"https://management.azure.com/subscriptions/{Assinatura}/resourcegroups/rg-devkit-u42?api-version=2021-04-01"),
            (HttpMethod.Put, $"{Web}/serverfarms/asp-u16-api-pedidos-7f3k?api-version=2023-12-01"),
            (HttpMethod.Put, $"https://management.azure.com/subscriptions/{Assinatura}/resourcegroups/rg-devkit-u42?api-version=2021-04-01"),
            (HttpMethod.Put, $"{Web}/sites/app-u16-api-pedidos-7f3k?api-version=2023-12-01"),
            (HttpMethod.Get, $"{Web}/sites/app-u16-api-pedidos-7f3k?api-version=2023-12-01"),
        ], arm.Recebidos.Select(r => (r.Metodo, r.Url)));

        using var plano = JsonDocument.Parse(arm.Recebidos[1].Corpo);
        Assert.Equal(("linux", "B1", true), (plano.RootElement.GetProperty("kind").GetString(), plano.RootElement.GetProperty("sku").GetProperty("name").GetString(),
            plano.RootElement.GetProperty("properties").GetProperty("reserved").GetBoolean()));
        using var site = JsonDocument.Parse(arm.Recebidos[3].Corpo);
        var propriedades = site.RootElement.GetProperty("properties");
        Assert.Equal($"/subscriptions/{Assinatura}/resourceGroups/rg-devkit-u42/providers/Microsoft.Web/serverfarms/asp-u16-api-pedidos-7f3k",
            propriedades.GetProperty("serverFarmId").GetString());
        Assert.True(propriedades.GetProperty("httpsOnly").GetBoolean());
        Assert.Equal("Disabled", propriedades.GetProperty("siteConfig").GetProperty("ftpsState").GetString());
        Assert.Equal("8", site.RootElement.GetProperty("tags").GetProperty("devkit-recurso").GetString());

        Assert.Equal("https://app-u16-api-pedidos-7f3k.azurewebsites.net", criado.Endpoint);
        Assert.EndsWith("/Microsoft.Web/sites/app-u16-api-pedidos-7f3k", criado.IdNoAzure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_static_web_app_usa_o_sku_pedido_e_o_endereco_sorteado_pelo_azure()
    {
        var (provedor, arm, _) = Montar();
        arm.Responder(HttpStatusCode.OK);
        arm.Responder(HttpStatusCode.OK);
        arm.Responder(HttpStatusCode.OK, "{\"properties\":{\"defaultHostname\":\"gentle-sea-0a1b2c.azurestaticapps.net\"}}");

        var criado = await provedor.CriarAplicativoAsync(SiteEstatico, default);

        Assert.Equal($"{Web}/staticSites/swa-u16-site-docs-2m9q?api-version=2023-12-01", arm.Recebidos[1].Url);
        using var corpo = JsonDocument.Parse(arm.Recebidos[1].Corpo);
        Assert.Equal(("Free", "eastus2"), (corpo.RootElement.GetProperty("sku").GetProperty("name").GetString(), corpo.RootElement.GetProperty("location").GetString()));
        Assert.Equal("https://gentle-sea-0a1b2c.azurestaticapps.net", criado.Endpoint);
    }

    [Fact]
    public async Task O_nome_do_site_tomado_e_definitivo_e_remover_apaga_o_site_e_o_plano_mesmo_ja_apagados()
    {
        var (provedor, arm, _) = Montar();
        arm.Responder(HttpStatusCode.OK);
        arm.Responder(HttpStatusCode.Conflict, Erro("WebsiteAlreadyExists", $"Website with given name already exists in /subscriptions/{Assinatura}."));

        var falha = await Assert.ThrowsAsync<FalhaNaNuvem>(() => provedor.CriarAplicativoAsync(AppService, default));
        Assert.False(falha.Transitoria);
        Assert.StartsWith("O nome do App Service já é usado no Azure (409)", falha.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Assinatura, falha.Message, StringComparison.Ordinal);

        arm.Recebidos.Clear();
        arm.Responder(HttpStatusCode.NotFound);
        arm.Responder(HttpStatusCode.OK);
        await provedor.RemoverAplicativoAsync(AppService, default);
        await provedor.RemoverPlanoAsync(AppService, default);
        await provedor.RemoverPlanoAsync(SiteEstatico, default);

        Assert.Equal(
        [
            (HttpMethod.Delete, $"{Web}/sites/app-u16-api-pedidos-7f3k?api-version=2023-12-01"),
            (HttpMethod.Delete, $"{Web}/serverfarms/asp-u16-api-pedidos-7f3k?api-version=2023-12-01"),
        ], arm.Recebidos.Select(r => (r.Metodo, r.Url)));
    }
}
