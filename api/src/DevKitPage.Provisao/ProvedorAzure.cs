using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using DevKitPage.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao;

/// <summary>
/// O Azure do trabalhador (seção <c>Provisao:Azure</c>): a assinatura do dev.kit e a identidade gerenciada com que ele cria os
/// recursos. Não há segredo aqui: o <see cref="IdentidadeClientId"/> é o id público da identidade atribuída ao contêiner, e o
/// token sai dela em tempo de execução. Sem ele, vale a credencial do ambiente (o <c>az login</c> de quem desenvolve).
/// </summary>
public sealed class OpcoesDoAzure
{
    public const string Secao = "Provisao:Azure";
    public const string ProvedorAzure = "azure";
    public const string ProvedorSimulado = "simulado";

    /// <summary><c>azure</c> (padrão) ou <c>simulado</c> — este só fora de Production, para a demonstração e o e2e.</summary>
    public string Provedor { get; set; } = ProvedorAzure;

    /// <summary>O id da assinatura onde os grupos dos usuários são criados. Vazio: todo pedido falha com o motivo.</summary>
    public string AssinaturaId { get; set; } = string.Empty;

    /// <summary>O client id da identidade gerenciada ATRIBUÍDA ao trabalhador (a dele, e não a da API).</summary>
    public string IdentidadeClientId { get; set; } = string.Empty;

    /// <summary>A raiz do Azure Resource Manager (trocada só numa nuvem soberana).</summary>
    public string Gerenciador { get; set; } = "https://management.azure.com/";

    /// <summary>A redundância da conta de armazenamento: a local é a mais barata, e a fila é só transporte.</summary>
    public string Sku { get; set; } = "Standard_LRS";
}

/// <summary>
/// A provisão no Azure pelo Resource Manager (REST), com o token da identidade gerenciada do trabalhador. Cada passo é
/// idempotente — PUT do grupo, GET antes do PUT da conta, PUT da fila — então o pedido repetido depois de uma queda acerta o
/// MESMO recurso. As respostas viram <see cref="ResultadoNaNuvem"/>: 401/403, 400 e o nome tomado são definitivos; 409 de
/// operação em andamento, 429, 5xx e a rede são transitórios; 202 é "em andamento".
/// </summary>
public sealed class ProvedorAzure(HttpClient http, TokenCredential credencial, IOptions<OpcoesDoAzure> opcoes, ILogger<ProvedorAzure> log) : IProvedorDeNuvem
{
    /// <summary>O nome do HttpClient (o teste troca o handler por este nome).</summary>
    public const string NomeDoCliente = "azure-arm";

    private const string VersaoDosGrupos = "2021-04-01";
    private const string VersaoDoStorage = "2023-05-01";
    private const string Concluido = "Succeeded";
    private const int TetoDoCorpo = 1024 * 1024;

    public Task<ResultadoNaNuvem> GarantirFilaAsync(AlvoNaNuvem alvo, CancellationToken ct) => ComSegurancaAsync(alvo, async () =>
    {
        using (var grupo = await EnviarAsync(HttpMethod.Put, Grupo(alvo), VersaoDosGrupos, new { location = alvo.Regiao, tags = alvo.Etiquetas }, ct))
        {
            if (!grupo.IsSuccessStatusCode)
                return await FalhaAsync(grupo, "criar o grupo de recursos", ct);
        }

        // A conta que já existe (a repetição) não recebe outro PUT: ele poderia mudar o que já está valendo.
        var conta = await EnviarAsync(HttpMethod.Get, Conta(alvo), VersaoDoStorage, null, ct);
        try
        {
            if (conta.StatusCode == HttpStatusCode.NotFound)
            {
                conta.Dispose();
                conta = await EnviarAsync(HttpMethod.Put, Conta(alvo), VersaoDoStorage, CorpoDaConta(alvo), ct);
                if (conta.StatusCode == HttpStatusCode.Accepted)
                    return new ResultadoNaNuvem(SituacaoNaNuvem.EmAndamento, "O Azure está criando a conta de armazenamento.");
            }

            if (!conta.IsSuccessStatusCode)
                return await FalhaAsync(conta, "criar a conta de armazenamento", ct);
            if (await EstadoAsync(conta, ct) is { } estado && !string.Equals(estado, Concluido, StringComparison.OrdinalIgnoreCase))
                return new ResultadoNaNuvem(SituacaoNaNuvem.EmAndamento, $"A conta de armazenamento está em {estado}.");
        }
        finally
        {
            conta.Dispose();
        }

        using var fila = await EnviarAsync(HttpMethod.Put, $"{Conta(alvo)}/queueServices/default/queues/{alvo.Fila}", VersaoDoStorage,
            new { properties = new { metadata = new Dictionary<string, string>() } }, ct);
        return fila.IsSuccessStatusCode
            ? new ResultadoNaNuvem(SituacaoNaNuvem.Pronto, "Grupo, conta e fila prontos.")
            : await FalhaAsync(fila, "criar a fila", ct);
    }, ct);

