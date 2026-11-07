using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao;

/// <summary>
/// A fila do usuário no Azure Resource Manager, pela REST — sem SDK de gestão: dois PUT idempotentes (a conta de armazenamento
/// e a fila) e um DELETE. O App Service é o plano e o site (dois passos), e o Static Web App, um recurso só (item 12) — PUT
/// idempotentes. Tudo fica no grupo FIXO da provisão (<see cref="OpcoesDoWorker.Grupo"/>, ex.: <c>devkit-clients</c>): o worker
/// não cria nem apaga grupo, recusa o pedido de outro grupo, e só REMOVE o recurso cujas tags dizem que é do usuário do pedido
/// (o que já não existe é sucesso). O token vem da <see cref="TokenCredential"/> (a identidade gerenciada do worker);
/// nenhuma chave passa por aqui nem é guardada. Cada falha sai CLASSIFICADA (<see cref="FalhaNaNuvem"/>): 408, 429, 5xx e a
/// rede são transitórias; 401/403 (a identidade sem papel), 404 do grupo, 409 do nome tomado e 400 são definitivas.
/// </summary>
public sealed class ProvedorDoAzure(HttpClient http, TokenCredential credencial, IOptions<OpcoesDoWorker> opcoes) : IProvedorDeNuvem
{
    /// <summary>O nome do HttpClient (o teste troca o handler por este nome).</summary>
    public const string NomeDoCliente = "arm";

    public const string VersaoDosGrupos = "2021-04-01";
    public const string VersaoDoStorage = "2023-05-01";

    private static readonly string[] Escopo = ["https://management.azure.com/.default"];

    /// <summary>A versão da API do <c>Microsoft.Web</c> (o plano, o site e o Static Web App).</summary>
    public const string VersaoDaWeb = "2023-12-01";

    public async Task<RecursoNaNuvem> CriarFilaAsync(FilaNaNuvem fila, CancellationToken ct)
    {
        Grupo(fila.GrupoDeRecursos);
        var tags = Tags(fila.UsuarioId, fila.RecursoId);

        // A conta: HTTPS, TLS 1.2 e sem acesso público a blob. A chave compartilhada continua permitida: a fila não aceita SAS de
        // delegação do usuário, e a SAS curta que o serviço do dev.kit vai pedir à API (fase 2) é assinada com ela.
        await EnviarAsync(HttpMethod.Put, $"{Conta(fila)}?api-version={VersaoDoStorage}", new
        {
            location = fila.Regiao,
            kind = "StorageV2",
            sku = new { name = "Standard_LRS" },
            tags,
            properties = new { minimumTlsVersion = "TLS1_2", allowBlobPublicAccess = false, supportsHttpsTrafficOnly = true },
        }, "a conta de armazenamento", ct);

        await EnviarAsync(HttpMethod.Put, $"{Conta(fila)}/queueServices/default/queues/{fila.Fila}?api-version={VersaoDoStorage}",
            new { properties = new { metadata = new Dictionary<string, string> { ["devkit"] = "comandos" } } }, "a fila", ct);

        return new RecursoNaNuvem(Conta(fila), $"https://{fila.Conta}.queue.core.windows.net/{fila.Fila}");
    }

    public Task RemoverFilaAsync(FilaNaNuvem fila, CancellationToken ct)
    {
        Grupo(fila.GrupoDeRecursos);
        return RemoverDoUsuarioAsync($"{Conta(fila)}?api-version={VersaoDoStorage}", fila.UsuarioId, "a conta de armazenamento", ct);
    }

    /// <summary>O plano Linux do App Service (o <c>serverfarm</c>, com <c>reserved</c>), no SKU pedido.</summary>
    public async Task CriarPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
    {
        Grupo(aplicativo.GrupoDeRecursos);
        var tags = Tags(aplicativo.UsuarioId, aplicativo.RecursoId);
        await EnviarAsync(HttpMethod.Put, $"{Plano(aplicativo)}?api-version={VersaoDaWeb}", new
        {
            location = aplicativo.Regiao,
            kind = "linux",
            sku = new { name = aplicativo.Plano },
            tags,
            properties = new { reserved = true },
        }, "o plano do App Service", ct);
    }

