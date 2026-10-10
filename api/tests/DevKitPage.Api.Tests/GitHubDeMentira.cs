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
            "body": "", "assets": [ { "id": 13, "name": "devkit-135.zip", "size": 4, "digest": "sha256:{{new string('A', 64)}}" } ] },
          { "tag_name": "catalogo-v3", "name": "prévia do catálogo", "draft": false, "prerelease": true, "published_at": "2026-10-10T09:00:00Z",
            "body": "", "assets": [ { "id": 61, "name": "catalogo-3.zip", "size": 10, "digest": null }, { "id": 62, "name": "manifesto.json", "size": 10, "digest": null } ] },
          { "tag_name": "catalogo-v2", "name": "Catálogo 2", "draft": false, "prerelease": false, "published_at": "2026-10-08T12:00:00Z",
            "body": "", "assets": [
              { "id": 51, "name": "catalogo-2.zip", "size": 3000, "digest": "sha256:{{new string('b', 64)}}" },
              { "id": 52, "name": "manifesto.json", "size": 900, "digest": null } ] },
          { "tag_name": "catalogo-v1", "name": "Catálogo 1", "draft": false, "prerelease": false, "published_at": "2026-10-02T12:00:00Z",
            "body": "", "assets": [
              { "id": 53, "name": "catalogo-1.zip", "size": 2000, "digest": null },
              { "id": 54, "name": "manifesto.json", "size": 600, "digest": null } ] }
        ]
        """;

    /// <summary>O zip do catálogo 2 (US #425), que o download do catálogo transmite.</summary>
    public byte[] ZipDoCatalogo { get; } = Enumerable.Range(0, 3000).Select(i => (byte)(i % 13)).ToArray();

    /// <summary>
    /// O manifesto do catálogo 2: a skill do Angular e o plan geral vieram no catálogo 1; a revisão (versão 2) e o
    /// fluxo mudaram no 2; a skill que saiu foi retirada no 2.
    /// </summary>
    public string Manifesto2 { get; set; } = """
        {
          "versao": 2, "minDevKit": 151,
          "itens": [
            { "tipo": "skill", "nome": "compilar-angular", "versao": 1, "hash": "a1", "catalogo": 1, "arquivo": "skills/compilar-angular.json",
              "descricao": "Compila o Angular", "tecnologia": "Angular", "minDevKit": 151, "requer": ["node"], "dependencias": [], "hashes": ["a1"] },
            { "tipo": "skill", "nome": "revisar-alteracoes", "versao": 2, "hash": "r2", "catalogo": 2, "arquivo": "skills/revisar-alteracoes.json",
              "descricao": "Revisa antes do commit", "tecnologia": "Git", "minDevKit": 151, "requer": ["git"], "dependencias": [], "hashes": ["r1", "r2"] },
            { "tipo": "plan", "nome": "geral", "versao": 1, "hash": "p1", "catalogo": 1, "arquivo": "plans/geral.json",
              "descricao": "Regras gerais", "tecnologia": "", "minDevKit": 151, "requer": [], "dependencias": [], "hashes": ["p1"] },
            { "tipo": "fluxo", "nome": "tarefa-avulsa", "versao": 1, "hash": "f1", "catalogo": 2, "arquivo": "fluxos/tarefa-avulsa.json",
              "descricao": "Uma tarefa sem work item", "tecnologia": "dev.kit", "minDevKit": 151, "requer": [], "dependencias": ["skill:revisar-alteracoes"], "hashes": ["f1"] }
          ],
          "retirados": [ { "tipo": "skill", "nome": "descrever-processos", "versao": 3, "catalogo": 2 } ]
        }
        """;

    private const string Manifesto1 = """
        { "versao": 1, "minDevKit": 151, "itens": [
            { "tipo": "skill", "nome": "compilar-angular", "versao": 1, "hash": "a1", "catalogo": 1, "arquivo": "skills/compilar-angular.json",
              "descricao": "Compila o Angular", "tecnologia": "Angular", "minDevKit": 151, "requer": [], "dependencias": [], "hashes": ["a1"] } ],
          "retirados": [] }
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
            // O catálogo básico (US #425): o zip passa pelo armazenamento como o do app; os manifestos vêm direto.
            $"{Api}/releases/assets/51" => Redirecionar($"{Armazenamento}/github-production-release-asset/catalogo-2.zip?X-Amz-Signature=assinada"),
            $"{Armazenamento}/github-production-release-asset/catalogo-2.zip" => Responder(HttpStatusCode.OK, Conteudo(ZipDoCatalogo, "application/octet-stream")),
            $"{Api}/releases/assets/52" => Responder(HttpStatusCode.OK, new StringContent(Manifesto2, Encoding.UTF8, "application/octet-stream")),
            $"{Api}/releases/assets/54" => Responder(HttpStatusCode.OK, new StringContent(Manifesto1, Encoding.UTF8, "application/octet-stream")),
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
