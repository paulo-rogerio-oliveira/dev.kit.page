using System.Net;
using System.Text;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O GitHub por trás do HttpClient das releases (US #405), no processo: a lista de releases, o asset que
/// responde 302 para o "armazenamento" em OUTRO host (como o GitHub faz) e o zip. Registra cada pedido
/// com o Authorization que chegou — é por ele que o teste prova que o token vai ao api.github.com e
/// NUNCA ao armazenamento.
/// </summary>
public sealed class GitHubDeMentira : HttpMessageHandler
{
    public const string Api = "https://api.github.com/repos/paulo-rogerio-oliveira/git.kit";
    public const string Armazenamento = "https://objects.githubusercontent.com";

    /// <summary>O corpo da falha: nada dele pode chegar ao cliente da API.</summary>
    public const string CorpoDaFalha = "{\"message\":\"segredo-interno-do-github\"}";

    /// <summary>Um pedido que chegou: o endereço e o Authorization (nulo quando não veio).</summary>
    public sealed record Pedido(Uri Endereco, string? Authorization, string Aceita);

    private readonly List<Pedido> _pedidos = [];

    public IReadOnlyList<Pedido> Pedidos
    {
        get
        {
            lock (_pedidos)
                return _pedidos.ToArray();
        }
    }

    /// <summary>O zip da v136 (bytes que o teste confere do outro lado, maior que um buffer de cópia).</summary>
    public byte[] Zip { get; } = Enumerable.Range(0, 200_000).Select(i => (byte)(i % 251)).ToArray();

    /// <summary>O hash publicado no <c>devkit-136.zip.sha256</c>.</summary>
    public const string Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>O GitHub fora do ar: toda resposta é 500 com o <see cref="CorpoDaFalha"/>.</summary>
    public bool ForaDoAr { get; set; }

    /// <summary>
    /// As releases: a v137 é prerelease (fica de fora), a v136 é a última estável (sem digest, com o
    /// <c>.sha256</c> ao lado), a v135 traz o digest do asset, e o rascunho e a tag ilegível ficam de fora.
    /// </summary>
    public string Releases { get; set; } = $$"""
        [
          { "tag_name": "v137", "name": "dev.kit 137", "draft": false, "prerelease": true, "published_at": "2026-10-10T12:00:00Z",
            "body": "- prévia", "assets": [ { "id": 21, "name": "devkit-137.zip", "size": 10, "digest": null } ] },
          { "tag_name": "v136", "name": "dev.kit 136", "draft": false, "prerelease": false, "published_at": "2026-10-09T18:00:00Z",
            "body": "## Novidades\n- **\"Precisa de você\"** no Board\n- Aviso de [nova versão](https://github.com/x) no app\n\nObrigado!",
            "assets": [
              { "id": 12, "name": "devkit-136.zip.sha256", "size": 80, "digest": null },
              { "id": 11, "name": "devkit-136.zip", "size": 200000, "digest": null } ] },
          { "tag_name": "nightly", "name": "nightly", "draft": false, "prerelease": false, "published_at": "2026-10-09T19:00:00Z",
            "body": "", "assets": [ { "id": 31, "name": "devkit-nightly.zip", "size": 10, "digest": null } ] },
          { "tag_name": "v138", "name": "rascunho", "draft": true, "prerelease": false, "published_at": null,
            "body": "", "assets": [ { "id": 41, "name": "devkit-138.zip", "size": 10, "digest": null } ] },
          { "tag_name": "v135", "name": "", "draft": false, "prerelease": false, "published_at": "2026-10-01T18:00:00Z",
            "body": "", "assets": [ { "id": 13, "name": "devkit-135.zip", "size": 4, "digest": "sha256:{{new string('A', 64)}}" } ] }
        ]
        """;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var endereco = request.RequestUri!;
        lock (_pedidos)
            _pedidos.Add(new Pedido(endereco, request.Headers.Authorization?.ToString(), request.Headers.Accept.ToString()));

        if (ForaDoAr)
            return Responder(HttpStatusCode.InternalServerError, new StringContent(CorpoDaFalha, Encoding.UTF8, "application/json"));

        var caminho = endereco.GetLeftPart(UriPartial.Path);
        // A API do GitHub exige o token; o armazenamento usa a URL assinada (e recusaria o Authorization).
        if (caminho.StartsWith(Api, StringComparison.Ordinal) && request.Headers.Authorization is null)
            return Responder(HttpStatusCode.Unauthorized, new StringContent(CorpoDaFalha));
        if (caminho.StartsWith(Armazenamento, StringComparison.Ordinal) && request.Headers.Authorization is not null)
            return Responder(HttpStatusCode.BadRequest, new StringContent("Only one auth mechanism allowed"));

        return caminho switch
        {
            $"{Api}/releases" => Responder(HttpStatusCode.OK, new StringContent(Releases, Encoding.UTF8, "application/json")),
            $"{Api}/releases/assets/11" => Redirecionar($"{Armazenamento}/github-production-release-asset/devkit-136.zip?X-Amz-Signature=assinada"),
            $"{Api}/releases/assets/12" => Redirecionar($"{Armazenamento}/github-production-release-asset/devkit-136.zip.sha256?X-Amz-Signature=assinada"),
            $"{Api}/releases/assets/13" => Responder(HttpStatusCode.OK, new ByteArrayContent([1, 2, 3, 4])),
            $"{Armazenamento}/github-production-release-asset/devkit-136.zip" => Responder(HttpStatusCode.OK, Conteudo(Zip, "application/octet-stream")),
            $"{Armazenamento}/github-production-release-asset/devkit-136.zip.sha256" => Responder(HttpStatusCode.OK, new StringContent($"{Sha256}  devkit-136.zip\n")),
            _ => Responder(HttpStatusCode.NotFound, new StringContent(CorpoDaFalha)),
        };
    }

    private static ByteArrayContent Conteudo(byte[] bytes, string tipo)
    {
        var conteudo = new ByteArrayContent(bytes);
        conteudo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(tipo);
        conteudo.Headers.ContentLength = bytes.Length;
        return conteudo;
    }

    private static Task<HttpResponseMessage> Redirecionar(string destino)
    {
        var resposta = new HttpResponseMessage(HttpStatusCode.Found);
        resposta.Headers.Location = new Uri(destino);
        return Task.FromResult(resposta);
    }

    private static Task<HttpResponseMessage> Responder(HttpStatusCode status, HttpContent conteudo)
        => Task.FromResult(new HttpResponseMessage(status) { Content = conteudo });

    /// <summary>O handler é do teste, e não do pool do HttpClientFactory: descartá-lo não faz nada.</summary>
    protected override void Dispose(bool disposing)
    {
    }
}