    /// <summary>
    /// O site Linux no plano (só HTTPS, TLS 1.2, sem FTP) ou o Static Web App no SKU pedido. O endereço vem do próprio recurso
    /// (o <c>defaultHostName</c>): o do Static Web App é sorteado pelo Azure.
    /// </summary>
    public async Task<RecursoNaNuvem> CriarAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
    {
        Grupo(aplicativo.GrupoDeRecursos);
        var tags = Tags(aplicativo.UsuarioId, aplicativo.RecursoId);
        object corpo = Estatico(aplicativo)
            ? new { location = aplicativo.Regiao, sku = new { name = aplicativo.Plano, tier = aplicativo.Plano }, tags, properties = new { } }
            : new
            {
                location = aplicativo.Regiao,
                kind = "app,linux",
                tags,
                properties = new
                {
                    serverFarmId = Plano(aplicativo),
                    httpsOnly = true,
                    siteConfig = new { minTlsVersion = "1.2", ftpsState = "Disabled" },
                },
            };
        await EnviarAsync(HttpMethod.Put, $"{Aplicativo(aplicativo)}?api-version={VersaoDaWeb}", corpo, OQue(aplicativo), ct);

        var host = await EnderecoAsync(aplicativo, ct) ?? (Estatico(aplicativo) ? null : $"{aplicativo.Nome}.azurewebsites.net");
        return new RecursoNaNuvem(Aplicativo(aplicativo), host is null ? string.Empty : $"https://{host}");
    }

    public Task RemoverAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
    {
        Grupo(aplicativo.GrupoDeRecursos);
        return RemoverDoUsuarioAsync($"{Aplicativo(aplicativo)}?api-version={VersaoDaWeb}", aplicativo.UsuarioId, OQue(aplicativo), ct);
    }

    public Task RemoverPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
    {
        if (aplicativo.NomeDoPlano.Length == 0)
            return Task.CompletedTask;
        Grupo(aplicativo.GrupoDeRecursos);
        return RemoverDoUsuarioAsync($"{Plano(aplicativo)}?api-version={VersaoDaWeb}", aplicativo.UsuarioId, "o plano do App Service", ct);
    }

    /// <summary>
    /// Remove SÓ o que é do usuário: lê o recurso e confere as tags de dono (<c>devkit=provisao</c> e <c>devkit-usuario</c>) antes
    /// do DELETE — no grupo compartilhado, um nome errado não apaga o de outro. O que já não existe é sucesso; o de outro dono (ou
    /// sem as tags) é recusa definitiva, sem DELETE.
    /// </summary>
    private async Task RemoverDoUsuarioAsync(string caminho, int usuarioId, string oQue, CancellationToken ct)
    {
        using (var lido = await ChamarAsync(HttpMethod.Get, caminho.TrimStart('/'), null, oQue, ct))
        {
            if (lido.StatusCode == HttpStatusCode.NotFound)
                return;
            if (!lido.IsSuccessStatusCode)
                throw await ClassificarAsync(lido, oQue, ct);
            if (!await DoUsuarioAsync(lido, usuarioId, ct))
                throw new FalhaNaNuvem($"O Azure diz que {oQue} não é deste usuário (as tags de dono não conferem): o worker não o remove.", transitoria: false);
        }
        await EnviarAsync(HttpMethod.Delete, caminho, null, $"a remoção d{oQue}", ct);
    }

