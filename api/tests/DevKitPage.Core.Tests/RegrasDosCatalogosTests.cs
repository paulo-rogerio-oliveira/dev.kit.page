using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras dos catálogos nomeados (US #461): o slug, o item editável (o nome que serve de pasta), a situação do
/// rascunho frente à última release, a colisão entre catálogos da mesma empresa e o fechamento das dependências.
/// </summary>
public sealed class RegrasDosCatalogosTests
{
    [Theory]
    [InlineData("ferramentas-uau", true)]
    [InlineData("a1", true)]
    [InlineData("x", false)]
    [InlineData("Com-Maiuscula", false)]
    [InlineData("-comeca-com-hifen", false)]
    [InlineData("basico", false)]
    [InlineData("meus", false)]
    public void Slug_e_minusculo_curto_e_nao_reservado(string slug, bool valido)
        => Assert.Equal(valido, RegrasDosCatalogos.SlugValido(slug));

    [Fact]
    public void O_catalogo_novo_sem_nome_ou_com_slug_torto_volta_com_o_campo()
    {
        var erros = RegrasDosCatalogos.Validar(new CatalogoNovoV1("Torto", " ", null));

        Assert.Equal(new[] { "nome", "slug" }, erros.Keys.Order());
        Assert.Empty(RegrasDosCatalogos.Validar(new CatalogoNovoV1("ferramentas-uau", "Ferramentas UAU", "Do time")));
    }

    [Fact]
    public void O_fluxo_conferido_e_pelo_slug_do_nome_com_as_dependencias_e_os_plans_embutidos()
    {
        const string envelope = """
            { "formato": "devkit-fluxo", "versao": 1, "nome": "Entrega UAU", "descricao": "",
              "desenho": { "nodes": [ { "id": "e1", "kind": "Skill", "skillName": "compilar-uau" } ] },
              "agentes": [], "planos": [ { "bloco": "p1", "plano": { "description": "Build mínimo", "content": "x" } } ] }
            """;

        var (item, erro) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(TiposDeItem.Fluxo, envelope));

        Assert.True(item is not null, erro);
        Assert.Equal(("entrega-uau", "Entrega UAU"), (item.Nome, item.Descricao));
        Assert.Equal(new[] { "plan:build-minimo", "skill:compilar-uau" }, item.Dependencias);
        Assert.Equal(new[] { "build-minimo" }, item.Embutidos);
        Assert.Equal("fluxos/entrega-uau.json", RegrasDosCatalogos.Arquivo(item.Tipo, item.Nome));
    }

    [Fact]
    public void O_nome_do_app_e_aceito_e_o_torto_e_o_conteudo_grande_sao_recusados()
    {
        static string Skill(string nome, string conteudo = "passo")
            => FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo(nome, "", conteudo, [], [], false, false, false));

        // "resolver conflitos" existe no básico real: o app aceita espaço no nome (a pasta sai do slug).
        Assert.Empty(RegrasDosCatalogos.Conferir(new ItemEditadoV1(TiposDeItem.Skill, Skill("resolver conflitos"))).Erro);
        Assert.StartsWith("Nome inválido: 'x'", RegrasDosCatalogos.Conferir(new ItemEditadoV1(TiposDeItem.Skill, Skill("x"))).Erro);
        Assert.StartsWith("Nome inválido", RegrasDosCatalogos.Conferir(new ItemEditadoV1(TiposDeItem.Skill, Skill(new string('a', 65)))).Erro);
        Assert.Equal("O conteúdo passa de 256 KB.", RegrasDosCatalogos.Conferir(new ItemEditadoV1(TiposDeItem.Skill, Skill("grande", new string('x', 300 * 1024)))).Erro);
        Assert.StartsWith("Tipo inválido", RegrasDosCatalogos.Conferir(new ItemEditadoV1("agente", Skill("a-skill"))).Erro);
    }

    private static ItemDoCatalogoV1 Publicado(string hash, string tecnologia = "Git")
        => new(TiposDeItem.Skill, "revisar-alteracoes", 3, hash, 2, "skills/revisar-alteracoes.json", "Revisa", tecnologia, 1, [], [], [hash]);

    [Fact]
    public void A_situacao_compara_com_a_ultima_release()
    {
        string Situacao(string hash, bool retirado, ItemDoCatalogoV1? publicado, string categoria = "")
            => RegrasDosCatalogos.Situacao(TiposDeItem.Skill, "revisar-alteracoes", hash, "Revisa", categoria, "skills/revisar-alteracoes.json", retirado, publicado).Situacao;

        Assert.Equal(SituacoesDoItem.Novo, Situacao("h1", false, null));
        Assert.Equal(SituacoesDoItem.Publicado, Situacao("h1", false, Publicado("h1")));
        Assert.Equal(SituacoesDoItem.Alterado, Situacao("h2", false, Publicado("h1")));
        Assert.Equal(SituacoesDoItem.Alterado, Situacao("h1", false, Publicado("h1"), categoria: "Qualidade"));
        Assert.Equal(SituacoesDoItem.RetiradaPendente, Situacao("h1", true, Publicado("h1")));
        Assert.Equal(SituacoesDoItem.Retirado, Situacao("h1", true, null));
        Assert.Equal(3, RegrasDosCatalogos.Situacao(TiposDeItem.Skill, "revisar-alteracoes", "h1", "Revisa", "", "skills/revisar-alteracoes.json", false, Publicado("h1")).VersaoPublicada);
    }

    [Fact]
    public void As_dependencias_resolvem_no_proprio_catalogo_no_plan_embutido_ou_no_basico()
    {
        var itens = new[]
        {
            new ItemParaPublicar(TiposDeItem.Fluxo, "entrega-uau", ["skill:compilar-uau", "plan:build-minimo", "skill:revisar-alteracoes", "skill:de-outro", "skill:sumiu"], ["build-minimo"]),
            new ItemParaPublicar(TiposDeItem.Skill, "compilar-uau", [], []),
        };

        var problemas = RegrasDosCatalogos.Dependencias(
            itens, new HashSet<string> { "skill:revisar-alteracoes" }, new Dictionary<string, string> { ["skill:de-outro"] = "Outro catálogo" });

        Assert.Equal(2, problemas.Count);
        Assert.Contains("fluxo:entrega-uau usa skill:de-outro, que está no catálogo 'Outro catálogo': copie o item para este catálogo (a máquina pode não ter o outro).", problemas);
        Assert.Contains("fluxo:entrega-uau usa skill:sumiu, que não está neste catálogo nem no básico.", problemas);
    }

    [Fact]
    public void A_colisao_lista_o_item_e_o_catalogo_onde_ele_ja_esta()
    {
        var colisoes = RegrasDosCatalogos.Colisoes(
            new HashSet<string> { "skill:compilar-uau", "plan:geral-uau" },
            [("Ferramentas", new HashSet<string> { "skill:compilar-uau" }), ("Vazio", new HashSet<string>())]);

        Assert.Equal("skill:compilar-uau (no catálogo 'Ferramentas')", Assert.Single(colisoes));
    }
}