    public Task<ResultadoNaNuvem> RemoverFilaAsync(AlvoNaNuvem alvo, CancellationToken ct) => ComSegurancaAsync(alvo, async () =>
    {
        // A conta leva a fila junto. A que já não existe (a repetição) é sucesso.
        using (var conta = await EnviarAsync(HttpMethod.Delete, Conta(alvo), VersaoDoStorage, null, ct))
        {
            if (!conta.IsSuccessStatusCode && conta.StatusCode != HttpStatusCode.NotFound)
                return await FalhaAsync(conta, "remover a conta de armazenamento", ct);
        }

        using var grupo = await EnviarAsync(HttpMethod.Get, Grupo(alvo), VersaoDosGrupos, null, ct);
        if (grupo.StatusCode == HttpStatusCode.NotFound)
            return new ResultadoNaNuvem(SituacaoNaNuvem.Pronto, "A fila e o grupo de recursos não existem mais.");
        if (!grupo.IsSuccessStatusCode)
            return await FalhaAsync(grupo, "ler o grupo de recursos", ct);

        using var dados = await JsonAsync(grupo, ct);
        if (Texto(dados?.RootElement, "properties", "provisioningState") is { } estado && string.Equals(estado, "Deleting", StringComparison.OrdinalIgnoreCase))
            return new ResultadoNaNuvem(SituacaoNaNuvem.EmAndamento, "O Azure está removendo o grupo de recursos.");
        // Só o grupo que o dev.kit criou PARA ESTE usuário é apagado: o mesmo nome com outra etiqueta não é nosso.
        if (!alvo.Etiquetas.TryGetValue("devkit-usuario", out var dono) || Texto(dados?.RootElement, "tags", "devkit-usuario") != dono)
            return new ResultadoNaNuvem(SituacaoNaNuvem.Pronto, "Fila removida; o grupo de recursos não é do dev.kit e fica.");

        using var recursos = await EnviarAsync(HttpMethod.Get, $"{Grupo(alvo)}/resources", VersaoDosGrupos, null, ct);
        if (!recursos.IsSuccessStatusCode)
            return await FalhaAsync(recursos, "listar o grupo de recursos", ct);
        using var lista = await JsonAsync(recursos, ct);
        var nomes = lista?.RootElement.TryGetProperty("value", out var valor) == true && valor.ValueKind == JsonValueKind.Array
            ? valor.EnumerateArray().Select(r => Texto(r, "name")).ToArray()
            : [];
        if (nomes.Any(n => !string.Equals(n, alvo.Conta, StringComparison.OrdinalIgnoreCase)))
            return new ResultadoNaNuvem(SituacaoNaNuvem.Pronto, "Fila removida; o grupo de recursos fica com os outros recursos do usuário.");
        if (nomes.Length > 0)
            return new ResultadoNaNuvem(SituacaoNaNuvem.EmAndamento, "A conta removida ainda aparece no grupo de recursos.");

        using var apagar = await EnviarAsync(HttpMethod.Delete, Grupo(alvo), VersaoDosGrupos, null, ct);
        if (apagar.StatusCode == HttpStatusCode.NotFound)
            return new ResultadoNaNuvem(SituacaoNaNuvem.Pronto, "A fila e o grupo de recursos não existem mais.");
        return apagar.IsSuccessStatusCode
            ? new ResultadoNaNuvem(SituacaoNaNuvem.EmAndamento, "O Azure está removendo o grupo de recursos vazio.")
            : await FalhaAsync(apagar, "remover o grupo de recursos", ct);
    }, ct);

    /// <summary>
    /// A configuração e os nomes conferidos antes da primeira chamada, e o que escapa do HTTP (rede, tempo, credencial) no
    /// resultado certo. A mensagem nunca leva o token nem o corpo da exceção.
    /// </summary>
    private async Task<ResultadoNaNuvem> ComSegurancaAsync(AlvoNaNuvem alvo, Func<Task<ResultadoNaNuvem>> passo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(opcoes.Value.AssinaturaId))
            return new ResultadoNaNuvem(SituacaoNaNuvem.FalhaDefinitiva, "A assinatura do Azure não está configurada no trabalhador (Provisao:Azure:AssinaturaId).");
        if (!RegrasDaProvisao.NomeDaContaValido(alvo.Conta) || !RegrasDaProvisao.NomeDaFilaValido(alvo.Fila))
            return new ResultadoNaNuvem(SituacaoNaNuvem.FalhaDefinitiva, "O nome da conta ou da fila está fora da regra do Azure.");

