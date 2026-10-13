using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api.Tests;

/// <summary>
/// Os catálogos nomeados (US #461) de ponta a ponta: a gestão é só do admin; a publicação gera a release com o zip e o
/// manifesto; a distribuição respeita o vínculo com a empresa (o dev da empresa vinculada vê e baixa; o de outra recebe
/// 404, como o anônimo); a delta responde 304; o zip confere com o SHA-256; e as rotas antigas
/// <c>/api/catalogo/base</c> passam a servir o básico publicado no site depois da importação da release do GitHub.
/// </summary>
public sealed class CatalogosApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static string Skill(string nome, string conteudo = "passo 1")
        => FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo(nome, $"Sobre {nome}", conteudo, [], [], false, false, false));

    private static string Fluxo(string nome, params string[] skills)
        => $$"""
            { "formato": "devkit-fluxo", "versao": 1, "nome": "{{nome}}", "descricao": "O fluxo {{nome}}",
              "desenho": { "nodes": [ {{string.Join(", ", skills.Select((s, i) => $$"""{ "id": "e{{i}}", "kind": "Skill", "skillName": "{{s}}" }"""))}} ] },
              "agentes": [], "planos": [] }
            """;

    /// <summary>O admin cria o catálogo com uma skill e um fluxo que a usa, e publica a versão 1.</summary>
    private static async Task<PublicacaoDoCatalogoV1> PublicadoAsync(HttpClient admin, string slug)
    {
        (await admin.PostAsJsonAsync("/api/catalogos", new CatalogoNovoV1(slug, "Ferramentas UAU", "do time"))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/catalogos/{slug}/itens", new ItemEditadoV1(TiposDeItem.Skill, Skill("compilar-uau")))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/catalogos/{slug}/itens", new ItemEditadoV1(TiposDeItem.Fluxo, Fluxo("Entrega UAU", "compilar-uau")))).EnsureSuccessStatusCode();
        var publicada = await admin.PostAsJsonAsync($"/api/catalogos/{slug}/publicar", new PublicarCatalogoV1(160));
        Assert.Equal(HttpStatusCode.Created, publicada.StatusCode);
        return (await publicada.Content.ReadFromJsonAsync<PublicacaoDoCatalogoV1>())!;
    }

    [Fact]
    public async Task A_gestao_e_so_do_admin()
    {
        var admin = await _api.AdminAsync();
        var (dev, _) = await _api.UsuarioAsync(admin, "ana.dev", Papeis.Dev);

        Assert.Equal(HttpStatusCode.Forbidden, (await dev.GetAsync("/api/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dev.PostAsJsonAsync("/api/catalogos", new CatalogoNovoV1("x-y", "X", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/catalogos")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/catalogos", new CatalogoNovoV1("meus", "Reservado", null))).StatusCode);
        var lista = (await admin.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!;
        Assert.Equal(Catalogos.Basico, Assert.Single(lista).Slug);
    }

    [Fact]
    public async Task Publicar_e_distribuir_so_para_a_empresa_vinculada()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var publicada = await PublicadoAsync(admin, "ferramentas-uau");
        (await admin.PostAsJsonAsync("/api/catalogos/ferramentas-uau/empresas", new VinculoV1(a.Id))).EnsureSuccessStatusCode();
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");

        Assert.Equal(("ferramentas-uau-v1", 2), (publicada.Release.Tag, publicada.Release.Itens));
        var meusDaAna = (await ana.GetFromJsonAsync<CatalogoDisponivelV1[]>("/api/catalogos/meus"))!;
        Assert.Equal(new[] { (Catalogos.Basico, true), ("ferramentas-uau", false) }, meusDaAna.Select(c => (c.Slug, c.Basico)));
        Assert.Equal(("Ferramentas UAU", 1), (meusDaAna[1].Nome, meusDaAna[1].Versao));
        Assert.Equal(new[] { Catalogos.Basico }, (await caio.GetFromJsonAsync<CatalogoDisponivelV1[]>("/api/catalogos/meus"))!.Select(c => c.Slug));
        Assert.Equal(new[] { Catalogos.Basico }, (await _api.CreateClient().GetFromJsonAsync<CatalogoDisponivelV1[]>("/api/catalogos/meus"))!.Select(c => c.Slug));

        // A delta da Ana, o 304 de quem já tem a versão e o zip que confere com o SHA-256.
        var delta = (await ana.GetFromJsonAsync<CatalogoBaseV1>("/api/catalogos/ferramentas-uau?desde=0"))!;
        Assert.Equal(new[] { "entrega-uau", "compilar-uau" }, delta.Manifesto.Itens.Select(i => i.Nome));
        Assert.Equal("/api/catalogos/ferramentas-uau/download", delta.UrlDownload);
        var emDia = await ana.GetAsync("/api/catalogos/ferramentas-uau?desde=1");
        Assert.Equal(HttpStatusCode.NotModified, emDia.StatusCode);
        Assert.Equal("\"ferramentas-uau-v1\"", emDia.Headers.ETag!.Tag);
        var zip = await ana.GetByteArrayAsync("/api/catalogos/ferramentas-uau/download");
        Assert.Equal(delta.Sha256, Convert.ToHexStringLower(SHA256.HashData(zip)));
        using (var pacote = new ZipArchive(new MemoryStream(zip)))
            Assert.Equal(new[] { "fluxos/entrega-uau.json", "manifesto.json", "skills/compilar-uau.json" }, pacote.Entries.Select(e => e.FullName).Order());

        // O de outra empresa e o anônimo: 404 em tudo (não se descobre que o catálogo existe).
        foreach (var cliente in new[] { caio, _api.CreateClient() })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/api/catalogos/ferramentas-uau?desde=0")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/api/catalogos/ferramentas-uau/download")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync("/api/catalogos/ferramentas-uau/releases/1/manifesto")).StatusCode);
        }

        // O admin vê o manifesto da release e o histórico.
        Assert.Equal(1, (await admin.GetFromJsonAsync<ManifestoDoCatalogoV1>("/api/catalogos/ferramentas-uau/releases/1/manifesto"))!.Versao);
        Assert.Equal(new[] { 1 }, (await admin.GetFromJsonAsync<ReleaseDoCatalogoV1[]>("/api/catalogos/ferramentas-uau/releases"))!.Select(r => r.Versao));
    }

    [Fact]
    public async Task A_dependencia_fora_do_catalogo_recusa_com_o_relatorio_e_o_item_invalido_e_400()
    {
        var admin = await _api.AdminAsync();
        (await admin.PostAsJsonAsync("/api/catalogos", new CatalogoNovoV1("ferramentas-uau", "Ferramentas UAU", null))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/catalogos/ferramentas-uau/itens", new ItemEditadoV1(TiposDeItem.Fluxo, Fluxo("Entrega UAU", "sumiu")))).EnsureSuccessStatusCode();

        var conferencia = (await admin.GetFromJsonAsync<RelatorioDePublicacaoV1>("/api/catalogos/ferramentas-uau/publicacao"))!;
        var recusa = await admin.PostAsJsonAsync("/api/catalogos/ferramentas-uau/publicar", new PublicarCatalogoV1());
        var invalido = await admin.PostAsJsonAsync("/api/catalogos/ferramentas-uau/itens", new ItemEditadoV1(TiposDeItem.Skill, """{ "content": "x" }"""));

        Assert.False(conferencia.PodePublicar);
        Assert.Equal(new[] { "+ fluxo:entrega-uau v1" }, conferencia.Mudancas);
        Assert.Equal(HttpStatusCode.Conflict, recusa.StatusCode);
        var relatorio = (await recusa.Content.ReadFromJsonAsync<RelatorioDePublicacaoV1>())!;
        Assert.Equal("fluxo:entrega-uau usa skill:sumiu, que não está neste catálogo nem no básico.", Assert.Single(relatorio.Problemas));
        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        using var problema = System.Text.Json.JsonDocument.Parse(await invalido.Content.ReadAsStringAsync());
        Assert.Equal("A skill está sem nome (campo \"name\").", problema.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>
    /// A release do GitHub (catalogo-v2) com um zip DE VERDADE: o manifesto traz os hashes calculados pela regra do
    /// gerador (a paridade com o git.kit é travada no Core.Tests).
    /// </summary>
    private void CatalogoDoGitHubDeVerdade()
    {
        var itens = new (string Tipo, string Nome, string Arquivo, string Texto)[]
        {
            (TiposDeItem.Skill, "revisar-alteracoes", "skills/revisar-alteracoes.json", Skill("revisar-alteracoes", "revise com o {DEVCLI}")),
            (TiposDeItem.Fluxo, "tarefa-avulsa", "fluxos/tarefa-avulsa.json", Fluxo("Tarefa avulsa", "revisar-alteracoes")),
        };
        var lidos = itens.Select(i => GeradorDoManifesto.Ler(i.Tipo, i.Nome, i.Arquivo, i.Texto)).ToArray();
        var v1 = GeradorDoManifesto.Gerar(GeradorDoManifesto.Vazio, lidos, 151).Manifesto;
        var v2 = v1 with { Versao = 2 };

        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in itens)
            {
                using var escrita = new StreamWriter(zip.CreateEntry(item.Arquivo).Open(), new UTF8Encoding(false));
                escrita.Write(item.Texto);
            }
        }

        _api.GitHub.ZipDoCatalogo = memoria.ToArray();
        _api.GitHub.Manifesto2 = ManifestoJson.Serializar(v2);
    }

    [Fact]
    public async Task O_basico_importado_do_github_e_republicado_no_site_continua_a_numeracao_e_as_rotas_antigas()
    {
        CatalogoDoGitHubDeVerdade();
        var admin = await _api.AdminAsync();
        var anonimo = _api.CreateClient();
        Assert.Equal("\"catalogo-v2\"", (await anonimo.GetAsync("/api/catalogo/base?desde=0")).Headers.ETag!.Tag); // antes: o GitHub

        var importacao = (await (await admin.PostAsync("/api/catalogos/basico/importar", null)).Content.ReadFromJsonAsync<ImportacaoDoBasicoV1>())!;
        var deNovo = (await (await admin.PostAsync("/api/catalogos/basico/importar", null)).Content.ReadFromJsonAsync<ImportacaoDoBasicoV1>())!;
        Assert.True(importacao.Importada, importacao.Mensagem);
        Assert.Equal((2, 2), (importacao.Versao, importacao.Itens));
        Assert.False(deNovo.Importada);

        // Importado: tudo publicado (nada pendente), e a rota antiga serve o MESMO pacote, agora do banco do site.
        var basico = (await admin.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!.Single(c => c.Basico);
        Assert.Equal((2, 2, 0), (basico.Versao, basico.Itens, basico.Pendentes));
        Assert.Equal(_api.GitHub.ZipDoCatalogo, await anonimo.GetByteArrayAsync("/api/catalogo/base/download"));
        Assert.Equal(HttpStatusCode.NotModified, (await anonimo.GetAsync("/api/catalogo/base?desde=2")).StatusCode);

        // Editar uma skill do básico e publicar: catalogo-v3, só ela na delta de quem tem a 2.
        var itens = (await admin.GetFromJsonAsync<ItemEditavelV1[]>("/api/catalogos/basico/itens"))!;
        var revisar = itens.Single(i => i.Nome == "revisar-alteracoes");
        (await admin.PutAsJsonAsync($"/api/catalogos/basico/itens/{revisar.Id}", new ItemEditadoV1(TiposDeItem.Skill, Skill("revisar-alteracoes", "revise com o {DEVCLI} e o {MSBUILD}")))).EnsureSuccessStatusCode();
        var publicada = await admin.PostAsJsonAsync("/api/catalogos/basico/publicar", new PublicarCatalogoV1());
        Assert.Equal(HttpStatusCode.Created, publicada.StatusCode);
        Assert.Equal("catalogo-v3", (await publicada.Content.ReadFromJsonAsync<PublicacaoDoCatalogoV1>())!.Release.Tag);

        var delta = (await anonimo.GetFromJsonAsync<CatalogoBaseV1>("/api/catalogo/base?desde=2"))!;
        var item = Assert.Single(delta.Manifesto.Itens);
        Assert.Equal(("revisar-alteracoes", 2, 3, 151), (item.Nome, item.Versao, item.Catalogo, item.MinDevKit));
        Assert.Equal(new[] { "devcli", "msbuild" }, item.Requer);
        var zip = await anonimo.GetByteArrayAsync("/api/catalogo/base/download");
        Assert.Equal(delta.Sha256, Convert.ToHexStringLower(SHA256.HashData(zip)));
        Assert.Equal(3, (await anonimo.GetFromJsonAsync<VitrineV1>("/api/catalogo/base/vitrine"))!.Versao);

        // A rota nova do básico é a mesma coisa; o histórico junta o site (3, 2) e o GitHub anterior (1).
        Assert.Equal(HttpStatusCode.NotModified, (await anonimo.GetAsync("/api/catalogos/basico?desde=3")).StatusCode);
        Assert.Equal(new[] { 3, 2, 1 }, (await admin.GetFromJsonAsync<ReleaseDoCatalogoV1[]>("/api/catalogos/basico/releases"))!.Select(r => r.Versao));
        Assert.Equal(new[] { 3, 2, 1 }, (await admin.GetFromJsonAsync<ReleaseDoCatalogoV1[]>("/api/catalogo/base/releases"))!.Select(r => r.Versao));
    }

    [Fact]
    public async Task O_item_com_nome_do_basico_e_a_empresa_que_ja_recebe_o_mesmo_item_sao_409()
    {
        CatalogoDoGitHubDeVerdade();
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        (await admin.PostAsJsonAsync("/api/catalogos", new CatalogoNovoV1("cat-a", "Catálogo A", null))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/catalogos", new CatalogoNovoV1("cat-b", "Catálogo B", null))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/catalogos/cat-a/itens", new ItemEditadoV1(TiposDeItem.Skill, Skill("compilar-uau")))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/catalogos/cat-b/itens", new ItemEditadoV1(TiposDeItem.Skill, Skill("compilar-uau")))).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/catalogos/cat-a/empresas", new VinculoV1(a.Id))).EnsureSuccessStatusCode();

        // O básico ainda está só no GitHub: a colisão confere o manifesto de lá.
        var doBasico = await admin.PostAsJsonAsync("/api/catalogos/cat-a/itens", new ItemEditadoV1(TiposDeItem.Skill, Skill("revisar-alteracoes")));
        var vinculo = await admin.PostAsJsonAsync("/api/catalogos/cat-b/empresas", new VinculoV1(a.Id));

        Assert.Equal(HttpStatusCode.Conflict, doBasico.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, vinculo.StatusCode);
        Assert.Contains("skill:compilar-uau (no catálogo 'Catálogo A')", await vinculo.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/catalogos/basico/empresas", new VinculoV1(a.Id))).StatusCode);
        var empresas = (await admin.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!.Single(c => c.Slug == "cat-a").Empresas;
        Assert.Equal("Empresa A", Assert.Single(empresas).Nome);
    }

    [Fact]
    public async Task Empresas_trazem_o_gestor_e_os_assentos_em_uso()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A", assentos: 3);
        await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        (await admin.PostAsJsonAsync($"/api/empresas/{a.Id}/gestores", new GestorNovo("gestor.a"))).EnsureSuccessStatusCode();

        var lista = (await admin.GetFromJsonAsync<EmpresaResumo[]>("/api/empresas"))!;

        Assert.Equal(("gestor.a", 1, 3), (lista[0].Gestor, lista[0].AssentosEmUso, lista[0].Assentos));
    }
}
