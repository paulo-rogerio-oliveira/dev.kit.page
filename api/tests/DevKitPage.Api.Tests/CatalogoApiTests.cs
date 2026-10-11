using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O catálogo (US #425) de ponta a ponta. O BÁSICO pelo mesmo caminho das releases do app — a delta e o 304, o zip em
/// streaming sem o token, a vitrine, as tags <c>catalogo-v*</c> fora das versões do app; o da EMPRESA — publicar,
/// listar, retirar, assinar e desistir, com o 403 entre empresas e o gestor retirando o item de outro autor.
/// </summary>
public sealed class CatalogoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Delta_traz_o_que_mudou_depois_da_versao_e_304_sem_mudanca()
    {
        var cliente = _api.CreateClient();

        var tudo = (await cliente.GetFromJsonAsync<CatalogoBaseV1>("/api/catalogo/base"))!;
        var desde1 = await cliente.GetAsync("/api/catalogo/base?desde=1");
        var semMudanca = await cliente.GetAsync("/api/catalogo/base?desde=2");

        Assert.Equal((0, 2, 4), (tudo.Desde, tudo.Manifesto.Versao, tudo.Manifesto.Itens.Count));
        Assert.Equal(("/api/catalogo/base/download", new string('b', 64)), (tudo.UrlDownload, tudo.Sha256));
        var delta = (await desde1.Content.ReadFromJsonAsync<CatalogoBaseV1>())!;
        Assert.Equal(new[] { "revisar-alteracoes", "tarefa-avulsa" }, delta.Manifesto.Itens.Select(i => i.Nome));
        Assert.Equal("descrever-processos", Assert.Single(delta.Manifesto.Retirados).Nome);
        Assert.Equal(new[] { "r1", "r2" }, delta.Manifesto.Itens[0].Hashes); // os hashes de todas as versões: a máquina reconhece o básico
        Assert.Equal(HttpStatusCode.NotModified, semMudanca.StatusCode);
        Assert.Equal("\"catalogo-v2\"", semMudanca.Headers.ETag!.Tag);
        // A prévia (catalogo-v3 prerelease) não é a última; o token não volta ao cliente.
        Assert.DoesNotContain(ApiDeTeste.TokenDoGitHub, await desde1.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Download_e_anonimo_em_streaming_e_o_token_nao_vai_ao_armazenamento()
    {
        var resposta = await _api.CreateClient().GetAsync("/api/catalogo/base/download", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("catalogo-2.zip", resposta.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal(_api.GitHub.ZipDoCatalogo, await resposta.Content.ReadAsByteArrayAsync());
        var armazenamento = Assert.Single(_api.GitHub.Pedidos, p => p.Endereco.AbsolutePath.EndsWith("catalogo-2.zip", StringComparison.Ordinal));
        Assert.Null(armazenamento.Authorization);
        Assert.All(_api.GitHub.Pedidos.Where(p => p.Endereco.Host == "api.github.com"), p => Assert.Equal($"Bearer {ApiDeTeste.TokenDoGitHub}", p.Authorization));
    }

    [Fact]
    public async Task Vitrine_e_publica_e_agrupada_por_tecnologia()
    {
        var vitrine = (await _api.CreateClient().GetFromJsonAsync<VitrineV1>("/api/catalogo/base/vitrine"))!;

        Assert.Equal(2, vitrine.Versao);
        Assert.Equal(new[] { "Angular", "dev.kit", "Geral", "Git" }, vitrine.Grupos.Select(g => g.Tecnologia));
    }

    [Fact]
    public async Task Tags_do_catalogo_ficam_fora_das_versoes_do_app_e_o_catalogo_usa_a_mesma_lista_em_cache()
    {
        var cliente = _api.CreateClient();

        var ultima = (await cliente.GetFromJsonAsync<VersaoV1>("/api/versoes/ultima"))!;
        await cliente.GetAsync("/api/catalogo/base");
        await cliente.GetAsync("/api/catalogo/base/vitrine");

        Assert.Equal("v136", ultima.Tag);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/api/versoes/2/download")).StatusCode); // catalogo-v2 não é a versão 2
        Assert.Single(_api.GitHub.Pedidos, p => p.Endereco.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sem_REPO_KEY_ou_com_o_manifesto_torto_e_503()
    {
        using var semChave = new ApiDeTeste(repoKey: null);
        _api.GitHub.Manifesto2 = """{ "versao": 7, "minDevKit": 1, "itens": [], "retirados": [] }""";

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await semChave.CreateClient().GetAsync("/api/catalogo/base")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _api.CreateClient().GetAsync("/api/catalogo/base")).StatusCode); // versão 7 numa tag catalogo-v2
    }

    [Fact]
    public async Task Releases_do_catalogo_sao_so_do_admin()
    {
        var admin = await _api.AdminAsync();
        var (dev, _) = await _api.UsuarioAsync(admin, "ana.dev", Papeis.Dev);

        var lista = (await admin.GetFromJsonAsync<ReleaseDoCatalogoV1[]>("/api/catalogo/base/releases"))!;

        Assert.Equal(new[] { ("catalogo-v2", 4, 1), ("catalogo-v1", 1, 0) }, lista.Select(r => (r.Tag, r.Itens, r.Retirados)));
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/catalogo/base/releases")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/catalogo/base/releases")).StatusCode);
    }

    private static PublicacaoV1 Skill(string nome, string conteudo = "{\"name\":\"x\"}", string[]? deps = null, string tipo = TiposDeItem.Skill)
        => new(tipo, nome, $"Sobre {nome}", conteudo, deps);

    [Fact]
    public async Task Publicar_listar_assinar_e_desistir_no_catalogo_da_empresa()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");

        var criada = await ana.PostAsJsonAsync("/api/catalogo/empresa", Skill("compilar-uau"));
        var igual = await ana.PostAsJsonAsync("/api/catalogo/empresa", Skill("compilar-uau"));
        var fluxo = (await (await ana.PostAsJsonAsync("/api/catalogo/empresa", Skill("entrega-uau", "{}", ["skill:compilar-uau"], TiposDeItem.Fluxo))).Content.ReadFromJsonAsync<PublicacaoFeitaV1>())!;

        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        Assert.Equal(HttpStatusCode.OK, igual.StatusCode);
        Assert.False((await igual.Content.ReadFromJsonAsync<PublicacaoFeitaV1>())!.Nova);

        var assinatura = (await (await bia.PostAsync($"/api/catalogo/empresa/{fluxo.Id}/assinatura", null)).Content.ReadFromJsonAsync<AssinaturaFeitaV1>())!;
        Assert.Equal(new[] { "skill:compilar-uau" }, assinatura.Trouxe);
        var lista = (await bia.GetFromJsonAsync<ItemDaEmpresaV1[]>("/api/catalogo/empresa"))!;
        Assert.All(lista, i => Assert.True(i.Assinado));
        Assert.Equal("Nome de ana@a.com", lista[0].Autor);
        Assert.Equal(2, (await bia.GetFromJsonAsync<ItemAssinadoV1[]>("/api/catalogo/empresa/assinados"))!.Length);

        (await bia.DeleteAsync($"/api/catalogo/empresa/{fluxo.Id}/assinatura")).EnsureSuccessStatusCode();
        Assert.Equal(new[] { "compilar-uau" }, (await bia.GetFromJsonAsync<ItemAssinadoV1[]>("/api/catalogo/empresa/assinados"))!.Select(i => i.Nome));
    }

    [Fact]
    public async Task Colisao_com_o_basico_e_409_e_o_nome_invalido_400()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");

        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsJsonAsync("/api/catalogo/empresa", Skill("revisar-alteracoes"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/api/catalogo/empresa", Skill("Nome Invalido"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/api/catalogo/empresa", Skill("grande", new string('x', 300 * 1024)))).StatusCode);
    }

    [Fact]
    public async Task Entre_empresas_e_403_o_gestor_retira_o_item_de_outro_autor_e_o_dev_nao()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var item = (await (await ana.PostAsJsonAsync("/api/catalogo/empresa", Skill("compilar-uau"))).Content.ReadFromJsonAsync<PublicacaoFeitaV1>())!;

        // A empresa B não vê, não assina e não retira o item da A — e a lista dela fica vazia.
        Assert.Equal(HttpStatusCode.Forbidden, (await caio.PostAsync($"/api/catalogo/empresa/{item.Id}/assinatura", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await caio.DeleteAsync($"/api/catalogo/empresa/{item.Id}")).StatusCode);
        Assert.Empty((await caio.GetFromJsonAsync<ItemDaEmpresaV1[]>("/api/catalogo/empresa"))!);
        // O admin não é de empresa nenhuma: 403 no catálogo da empresa.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/catalogo/empresa")).StatusCode);
        // O dev que não é o autor não retira; o gestor da empresa retira.
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.DeleteAsync($"/api/catalogo/empresa/{item.Id}")).StatusCode);
        var retirado = await gestor.DeleteAsync($"/api/catalogo/empresa/{item.Id}");
        Assert.Equal(HttpStatusCode.OK, retirado.StatusCode);
        Assert.NotNull((await retirado.Content.ReadFromJsonAsync<ItemDaEmpresaV1>())!.Retirado);
        Assert.Equal(HttpStatusCode.BadRequest, (await bia.PostAsync($"/api/catalogo/empresa/{item.Id}/assinatura", null)).StatusCode); // retirado não se assina
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PostAsync("/api/catalogo/empresa/999/assinatura", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/catalogo/empresa")).StatusCode);
    }
}
