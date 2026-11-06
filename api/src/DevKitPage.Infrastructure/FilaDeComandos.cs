using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A fila do usuário no Azure Storage, pela REST — sem SDK, como a provisão. A chave da conta sai do Resource Manager
/// (<c>listKeys</c>) com o token da IDENTIDADE GERENCIADA da API e fica só na memória (<see cref="OpcoesDoRemoto.ChaveEmCacheMinutos"/>):
/// nunca no banco, no log nem na resposta. Com ela a API assina duas SAS de serviço da fila do usuário — a de ESCRITA, de
/// minutos, que ela mesma usa para pôr a mensagem, e a de CONSUMO (<see cref="ConversaV1.PermissaoDaSas"/>), que entrega ao
/// serviço da máquina. A fila não aceita SAS de delegação de usuário: por isso a chave compartilhada (ver o item 4).
/// </summary>
public sealed class FilaDoAzure(IHttpClientFactory clientes, TokenCredential credencial, IMemoryCache cache, TimeProvider relogio, IOptions<OpcoesDoRemoto> opcoes)
    : IFilaDeComandos
{
    /// <summary>O nome do HttpClient (o teste troca o handler por este nome).</summary>
    public const string NomeDoCliente = "fila-de-comandos";

    public const string VersaoDoArm = "2023-05-01";

    /// <summary>A permissão da SAS que a própria API usa: <c>a</c> (add) — só pôr mensagem.</summary>
    public const string PermissaoDeEscrita = "a";

    private static readonly string[] Escopo = ["https://management.azure.com/.default"];

    /// <summary>A folga do início da SAS: o relógio da máquina pode estar um pouco atrás do Azure.</summary>
    private static readonly TimeSpan Folga = TimeSpan.FromMinutes(2);

    public async Task EnviarAsync(FilaDoUsuario fila, string mensagem, TimeSpan ttl, CancellationToken ct)
    {
        var segundos = Math.Max(1, (int)Math.Ceiling(ttl.TotalSeconds));
        var corpo = $"<?xml version=\"1.0\" encoding=\"utf-8\"?><QueueMessage><MessageText>{SecurityElement.Escape(Convert.ToBase64String(Encoding.UTF8.GetBytes(mensagem)))}</MessageText></QueueMessage>";
        for (var tentativa = 1; ; tentativa++)
        {
            var agora = relogio.GetUtcNow();
            var sas = RegrasDaConversa.Sas(fila.Conta, fila.Fila, await ChaveAsync(fila, ct), PermissaoDeEscrita, agora - Folga, agora.AddMinutes(5));
            using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{Endereco(fila)}/messages?messagettl={segundos}&{sas}")
            {
                Content = new StringContent(corpo, Encoding.UTF8, "application/xml"),
            };
            pedido.Headers.Add("x-ms-version", RegrasDaConversa.VersaoDoStorage);
            using var resposta = await EnviarAsync(pedido, "pôr o comando na fila", ct);
            if (resposta.StatusCode == HttpStatusCode.Created)
                return;
            // A chave foi trocada no portal (a SAS assinada com a velha dá 403): relê a chave UMA vez.
            if (resposta.StatusCode == HttpStatusCode.Forbidden && tentativa == 1)
            {
                cache.Remove(ChaveDoCache(fila));
                continue;
            }
            throw new FalhaNaFila($"A fila recusou o comando ({(int)resposta.StatusCode} {CodigoDoStorage(resposta)}).");
        }
    }

    public async Task<AcessoAFila> EmitirSasAsync(FilaDoUsuario fila, DateTimeOffset ate, CancellationToken ct)
    {
        var sas = RegrasDaConversa.Sas(fila.Conta, fila.Fila, await ChaveAsync(fila, ct), ConversaV1.PermissaoDaSas, relogio.GetUtcNow() - Folga, ate);
        return new AcessoAFila(Endereco(fila), sas, ate);
    }

    /// <summary>
    /// O endereço da fila, montado da conta e da fila (minúsculas e dígitos, validados na provisão) — nunca de um texto do
    /// banco: a SAS assinada com a chave só vai para o host do Storage.
    /// </summary>
    public static string Endereco(FilaDoUsuario fila) => $"https://{fila.Conta}.queue.core.windows.net/{fila.Fila}";

    /// <summary>A chave da conta: da memória, ou do <c>listKeys</c> do ARM com o token da identidade da API.</summary>
    private async Task<string> ChaveAsync(FilaDoUsuario fila, CancellationToken ct)
    {
        if (cache.TryGetValue(ChaveDoCache(fila), out string? guardada) && guardada is not null)
            return guardada;
        if (!fila.IdNoAzure.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
            throw new FalhaNaFila("A fila do usuário está sem o id do Azure: peça-a de novo na conta.");

        AccessToken token;
        try
        {
            token = await credencial.GetTokenAsync(new TokenRequestContext(Escopo), ct);
        }
        catch (CredentialUnavailableException e)
        {
            throw new FalhaNaFila("A API não tem identidade para o Azure (a identidade gerenciada, ou o az login no desenvolvimento).", e);
        }
        catch (AuthenticationFailedException e)
        {
            throw new FalhaNaFila("A identidade da API não conseguiu o token do Azure.", e);
        }

        var arm = opcoes.Value.UrlDoArm.TrimEnd('/');
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{arm}{fila.IdNoAzure}/listKeys?api-version={VersaoDoArm}");
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var resposta = await EnviarAsync(pedido, "ler a chave da fila", ct);
        if (!resposta.IsSuccessStatusCode)
            throw new FalhaNaFila(resposta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized
                ? "A identidade da API não tem permissão para ler a chave da fila (listKeys). Confira o papel dela."
                : $"O Azure não devolveu a chave da fila ({(int)resposta.StatusCode}).");

        var chave = await PrimeiraChaveAsync(resposta, ct) ?? throw new FalhaNaFila("O Azure devolveu a conta da fila sem chave.");
        cache.Set(ChaveDoCache(fila), chave, TimeSpan.FromMinutes(Math.Clamp(opcoes.Value.ChaveEmCacheMinutos, 1, 60)));
        return chave;
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage pedido, string oQue, CancellationToken ct)
    {
        try
        {
            return await clientes.CreateClient(NomeDoCliente).SendAsync(pedido, ct);
        }
        catch (HttpRequestException e)
        {
            throw new FalhaNaFila($"Sem resposta do Azure ao {oQue}.", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new FalhaNaFila($"O Azure demorou demais ao {oQue}.", e);
        }
    }

    private static async Task<string?> PrimeiraChaveAsync(HttpResponseMessage resposta, CancellationToken ct)
    {
        try
        {
            using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("keys", out var chaves) && chaves.ValueKind == JsonValueKind.Array)
                foreach (var chave in chaves.EnumerateArray())
                    if (chave.ValueKind == JsonValueKind.Object && chave.TryGetProperty("value", out var valor) && valor.ValueKind == JsonValueKind.String
                        && valor.GetString() is { Length: > 0 } texto)
                        return texto;
        }
        catch (JsonException)
        {
            // Sem o corpo do ARM: a chamadora diz que veio sem chave.
        }
        return null;
    }

    /// <summary>O código de erro do Storage (o cabeçalho <c>x-ms-error-code</c>), sem o corpo — que pode citar a URL com a SAS.</summary>
    private static string CodigoDoStorage(HttpResponseMessage resposta)
        => resposta.Headers.TryGetValues("x-ms-error-code", out var codigos) ? codigos.First() : resposta.StatusCode.ToString();

    private static string ChaveDoCache(FilaDoUsuario fila) => $"fila-de-comandos:chave:{fila.IdNoAzure}";
}

/// <summary>
/// A fila SIMULADA, de desenvolvimento e das demonstrações: guarda as mensagens na memória e emite uma SAS de mentira. A API
/// não sobe com ela em Production (<see cref="FilaDeComandos.Validar"/>): ela diria "na fila" sem fila nenhuma.
/// </summary>
public sealed class FilaSimulada(TimeProvider relogio) : IFilaDeComandos
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(string Mensagem, DateTimeOffset VenceEm)>> _filas = new(StringComparer.Ordinal);

    public Task EnviarAsync(FilaDoUsuario fila, string mensagem, TimeSpan ttl, CancellationToken ct)
    {
        _filas.GetOrAdd(fila.Conta, _ => new()).Enqueue((mensagem, relogio.GetUtcNow().Add(ttl)));
        return Task.CompletedTask;
    }

    public Task<AcessoAFila> EmitirSasAsync(FilaDoUsuario fila, DateTimeOffset ate, CancellationToken ct)
        => Task.FromResult(new AcessoAFila(FilaDoAzure.Endereco(fila),
            $"sv=simulado&se={Uri.EscapeDataString(ate.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))}&sp={ConversaV1.PermissaoDaSas}&spr=https&sig=simulado",
            ate));

    /// <summary>As mensagens ainda vivas da conta (o TTL vencido some, como no Azure).</summary>
    public IReadOnlyList<string> Mensagens(string conta)
    {
        var agora = relogio.GetUtcNow();
        return _filas.TryGetValue(conta, out var fila) ? fila.Where(m => m.VenceEm > agora).Select(m => m.Mensagem).ToArray() : [];
    }
}