    private static async Task<bool> DoUsuarioAsync(HttpResponseMessage resposta, int usuarioId, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
            return json.RootElement.ValueKind == JsonValueKind.Object
                   && json.RootElement.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object
                   && Texto(tags, "devkit") == "provisao"
                   && Texto(tags, "devkit-usuario") == usuarioId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// O grupo do pedido, conferido contra o grupo FIXO da provisão: sem a configuração, ou com o pedido gravado para outro grupo
    /// (um pedido antigo, de <c>rg-devkit-u{id}</c>), o worker falha de vez sem chamar o ARM.
    /// </summary>
    private string Grupo(string doPedido)
    {
        var grupo = opcoes.Value.Grupo.Trim();
        if (grupo.Length == 0)
            throw new FalhaNaNuvem("O worker está sem o grupo da provisão (Provisao:Grupo, ex.: devkit-clients).", transitoria: false);
        if (!string.Equals(doPedido?.Trim(), grupo, StringComparison.OrdinalIgnoreCase))
            throw new FalhaNaNuvem($"O pedido é do grupo '{doPedido}', fora do grupo da provisão ({grupo}): o worker só cria e remove dentro dele.", transitoria: false);
        return grupo;
    }

    private static Dictionary<string, string> Tags(int usuarioId, long recursoId) => new()
    {
        ["devkit"] = "provisao",
        ["devkit-usuario"] = usuarioId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["devkit-recurso"] = recursoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static bool Estatico(AplicativoNaNuvem aplicativo) => aplicativo.Tipo == ProvisaoV1.TipoStaticWebApp;

    private static string OQue(AplicativoNaNuvem aplicativo) => Estatico(aplicativo) ? "o Static Web App" : "o App Service";

    private string Plano(AplicativoNaNuvem aplicativo)
        => $"/{Assinatura()}/resourceGroups/{aplicativo.GrupoDeRecursos}/providers/Microsoft.Web/serverfarms/{aplicativo.NomeDoPlano}";

    private string Aplicativo(AplicativoNaNuvem aplicativo)
        => $"/{Assinatura()}/resourceGroups/{aplicativo.GrupoDeRecursos}/providers/Microsoft.Web/{(Estatico(aplicativo) ? "staticSites" : "sites")}/{aplicativo.Nome}";

    /// <summary>O endereço público do aplicativo (<c>properties.defaultHostName</c>; no Static Web App, <c>defaultHostname</c>); nulo sem ele.</summary>
    private async Task<string?> EnderecoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
    {
        using var resposta = await ChamarAsync(HttpMethod.Get, $"{Aplicativo(aplicativo)}?api-version={VersaoDaWeb}".TrimStart('/'), null, OQue(aplicativo), ct);
        if (!resposta.IsSuccessStatusCode)
            throw await ClassificarAsync(resposta, OQue(aplicativo), ct);
        try
        {
            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("properties", out var propriedades)
                && propriedades.ValueKind == JsonValueKind.Object)
                return Texto(propriedades, "defaultHostName") ?? Texto(propriedades, "defaultHostname");
        }
        catch (JsonException)
        {
            // Sem corpo legível: fica o endereço padrão (o do App Service) ou nenhum.
        }
        return null;
    }

    private string Assinatura()
    {
        var assinatura = opcoes.Value.AssinaturaId.Trim();
        if (assinatura.Length == 0)
            throw new FalhaNaNuvem("O worker está sem a assinatura do Azure (Provisao:AssinaturaId).", transitoria: false);
        return $"subscriptions/{assinatura}";
    }

    private string Conta(FilaNaNuvem fila)
        => $"/{Assinatura()}/resourceGroups/{fila.GrupoDeRecursos}/providers/Microsoft.Storage/storageAccounts/{fila.Conta}";

    /// <summary>
    /// Uma chamada ao ARM: 200/201/204 é sucesso; o 202 é a operação LONGA, consultada no <c>Location</c> (ou no
    /// <c>Azure-AsyncOperation</c>) até terminar; no DELETE, o 404 também é sucesso (já não existe).
    /// </summary>
    private async Task EnviarAsync(HttpMethod metodo, string caminho, object? corpo, string oQue, CancellationToken ct)
    {
        using var resposta = await ChamarAsync(metodo, caminho.TrimStart('/'), corpo, oQue, ct);
        if (metodo == HttpMethod.Delete && resposta.StatusCode == HttpStatusCode.NotFound)
            return;
        if (resposta.StatusCode == HttpStatusCode.Accepted)
        {
            await AcompanharAsync(resposta, oQue, ct);
            return;
        }
        if (!resposta.IsSuccessStatusCode)
            throw await ClassificarAsync(resposta, oQue, ct);
    }

    /// <summary>A operação longa: consulta até sair do 202, respeitando o <c>Retry-After</c>; esgotou, transitória (o pedido volta).</summary>
    private async Task AcompanharAsync(HttpResponseMessage aceita, string oQue, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var alvo = aceita.Headers.Location ?? (aceita.Headers.TryGetValues("Azure-AsyncOperation", out var async) ? new Uri(async.First()) : null);
        if (alvo is null)
            return;
        // O token do ARM só vai ao ARM: um Location de outro host não é seguido.
        if (http.BaseAddress is { } raiz && !string.Equals(alvo.Host, raiz.Host, StringComparison.OrdinalIgnoreCase))
            throw new FalhaNaNuvem($"O Azure mandou acompanhar {oQue} noutro endereço; o worker não o segue.", transitoria: false);
        var espera = Espera(aceita, cfg);
        for (var consulta = 0; consulta < Math.Max(1, cfg.MaximoDeConsultas); consulta++)
        {
            if (espera > TimeSpan.Zero)
                await Task.Delay(espera, ct);
            using var resposta = await ChamarAsync(HttpMethod.Get, alvo.ToString(), null, oQue, ct);
            if (resposta.StatusCode == HttpStatusCode.Accepted)
            {
                espera = Espera(resposta, cfg);
                continue;
            }
            if (!resposta.IsSuccessStatusCode)
                throw await ClassificarAsync(resposta, oQue, ct);
            // O Azure-AsyncOperation responde 200 com o status da operação: Failed/Canceled é falha.
            var status = await StatusDaOperacaoAsync(resposta, ct);
            if (status is "Failed" or "Canceled")
                throw new FalhaNaNuvem($"O Azure não concluiu {oQue} (status {status}).", transitoria: true);
            if (status is "InProgress")
            {
                espera = Espera(resposta, cfg);
                continue;
            }
            return;
        }
        throw new FalhaNaNuvem($"O Azure ainda não concluiu {oQue} depois de {cfg.MaximoDeConsultas} consultas.", transitoria: true);
    }

    private async Task<HttpResponseMessage> ChamarAsync(HttpMethod metodo, string caminho, object? corpo, string oQue, CancellationToken ct)
    {
        AccessToken token;
        try
        {
            token = await credencial.GetTokenAsync(new TokenRequestContext(Escopo), ct);
        }
        catch (CredentialUnavailableException e)
        {
            throw new FalhaNaNuvem("O worker não tem identidade para o Azure (a identidade gerenciada, ou o az login no desenvolvimento).", transitoria: false, e);
        }
        catch (AuthenticationFailedException e)
        {
            throw new FalhaNaNuvem("A identidade do worker não conseguiu o token do Azure.", transitoria: true, e);
        }

        using var pedido = new HttpRequestMessage(metodo, caminho);
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        if (corpo is not null)
            pedido.Content = JsonContent.Create(corpo);
        try
        {
            return await http.SendAsync(pedido, ct);
        }
        catch (HttpRequestException e)
        {
            throw new FalhaNaNuvem($"Sem resposta do Azure ao pedir {oQue}.", transitoria: true, e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new FalhaNaNuvem($"O Azure demorou demais para responder sobre {oQue}.", transitoria: true, e);
        }
    }

    /// <summary>A falha do ARM em português, com o código dele — o corpo de erro do ARM não leva segredo, e a mensagem ainda passa pela máscara.</summary>
    internal static async Task<FalhaNaNuvem> ClassificarAsync(HttpResponseMessage resposta, string oQue, CancellationToken ct)
    {
        var (codigo, mensagem) = await ErroDoArmAsync(resposta, ct);
        var status = (int)resposta.StatusCode;
        var transitoria = status is 408 or 429 or >= 500;
        var texto = (status, codigo) switch
        {
            (409, "StorageAccountAlreadyTaken") => "O nome da conta já é usado por outra no Azure. Remova este pedido e peça a fila de novo (outro nome é sorteado).",
            (409, "WebsiteAlreadyExists") => $"O nome d{oQue} já é usado no Azure (409). Remova este pedido e peça de novo (outro sufixo é sorteado).",
            (404, "ResourceGroupNotFound") => $"O grupo da provisão não existe na assinatura ({codigo}) ao criar {oQue}: crie-o — o worker não cria grupos.",
            (401 or 403, _) => $"A identidade do worker não tem permissão para {oQue} ({codigo}). Confira o papel dela na assinatura.",
            (429, _) => $"O Azure limitou os pedidos ({codigo}) ao criar {oQue}.",
            _ => $"O Azure recusou {oQue} ({status} {codigo}): {mensagem}",
        };
        return new FalhaNaNuvem(RegrasDaProvisao.Erro(texto), transitoria);
    }

    private static async Task<(string Codigo, string Mensagem)> ErroDoArmAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var erro) && erro.ValueKind == JsonValueKind.Object)
                return (Texto(erro, "code") ?? resposta.StatusCode.ToString(), Texto(erro, "message") ?? string.Empty);
        }
        catch (JsonException)
        {
            // Sem corpo de erro do ARM (um proxy, um 503 cru): fica o status.
        }
        return (resposta.StatusCode.ToString(), string.Empty);
    }

    private static async Task<string?> StatusDaOperacaoAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try
        {
            var texto = await resposta.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(texto))
                return null;
            using var json = JsonDocument.Parse(texto);
            return json.RootElement.ValueKind == JsonValueKind.Object ? Texto(json.RootElement, "status") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Texto(JsonElement objeto, string nome)
        => objeto.TryGetProperty(nome, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString() : null;

    private static TimeSpan Espera(HttpResponseMessage resposta, OpcoesDoWorker cfg)
        => resposta.Headers.RetryAfter?.Delta is { } delta ? delta : TimeSpan.FromSeconds(Math.Max(0, cfg.EsperaDaOperacaoSegundos));
}
