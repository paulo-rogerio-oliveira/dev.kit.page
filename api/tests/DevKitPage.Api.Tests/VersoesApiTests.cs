using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// As versões do dev.kit pelas GitHub Releases (US #405, #406), de ponta a ponta no processo com o GitHub
/// de mentira: a última estável anônima, o download autenticado em streaming, o token que vai ao
/// api.github.com e NUNCA ao cliente nem ao armazenamento, o 503 genérico e o 404.
/// </summary>
public sealed class VersoesApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Ultima_e_anonima_e_traz_a_versao_estavel_com_destaques_tamanho_e_sha()
    {
        var resposta = await _api.CreateClient().GetAsync("/api/versoes/ultima");
        var texto = await resposta.Content.ReadAsStringAsync();
        var versao = (await resposta.Content.ReadFromJsonAsync<VersaoV1>())!;

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal((136, "v136", "dev.kit 136"), (versao.Versao, versao.Tag, versao.Nome)); // a v137 é prerelease; o rascunho e a "nightly" ficam de fora
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 18, 0, 0, TimeSpan.Zero), versao.PublicadaEm);
        Assert.Equal(new[] { "\"Precisa de você\" no Board", "Aviso de nova versão no app" }, versao.Destaques);
        Assert.Equal((200_000L, GitHubDeMentira.Sha256), (versao.TamanhoBytes, versao.Sha256));
        Assert.Equal(("/api/versoes/136/download", false), (versao.UrlDownload, versao.LoginObrigatorio));
        // O token foi ao GitHub, e nada dele (nem o endereço do GitHub) volta ao cliente.
        Assert.All(_api.GitHub.Pedidos.Where(p => p.Endereco.Host == "api.github.com"),
            p => Assert.Equal($"Bearer {ApiDeTeste.TokenDoGitHub}", p.Authorization));
        Assert.DoesNotContain(ApiDeTeste.TokenDoGitHub, texto);
        Assert.DoesNotContain("github", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(resposta.Headers.Concat(resposta.Content.Headers), h => h.Value.Any(v => v.Contains(ApiDeTeste.TokenDoGitHub, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Ultima_fica_em_cache_e_nao_consulta_o_github_a_cada_visita()
    {
        var cliente = _api.CreateClient();

        for (var i = 0; i < 3; i++)
            (await cliente.GetAsync("/api/versoes/ultima")).EnsureSuccessStatusCode();

        Assert.Single(_api.GitHub.Pedidos, p => p.Endereco.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Download_exige_token()
    {
        var resposta = await _api.CreateClient().GetAsync("/api/versoes/136/download");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.DoesNotContain(_api.GitHub.Pedidos, p => p.Endereco.AbsolutePath.Contains("/assets/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Download_transmite_o_zip_e_o_token_nao_vai_ao_armazenamento_nem_volta_ao_cliente()
    {
        var admin = await _api.AdminAsync();

        var resposta = await admin.GetAsync("/api/versoes/136/download", HttpCompletionOption.ResponseHeadersRead);
        var bytes = await resposta.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("application/zip", resposta.Content.Headers.ContentType!.MediaType);
        Assert.Equal("devkit-136.zip", resposta.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal(_api.GitHub.Zip.Length, resposta.Content.Headers.ContentLength);
        Assert.Equal(_api.GitHub.Zip, bytes);

        var asset = Assert.Single(_api.GitHub.Pedidos, p => p.Endereco.AbsolutePath.EndsWith("/releases/assets/11", StringComparison.Ordinal));
        Assert.Equal($"Bearer {ApiDeTeste.TokenDoGitHub}", asset.Authorization);
        Assert.Contains("application/octet-stream", asset.Aceita);
        var armazenamento = Assert.Single(_api.GitHub.Pedidos, p => p.Endereco.Host == "objects.githubusercontent.com" && p.Endereco.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal));
        Assert.Null(armazenamento.Authorization); // o 302 foi seguido SEM o token
        Assert.DoesNotContain(resposta.Headers.Concat(resposta.Content.Headers), h => h.Value.Any(v => v.Contains("github", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Download_de_versao_antiga_estavel_funciona_e_inexistente_ou_prerelease_e_404()
    {
        var admin = await _api.AdminAsync();

        var antiga = await admin.GetAsync("/api/versoes/135/download");
        var inexistente = await admin.GetAsync("/api/versoes/999/download");
        var prerelease = await admin.GetAsync("/api/versoes/137/download");

        Assert.Equal(HttpStatusCode.OK, antiga.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await antiga.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, prerelease.StatusCode);
    }

    [Fact]
    public async Task Sem_REPO_KEY_e_503_sem_chamar_o_github()
    {
        using var semChave = new ApiDeTeste(repoKey: null);
        var admin = await semChave.AdminAsync();

        var ultima = await semChave.CreateClient().GetAsync("/api/versoes/ultima");
        var download = await admin.GetAsync("/api/versoes/136/download");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ultima.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, download.StatusCode);
        Assert.Contains("indisponível", await ultima.Content.ReadAsStringAsync());
        Assert.Empty(semChave.GitHub.Pedidos);
    }

    [Fact]
    public async Task GitHub_fora_do_ar_e_503_generico_sem_o_corpo_do_github_e_sem_cache_da_falha()
    {
        _api.GitHub.ForaDoAr = true;
        var cliente = _api.CreateClient();

        var fora = await cliente.GetAsync("/api/versoes/ultima");
        var texto = await fora.Content.ReadAsStringAsync();
        _api.GitHub.ForaDoAr = false;
        var deVolta = await cliente.GetAsync("/api/versoes/ultima");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, fora.StatusCode);
        Assert.DoesNotContain("segredo-interno-do-github", texto);
        Assert.DoesNotContain(ApiDeTeste.TokenDoGitHub, texto);
        Assert.Equal(HttpStatusCode.OK, deVolta.StatusCode); // a falha não ficou em cache
    }

    [Fact]
    public async Task Sem_release_estavel_com_zip_e_503()
    {
        _api.GitHub.Releases = """[ { "tag_name": "v137", "draft": false, "prerelease": true, "published_at": "2026-10-10T12:00:00Z", "assets": [ { "id": 21, "name": "devkit-137.zip", "size": 1 } ] } ]""";

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _api.CreateClient().GetAsync("/api/versoes/ultima")).StatusCode);
    }

    [Fact]
    public async Task Login_obrigatorio_reflete_a_variavel_na_politica_e_na_ultima_versao()
    {
        using var obrigatorio = new ApiDeTeste(loginObrigatorio: true);

        var desligada = (await _api.CreateClient().GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!;
        var ligada = (await obrigatorio.CreateClient().GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!;
        var versao = (await obrigatorio.CreateClient().GetFromJsonAsync<VersaoV1>("/api/versoes/ultima"))!;

        Assert.False(desligada.LoginObrigatorio);
        Assert.True(ligada.LoginObrigatorio);
        Assert.True(versao.LoginObrigatorio);
    }

    [Fact]
    public async Task Politica_nao_depende_do_github()
    {
        using var semChave = new ApiDeTeste(repoKey: null, loginObrigatorio: true);

        var politica = await semChave.CreateClient().GetAsync("/api/auth/politica");

        Assert.Equal(HttpStatusCode.OK, politica.StatusCode);
        Assert.True((await politica.Content.ReadFromJsonAsync<PoliticaDeLoginV1>())!.LoginObrigatorio);
    }

    [Fact]
    public async Task Dev_baixa_a_versao_e_quem_tem_a_troca_pendente_ou_foi_bloqueado_nao()
    {
        var admin = await _api.AdminAsync();
        var (dev, login) = await _api.UsuarioAsync(admin, "ana.dev", Papeis.Dev);
        // Um dev que ainda não trocou a senha temporária: o token dele só abre a troca.
        var criado = (await (await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("bia.dev", "", Papeis.Dev, null))).Content.ReadFromJsonAsync<UsuarioComSenha>())!;
        var comTroca = _api.CreateClient();
        var token = (await (await comTroca.PostAsJsonAsync("/api/auth/login", new LoginRequest("bia.dev", criado.SenhaTemporaria))).Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        comTroca.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(Papeis.Dev, login.Papel);
        Assert.Equal(HttpStatusCode.OK, (await dev.GetAsync("/api/versoes/136/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await comTroca.GetAsync("/api/versoes/136/download")).StatusCode);

        // Bloqueado pelo admin depois de entrar: o token ainda vale, mas o download não sai.
        (await admin.PutAsJsonAsync($"/api/usuarios/{await IdAsync(admin, "ana.dev")}/bloqueio", new BloqueioDoUsuario(true))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/versoes/136/download")).StatusCode);
    }

    private static async Task<int> IdAsync(HttpClient admin, string login)
        => (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == login).Id;
}
