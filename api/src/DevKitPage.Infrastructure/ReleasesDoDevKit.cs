using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// As releases do dev.kit no GitHub (US #405), pelo HttpClient tipado. O PAT (<c>REPO_KEY</c>) só existe
/// AQUI: o app e a landing perguntam pela versão e baixam o zip pela API, que faz o proxy em streaming.
/// <para>
/// - A lista das releases fica em memória por <see cref="OpcoesDeAtualizacao.CacheMinutos"/>: cada dev.kit e
///   cada visita à landing pergunta pela última versão, e o GitHub limita as chamadas por token.
/// - Nada do GitHub vaza: falha (sem token, fora do ar, JSON inesperado) vira nulo e um log sem o corpo da
///   resposta nem o token; a API responde 503 com uma mensagem genérica.
/// - O download segue o redirecionamento do GitHub À MÃO (o cliente nasce sem o automático): o
///   <c>releases/assets/{id}</c> responde 302 para o armazenamento, em outro host, com a URL já assinada —
///   e o pedido seguinte vai SEM o <c>Authorization</c>, para o token nunca sair do api.github.com.
/// </para>
/// </summary>
public sealed class ReleasesDoDevKit(HttpClient http, IOptions<OpcoesDeAtualizacao> opcoes, IMemoryCache cache, ILogger<ReleasesDoDevKit> log)
    : IReleasesDoDevKit
{
    /// <summary>O nome do HttpClient — o teste troca o handler do GitHub por ele.</summary>
    public const string NomeDoCliente = "github-releases";

    /// <summary>Quantas releases a lista lê (as mais recentes): o download aceita qualquer uma delas.</summary>
    public const int ReleasesLidas = 30;

    private const int SaltosMaximos = 5;
    private const string ChaveDoCache = "releases-do-devkit";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Uma consulta ao GitHub por vez quando o cache vence: as visitas simultâneas esperam a mesma resposta.</summary>
    private static readonly SemaphoreSlim UmaConsulta = new(1, 1);

    public async Task<IReadOnlyList<ReleaseDoDevKit>?> EstaveisAsync(CancellationToken ct)
    {
        var cfg = opcoes.Value;
        if (cfg.Token.Length == 0)
        {
            log.LogWarning("Atualização do dev.kit indisponível: a variável {Variavel} não está configurada.", OpcoesDeAtualizacao.VariavelDoToken);
            return null;
        }

        var chave = $"{ChaveDoCache}:{cfg.Repositorio}";
        if (cache.TryGetValue(chave, out IReadOnlyList<ReleaseDoDevKit>? guardadas))
            return guardadas;

        await UmaConsulta.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(chave, out guardadas))
                return guardadas;

            var lidas = await LerAsync(cfg, ct);
            // Só o sucesso fica em cache: o GitHub que volta do ar volta na próxima visita.
            if (lidas is not null)
                cache.Set(chave, lidas, TimeSpan.FromMinutes(Math.Max(1, cfg.CacheMinutos)));
            return lidas;
        }
        finally
        {
            UmaConsulta.Release();
        }
    }

    public async Task<PacoteAberto?> AbrirPacoteAsync(ReleaseDoDevKit release, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        if (cfg.Token.Length == 0)
            return null;

        var resposta = await AssetAsync(cfg, release.AssetId, ct);
        if (resposta is null)
            return null;
        // ResponseHeadersRead: o corpo é o stream da conexão com o GitHub, copiado direto para o cliente.
        return new PacoteAberto(await resposta.Content.ReadAsStreamAsync(ct), resposta.Content.Headers.ContentLength, resposta);
    }

    /// <summary>A lista do GitHub, já filtrada e ordenada pela <see cref="RegrasDeVersao"/>; nula na falha.</summary>
    private async Task<IReadOnlyList<ReleaseDoDevKit>?> LerAsync(OpcoesDeAtualizacao cfg, CancellationToken ct)
    {
        try
        {
            using var pedido = Pedido(cfg, $"repos/{cfg.Repositorio}/releases?per_page={ReleasesLidas}", "application/vnd.github+json");
            using var resposta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resposta.IsSuccessStatusCode)
            {
                log.LogWarning("O GitHub respondeu {Status} à lista de releases de {Repositorio}.", (int)resposta.StatusCode, cfg.Repositorio);
                return null;
            }

            var releases = await resposta.Content.ReadFromJsonAsync<ReleaseDoGitHub[]>(Json, ct) ?? [];
            var estaveis = RegrasDeVersao.Estaveis(
                    releases.Where(r => Pacote(r) is not null),
                    r => new RegrasDeVersao.Candidata(r.TagName, r.Prerelease, r.Draft, r.PublishedAt))
                .ToList();

            var lista = new List<ReleaseDoDevKit>(estaveis.Count);
            foreach (var (r, versao) in estaveis)
            {
                var zip = Pacote(r)!;
                // O hash da ÚLTIMA (a que o app instala) vale uma chamada a mais quando o GitHub não tem o digest.
                var sha = RegrasDeVersao.Sha256DoDigest(zip.Digest)
                    ?? (lista.Count == 0 ? await Sha256DoArquivoAsync(cfg, r, zip, ct) : null);
                lista.Add(new ReleaseDoDevKit(
                    versao, r.TagName!, string.IsNullOrWhiteSpace(r.Name) ? r.TagName! : r.Name.Trim(), r.PublishedAt ?? DateTimeOffset.MinValue,
                    RegrasDeVersao.Destaques(r.Body), zip.Id, zip.Name!, zip.Size, sha));
            }

            return lista;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            // Só o tipo: a mensagem de uma falha HTTP pode trazer a URL, e o corpo do GitHub não sai daqui.
            log.LogWarning("Falha ao consultar as releases de {Repositorio} no GitHub ({Tipo}).", cfg.Repositorio, ex.GetType().Name);
            return null;
        }
    }

    /// <summary>O hash do <c>&lt;zip&gt;.sha256</c> que o release.yml do git.kit publica ao lado do zip; nulo sem ele.</summary>
    private async Task<string?> Sha256DoArquivoAsync(OpcoesDeAtualizacao cfg, ReleaseDoGitHub release, AssetDoGitHub zip, CancellationToken ct)
    {
        var arquivo = release.Assets?.FirstOrDefault(a => string.Equals(a.Name, zip.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (arquivo is null)
            return null;

        using var resposta = await AssetAsync(cfg, arquivo.Id, ct);
        return resposta is null ? null : RegrasDeVersao.Sha256DoArquivo(await resposta.Content.ReadAsStringAsync(ct));
    }

    /// <summary>
    /// O asset pela API (<c>Accept: application/octet-stream</c>), seguindo o 302 para o armazenamento SEM o
    /// token. Nulo (com o log) quando o GitHub não o entrega.
    /// </summary>
    private async Task<HttpResponseMessage?> AssetAsync(OpcoesDeAtualizacao cfg, long assetId, CancellationToken ct)
    {
        var pedido = Pedido(cfg, $"repos/{cfg.Repositorio}/releases/assets/{assetId}", "application/octet-stream");
        var endereco = pedido.RequestUri!;
        var resposta = await EnviarAsync(pedido, ct);
        for (var saltos = 0; Redirecionou(resposta) && saltos < SaltosMaximos; saltos++)
        {
            endereco = new Uri(endereco, resposta.Headers.Location!); // o Location relativo vale sobre o endereço anterior
            resposta.Dispose();

            var seguinte = new HttpRequestMessage(HttpMethod.Get, endereco); // sem Authorization, de propósito
            seguinte.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            resposta = await EnviarAsync(seguinte, ct);
        }

        if (resposta.IsSuccessStatusCode)
            return resposta;

        log.LogWarning("O GitHub respondeu {Status} ao asset {Asset} de {Repositorio}.", (int)resposta.StatusCode, assetId, cfg.Repositorio);
        resposta.Dispose();
        return null;
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        using (pedido)
            return await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    private static bool Redirecionou(HttpResponseMessage resposta)
        => resposta.Headers.Location is not null
           && resposta.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
               or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    /// <summary>Um pedido à API do GitHub, com o token — o ÚNICO lugar onde ele entra num cabeçalho.</summary>
    private HttpRequestMessage Pedido(OpcoesDeAtualizacao cfg, string caminho, string aceita)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(cfg.UrlDaApi.TrimEnd('/') + "/"), caminho));
        pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.Token);
        pedido.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(aceita));
        pedido.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return pedido;
    }

    /// <summary>O pacote da release: o primeiro <c>.zip</c> (o <c>.sha256</c> e outros anexos ficam de fora).</summary>
    private static AssetDoGitHub? Pacote(ReleaseDoGitHub release)
        => release.Assets?.FirstOrDefault(a => a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Configura o cliente: o GitHub recusa pedido sem User-Agent.</summary>
    public static void Configurar(HttpClient cliente)
    {
        cliente.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("dev.kit.page", "1.0"));
        cliente.Timeout = TimeSpan.FromSeconds(60);
    }

    /// <summary>O pedaço da release do GitHub que a API lê.</summary>
    private sealed record ReleaseDoGitHub(
        string? TagName, string? Name, bool Draft, bool Prerelease, DateTimeOffset? PublishedAt, string? Body, AssetDoGitHub[]? Assets);

    /// <summary>Um anexo da release: o <see cref="Digest"/> (<c>sha256:…</c>) vem nos assets novos do GitHub.</summary>
    private sealed record AssetDoGitHub(long Id, string? Name, long Size, string? Digest);
}