/// <summary>A escolha da fila pela configuração (<see cref="OpcoesDoRemoto.Fila"/>) e a recusa de subir mal configurada.</summary>
public static class FilaDeComandos
{
    /// <summary>
    /// A API NÃO sobe com a fila desconhecida, nem com a simulada em Production (o comando ficaria "na fila" sem fila
    /// nenhuma, e o site fingiria que entregou).
    /// </summary>
    public static void Validar(OpcoesDoRemoto opcoes, IHostEnvironment ambiente)
    {
        var fila = (opcoes.Fila ?? string.Empty).Trim().ToLowerInvariant();
        if (fila is not (OpcoesDoRemoto.FilaAzure or OpcoesDoRemoto.FilaSimulada))
            throw new InvalidOperationException($"Remoto:Fila é '{OpcoesDoRemoto.FilaAzure}' ou '{OpcoesDoRemoto.FilaSimulada}', e não '{opcoes.Fila}'.");
        if (fila == OpcoesDoRemoto.FilaSimulada && ambiente.IsProduction())
            throw new InvalidOperationException("A fila simulada não sobe em Production: o comando ficaria na fila sem fila nenhuma.");
    }

    public static bool Simulada(OpcoesDoRemoto opcoes) => string.Equals(opcoes.Fila?.Trim(), OpcoesDoRemoto.FilaSimulada, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A credencial do Azure de um processo do dev.kit.page (a API e o worker da provisão): a identidade gerenciada em produção
/// (a atribuída ao usuário pelo client id, ou a do sistema); fora dela, a credencial padrão (o <c>az login</c> de quem roda).
/// Nenhum client secret é lido daqui.
/// </summary>
public static class CredencialDoAzure
{
    public static TokenCredential Criar(string? identidadeClientId, bool producao)
    {
        var clientId = (identidadeClientId ?? string.Empty).Trim();
        if (clientId.Length > 0)
            return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
        return producao
            ? new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)
            : new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true });
    }
}