        try
        {
            return await passo();
        }
        catch (CredentialUnavailableException)
        {
            return new ResultadoNaNuvem(SituacaoNaNuvem.FalhaDefinitiva, "A identidade gerenciada do trabalhador não está disponível: confira Provisao:Azure:IdentidadeClientId.");
        }
        catch (AuthenticationFailedException ex)
        {
            log.LogWarning(ex, "Provisão: o token da identidade gerenciada falhou.");
            return new ResultadoNaNuvem(SituacaoNaNuvem.FalhaTransitoria, "Falha ao obter o token da identidade gerenciada.");
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "Provisão: falha de rede com o Azure.");
            return new ResultadoNaNuvem(SituacaoNaNuvem.FalhaTransitoria, "Falha de rede ao falar com o Azure.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ResultadoNaNuvem(SituacaoNaNuvem.FalhaTransitoria, "O Azure não respondeu a tempo.");
        }
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string caminho, string versao, object? corpo, CancellationToken ct)
    {
        var token = await credencial.GetTokenAsync(new TokenRequestContext([Escopo]), ct);
        using var pedido = new HttpRequestMessage(metodo, new Uri(new Uri(opcoes.Value.Gerenciador), $"{caminho}?api-version={versao}"));
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (corpo is not null)
            pedido.Content = JsonContent.Create(corpo);
        return await http.SendAsync(pedido, ct);
    }

    /// <summary>O escopo do token: o do Resource Manager da nuvem configurada.</summary>
    private string Escopo => new Uri(new Uri(opcoes.Value.Gerenciador), ".default").ToString();

    private string Grupo(AlvoNaNuvem alvo)
        => $"subscriptions/{Uri.EscapeDataString(opcoes.Value.AssinaturaId.Trim())}/resourcegroups/{Uri.EscapeDataString(alvo.GrupoDeRecursos)}";

    private string Conta(AlvoNaNuvem alvo) => $"{Grupo(alvo)}/providers/Microsoft.Storage/storageAccounts/{alvo.Conta}";

    /// <summary>
    /// A conta da fila: HTTPS só, TLS 1.2 e sem acesso público a blob. A chave compartilhada fica LIGADA de propósito: a SAS de
    /// fila que o serviço do Windows vai usar (o próximo item) só existe assinada pela chave da conta — é o lugar onde a
    /// identidade gerenciada não serve. A chave nunca sai do Azure nem é gravada aqui.
    /// </summary>
    private object CorpoDaConta(AlvoNaNuvem alvo) => new
    {
        sku = new { name = opcoes.Value.Sku },
        kind = "StorageV2",
        location = alvo.Regiao,
        tags = alvo.Etiquetas,
        properties = new
        {
            minimumTlsVersion = "TLS1_2",
            supportsHttpsTrafficOnly = true,
            allowBlobPublicAccess = false,
            allowSharedKeyAccess = true,
        },
    };

    private static async Task<ResultadoNaNuvem> FalhaAsync(HttpResponseMessage resposta, string oQue, CancellationToken ct)
    {
        var (codigo, mensagem) = await ErroAsync(resposta, ct);
        var status = (int)resposta.StatusCode;
        var detalhe = codigo.Length > 0 ? $" ({codigo})" : string.Empty;
        return resposta.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                => new(SituacaoNaNuvem.FalhaDefinitiva, $"A identidade do trabalhador não tem permissão para {oQue}{detalhe}."),
            HttpStatusCode.Conflict when codigo == "StorageAccountAlreadyTaken"
                => new(SituacaoNaNuvem.FalhaDefinitiva, "O nome da conta de armazenamento já é de outro no Azure."),
            HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests
                => new(SituacaoNaNuvem.FalhaTransitoria, $"O Azure respondeu {status} ao {oQue}{detalhe}."),
            _ when status >= 500
                => new(SituacaoNaNuvem.FalhaTransitoria, $"O Azure respondeu {status} ao {oQue}{detalhe}."),
            _ => new(SituacaoNaNuvem.FalhaDefinitiva, $"O Azure recusou {oQue}{detalhe}{(mensagem.Length > 0 ? ": " + mensagem : ".")}"),
        };
    }

    /// <summary>O <c>error.code</c> e o <c>error.message</c> do ARM, com o corpo lido até um teto.</summary>
    private static async Task<(string Codigo, string Mensagem)> ErroAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try
        {
            using var dados = await JsonAsync(resposta, ct);
            return (Texto(dados?.RootElement, "error", "code") ?? string.Empty, Texto(dados?.RootElement, "error", "message") ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return (string.Empty, string.Empty);
        }
    }

    private static async Task<string?> EstadoAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        using var dados = await JsonAsync(resposta, ct);
        return Texto(dados?.RootElement, "properties", "provisioningState");
    }

    private static async Task<JsonDocument?> JsonAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        var bytes = await resposta.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length == 0 || bytes.Length > TetoDoCorpo)
            return null;
        try
        {
            return JsonDocument.Parse(bytes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texto(JsonElement? raiz, params string[] caminho)
    {
        if (raiz is not { } atual)
            return null;
        foreach (var nome in caminho)
        {
            if (atual.ValueKind != JsonValueKind.Object || !atual.TryGetProperty(nome, out atual))
                return null;
        }

        return atual.ValueKind == JsonValueKind.String ? atual.GetString() : null;
    }
}
