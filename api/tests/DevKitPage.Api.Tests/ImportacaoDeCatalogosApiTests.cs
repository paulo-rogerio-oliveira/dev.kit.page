using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

public sealed class ImportacaoDeCatalogosApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static ImportarCatalogoV1 Local(string slug = "importado")
    {
        var skill = FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo("exemplo-importado", "Exemplo", "Faça o trabalho.", [], [], false, false, false));
        var manifesto = GeradorDoManifesto.Gerar(GeradorDoManifesto.Vazio,
            [GeradorDoManifesto.Ler(TiposDeItem.Skill, "exemplo-importado", "skills/exemplo-importado.json", skill)], 160).Manifesto;
        return new(new CatalogoNovoV1(slug, "Importado", null),
            [new("manifesto.json", ManifestoJson.Serializar(manifesto)), new("skills/exemplo-importado.json", skill)]);
    }

    [Fact]
    public async Task Local_persiste_em_outra_conexao_publica_e_baixa_com_conteudo_e_origem()
    {
        var admin = await _api.AdminAsync();
        var resposta = await admin.PostAsJsonAsync("/api/catalogos/importar", Local());
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var importado = (await resposta.Content.ReadFromJsonAsync<CatalogoImportadoV1>())!;
        Assert.Equal((1, 0, 0, 1, "Pasta local"), (importado.Skills, importado.Rules, importado.Fluxos, importado.Catalogo.Pendentes, importado.Catalogo.Origem));
        using (var escopo = _api.Services.CreateScope())
        {
            var opcoes = escopo.ServiceProvider.GetRequiredService<DbContextOptions<DevKitPageDb>>();
            await using var reaberto = new DevKitPageDb(opcoes);
            var catalogo = await reaberto.Catalogos.AsNoTracking().SingleAsync(c => c.Slug == "importado");
            Assert.Equal("Pasta local", catalogo.Origem);
            Assert.Equal(160, catalogo.MinDevKitDaOrigem);
            Assert.Contains("Faça o trabalho.", (await reaberto.ItensEditaveis.SingleAsync(i => i.CatalogoId == catalogo.Id)).Conteudo);
        }
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/catalogos/importado/publicar", new PublicarCatalogoV1())).StatusCode);
        var lista = (await admin.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!;
        Assert.Equal((1, 0), (lista.Single(c => c.Slug == "importado").Versao, lista.Single(c => c.Slug == "importado").Pendentes));
        using var pacote = new ZipArchive(new MemoryStream(await admin.GetByteArrayAsync("/api/catalogos/importado/download")));
        using (var manifesto = new StreamReader(pacote.GetEntry("manifesto.json")!.Open()))
            Assert.Equal(160, ManifestoJson.Ler(await manifesto.ReadToEndAsync()).MinDevKit);
        using var leitor = new StreamReader(pacote.GetEntry("skills/exemplo-importado.json")!.Open());
        Assert.Contains("Faça o trabalho.", await leitor.ReadToEndAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/catalogos/importar", Local())).StatusCode);
    }

    [Theory]
    [InlineData("../segredo.json")]
    [InlineData("/absoluto.json")]
    [InlineData("C:/segredo.json")]
    [InlineData("skills\\fora.json")]
    public async Task Recusa_caminhos_fora_da_pasta_sem_gravar_catalogo(string caminho)
    {
        var admin = await _api.AdminAsync();
        var pedido = Local() with { Arquivos = [.. Local().Arquivos!, new(caminho, "{}")] };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/catalogos/importar", pedido)).StatusCode);
        Assert.Single((await admin.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!);
    }

    [Fact]
    public async Task Importa_os_tres_tipos_sob_base_com_categoria_e_dependencias_e_publica()
    {
        var admin = await _api.AdminAsync();
        var skill = Local().Arquivos![1].Conteudo;
        var plan = FormatosDoCatalogo.ExportarPlan(new PlanDoCatalogo("regra-importada", "Regra", "Valide a entrega.", "", false, false, false));
        const string fluxo = "{\"formato\":\"devkit-fluxo\",\"versao\":1,\"nome\":\"Fluxo importado\",\"desenho\":{\"nodes\":[{\"id\":\"e1\",\"kind\":\"Skill\",\"skillName\":\"exemplo-importado\"}]},\"agentes\":[],\"planos\":[]}";
        var lidos = new[]
        {
            GeradorDoManifesto.Ler(TiposDeItem.Skill, "exemplo-importado", "skills/exemplo-importado.json", skill, "Minha categoria"),
            GeradorDoManifesto.Ler(TiposDeItem.Plan, "regra-importada", "plans/regra-importada.json", plan),
            GeradorDoManifesto.Ler(TiposDeItem.Fluxo, "fluxo-importado", "fluxos/fluxo-importado.json", fluxo),
        };
        var manifesto = GeradorDoManifesto.Gerar(GeradorDoManifesto.Vazio, lidos, 170).Manifesto;
        var resposta = await admin.PostAsJsonAsync("/api/catalogos/importar", Local() with { Arquivos =
            [new("manifesto.json", ManifestoJson.Serializar(manifesto)), new("base/skills/exemplo-importado.json", skill),
                new("base/plans/regra-importada.json", plan), new("base/fluxos/fluxo-importado.json", fluxo)] });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var importado = (await resposta.Content.ReadFromJsonAsync<CatalogoImportadoV1>())!;
        Assert.Equal((1, 1, 1), (importado.Skills, importado.Rules, importado.Fluxos));
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/catalogos/importado/publicar", new PublicarCatalogoV1(1))).StatusCode);
        var publicado = (await admin.GetFromJsonAsync<ManifestoDoCatalogoV1>("/api/catalogos/importado/releases/1/manifesto"))!;
        Assert.Equal(170, publicado.MinDevKit);
        Assert.Equal("Minha categoria", publicado.Itens.Single(i => i.Tipo == TiposDeItem.Skill).Tecnologia);
        Assert.Contains("skill:exemplo-importado", publicado.Itens.Single(i => i.Tipo == TiposDeItem.Fluxo).Dependencias);
    }

    [Fact]
    public async Task Recusa_hash_adulterado_duplicatas_manifesto_invalido_e_arquivo_grande_atomicamente()
    {
        var admin = await _api.AdminAsync();
        var local = Local();
        var skill = local.Arquivos![1];
        var casos = new IReadOnlyList<ArquivoDeCatalogoV1>[]
        {
            [local.Arquivos[0], skill with { Conteudo = skill.Conteudo.Replace("Faça", "Ignore", StringComparison.Ordinal) }],
            [.. local.Arquivos, skill],
            [new("manifesto.json", "{}")],
            [new("manifesto.json", "{ ruim")],
            [new("manifesto.json", new string('x', 1024 * 1024 + 1))],
        };
        foreach (var arquivos in casos)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/catalogos/importar", local with { Arquivos = arquivos })).StatusCode);
            Assert.Single((await admin.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!);
        }
    }

    [Fact]
    public async Task Importacao_exige_admin_inclusive_github()
    {
        var admin = await _api.AdminAsync();
        var (dev, _) = await _api.UsuarioAsync(admin, "dev-importador", Papeis.Dev);
        foreach (var pedido in new[] { Local(), Local() with { Arquivos = null, Repositorio = "org/repo" } })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().PostAsJsonAsync("/api/catalogos/importar", pedido)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await dev.PostAsJsonAsync("/api/catalogos/importar", pedido)).StatusCode);
        }
    }

    private sealed class GitHubDoCatalogo(byte[] zip, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<Uri> Pedidos { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Null(request.Headers.Authorization);
            Pedidos.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(zip) });
        }
    }

    private static byte[] Pacote(string prefixo = "repo-main/catalogo/", string? caminhoExtra = null)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
        {
            foreach (var arquivo in Local().Arquivos!)
            {
                using var escritor = new StreamWriter(zip.CreateEntry(prefixo + arquivo.Caminho).Open(), new UTF8Encoding(false));
                escritor.Write(arquivo.Conteudo);
            }
            if (caminhoExtra is not null) zip.CreateEntry(caminhoExtra);
        }
        return memoria.ToArray();
    }

    [Fact]
    public async Task Github_importa_pasta_e_branch_sem_credencial_e_publica()
    {
        var github = new GitHubDoCatalogo(Pacote());
        using var api = _api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient(ImportacaoDeCatalogos.ClienteGitHub).ConfigurePrimaryHttpMessageHandler(() => github)));
        var admin = await _api.AdminAsync();
        using var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
        var resposta = await cliente.PostAsJsonAsync("/api/catalogos/importar", Local() with
            { Arquivos = null, Repositorio = "https://github.com/org/repo", Referencia = "feature/teste", Pasta = "catalogo" });
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("https://codeload.github.com/org/repo/zip/feature%2Fteste", Assert.Single(github.Pedidos).AbsoluteUri);
        var importado = (await resposta.Content.ReadFromJsonAsync<CatalogoImportadoV1>())!;
        Assert.Equal("GitHub · org/repo@feature/teste:catalogo", importado.Catalogo.Origem);
        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync("/api/catalogos/importado/publicar", new PublicarCatalogoV1())).StatusCode);
    }

    [Theory]
    [InlineData("http://localhost/interno", "main", "")]
    [InlineData("org/repo", "../segredo", "")]
    [InlineData("org/repo", "main", "../fora")]
    public async Task Github_valida_entrada_antes_de_acessar_rede(string repo, string referencia, string pasta)
    {
        var admin = await _api.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/catalogos/importar", Local() with
            { Arquivos = null, Repositorio = repo, Referencia = referencia, Pasta = pasta })).StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task Github_indisponivel_ou_redirect_nao_cria_catalogo(HttpStatusCode status)
    {
        var github = new GitHubDoCatalogo([], status);
        using var api = _api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient(ImportacaoDeCatalogos.ClienteGitHub).ConfigurePrimaryHttpMessageHandler(() => github)));
        var admin = await _api.AdminAsync();
        using var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync("/api/catalogos/importar", Local() with { Arquivos = null, Repositorio = "org/repo" })).StatusCode);
        Assert.Single((await cliente.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Github_zip_invalido_ou_caminho_inseguro_nao_persiste(bool caminhoInseguro)
    {
        var github = new GitHubDoCatalogo(caminhoInseguro ? Pacote(caminhoExtra: "repo-main/../segredo.json") : [1, 2, 3]);
        using var api = _api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient(ImportacaoDeCatalogos.ClienteGitHub).ConfigurePrimaryHttpMessageHandler(() => github)));
        var admin = await _api.AdminAsync();
        using var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PostAsJsonAsync("/api/catalogos/importar", Local() with
            { Arquivos = null, Repositorio = "org/repo", Pasta = "catalogo" })).StatusCode);
        Assert.Single((await cliente.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!);
    }

    [Fact]
    public async Task Github_limita_os_bytes_reais_do_download_antes_de_abrir_o_zip()
    {
        var github = new GitHubDoCatalogo(new byte[ImportacaoDeCatalogos.MaximoBytes + 1]);
        using var api = _api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHttpClient(ImportacaoDeCatalogos.ClienteGitHub).ConfigurePrimaryHttpMessageHandler(() => github)));
        var admin = await _api.AdminAsync();
        using var cliente = api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
        var resposta = await cliente.PostAsJsonAsync("/api/catalogos/importar", Local() with { Arquivos = null, Repositorio = "org/repo" });
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var problema = await resposta.Content.ReadAsStringAsync();
        Assert.True(problema.Contains("limite de tamanho", StringComparison.Ordinal), problema);
        Assert.Single((await cliente.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!);
    }
}
