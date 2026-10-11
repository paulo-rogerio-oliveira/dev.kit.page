using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras do catálogo (US #425): a tag <c>catalogo-vN</c> separada das do app, a delta e o 304, a vitrine por
/// tecnologia, o manifesto torto recusado; e as do catálogo da empresa — nome, tamanho, colisão com o básico, hash
/// e o escopo das claims.
/// </summary>
public sealed class CatalogoTests
{
    private static ItemDoCatalogoV1 Item(string nome, int catalogo, string tecnologia = "Git", string tipo = TiposDeItem.Skill)
        => new(tipo, nome, 1, "h-" + nome, catalogo, $"skills/{nome}.json", $"Sobre {nome}", tecnologia, 151, [], [], ["h-" + nome]);

    private static readonly ManifestoDoCatalogoV1 Manifesto = new(
        3, 151,
        [Item("revisar-alteracoes", 1), Item("compilar-angular", 3, "Angular"), Item("geral", 2, "", TiposDeItem.Plan), Item("abrir-pr", 3)],
        [new ItemRetiradoV1(TiposDeItem.Skill, "antiga", 2, 2), new ItemRetiradoV1(TiposDeItem.Skill, "velha", 1, 3)]);

    [Theory]
    [InlineData("catalogo-v3", 3)]
    [InlineData("Catalogo-V12", 12)]
    [InlineData("v136", null)]
    [InlineData("catalogo-v0", null)]
    [InlineData("catalogo-v3-beta", null)]
    [InlineData(null, null)]
    public void Versao_do_catalogo_so_le_a_tag_catalogo(string? tag, int? esperada)
        => Assert.Equal(esperada, RegrasDoCatalogoBase.Versao(tag));

    [Fact]
    public void Tag_do_catalogo_nunca_e_versao_do_app()
    {
        Assert.Null(RegrasDeVersao.Versao("catalogo-v3"));
        Assert.Null(RegrasDeVersao.Versao("catalogo-v136"));
        Assert.Equal(136, RegrasDeVersao.Versao("v136"));
    }

    [Fact]
    public void Delta_traz_so_o_que_mudou_depois_da_versao_informada()
    {
        var delta = RegrasDoCatalogoBase.Delta(Manifesto, 2)!;

        Assert.Equal(3, delta.Versao);
        Assert.Equal(new[] { "compilar-angular", "abrir-pr" }, delta.Itens.Select(i => i.Nome));
        Assert.Equal("velha", Assert.Single(delta.Retirados).Nome);
        Assert.Equal(4, RegrasDoCatalogoBase.Delta(Manifesto, 0)!.Itens.Count); // quem não tem nada recebe tudo
    }

    [Theory]
    [InlineData(3)]
    [InlineData(9)]
    public void Delta_e_nula_sem_mudanca_ou_para_quem_esta_a_frente(int desde)
        => Assert.Null(RegrasDoCatalogoBase.Delta(Manifesto, desde));

    [Fact]
    public void Vitrine_agrupa_por_tecnologia_e_o_sem_tecnologia_vai_para_geral()
    {
        var vitrine = RegrasDoCatalogoBase.Vitrine(Manifesto, new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(new[] { "Angular", "Geral", "Git" }, vitrine.Grupos.Select(g => g.Tecnologia));
        Assert.Equal(new[] { "abrir-pr", "revisar-alteracoes" }, vitrine.Grupos.Single(g => g.Tecnologia == "Git").Itens.Select(i => i.Nome));
    }

    [Fact]
    public void Manifesto_torto_ou_de_outra_versao_e_invalido()
    {
        Assert.True(RegrasDoCatalogoBase.Valido(Manifesto, 3));
        Assert.False(RegrasDoCatalogoBase.Valido(Manifesto, 2));
        Assert.False(RegrasDoCatalogoBase.Valido(Manifesto with { Itens = [Item("x1", 1) with { Tipo = "macro" }] }, 3));
        Assert.False(RegrasDoCatalogoBase.Valido(Manifesto with { Itens = [Item("x1", 1) with { Hash = "" }] }, 3));
        Assert.False(RegrasDoCatalogoBase.Valido(null, 3));
    }

    private static readonly IReadOnlySet<string> Basico = new HashSet<string> { "skill:revisar-alteracoes", "plan:geral" };

    private static PublicacaoV1 Publicacao(string nome = "compilar-uau", string tipo = TiposDeItem.Skill, string conteudo = "{\"name\":\"x\"}", string[]? deps = null)
        => new(tipo, nome, "Compila o UAU", conteudo, deps);

    [Fact]
    public void Publicacao_valida_passa()
        => Assert.Empty(RegrasDoCatalogoDaEmpresa.Validar(Publicacao(deps: ["skill:compilar-uau", "plan:geral"]), Basico));

    [Theory]
    [InlineData("Compilar")]
    [InlineData("a")]
    [InlineData("-comeca-com-hifen")]
    [InlineData("com espaco")]
    [InlineData("acentuação")]
    public void Nome_invalido_e_recusado(string nome)
        => Assert.StartsWith("Nome inválido", RegrasDoCatalogoDaEmpresa.Validar(Publicacao(nome), Basico));

    [Fact]
    public void Colisao_com_o_basico_e_recusada_so_no_mesmo_tipo()
    {
        Assert.StartsWith("Já existe", RegrasDoCatalogoDaEmpresa.Validar(Publicacao("revisar-alteracoes"), Basico));
        Assert.Empty(RegrasDoCatalogoDaEmpresa.Validar(Publicacao("revisar-alteracoes", TiposDeItem.Plan), Basico));
    }

    [Fact]
    public void Conteudo_acima_de_256_kb_vazio_tipo_errado_e_dependencia_torta_sao_recusados()
    {
        var grande = new string('x', RegrasDoCatalogoDaEmpresa.TamanhoMaximoDoConteudo + 1);

        Assert.Contains("256 KB", RegrasDoCatalogoDaEmpresa.Validar(Publicacao(conteudo: grande), Basico));
        Assert.Empty(RegrasDoCatalogoDaEmpresa.Validar(Publicacao(conteudo: new string('x', RegrasDoCatalogoDaEmpresa.TamanhoMaximoDoConteudo)), Basico));
        Assert.Contains("obrigatório", RegrasDoCatalogoDaEmpresa.Validar(Publicacao(conteudo: "  "), Basico));
        Assert.StartsWith("Tipo inválido", RegrasDoCatalogoDaEmpresa.Validar(Publicacao(tipo: "macro"), Basico));
        Assert.StartsWith("Dependências", RegrasDoCatalogoDaEmpresa.Validar(Publicacao(deps: ["skill"]), Basico));
        Assert.NotEmpty(RegrasDoCatalogoDaEmpresa.Validar(null, Basico));
    }

    [Fact]
    public void Hash_ignora_a_quebra_de_linha_e_muda_com_o_conteudo()
    {
        Assert.Equal(RegrasDoCatalogoDaEmpresa.Hash("a\r\nb"), RegrasDoCatalogoDaEmpresa.Hash("a\nb"));
        Assert.NotEqual(RegrasDoCatalogoDaEmpresa.Hash("a\nb"), RegrasDoCatalogoDaEmpresa.Hash("a\nc"));
        Assert.Equal(64, RegrasDoCatalogoDaEmpresa.Hash("x").Length);
    }

    [Fact]
    public void Escopo_da_empresa_sai_das_claims_do_gestor_e_do_dev_com_empresa()
    {
        Assert.Equal(new EscopoDaEmpresa(4, 7, true), EscopoDaEmpresa.DasClaims(Papeis.Gestor, "4", "7"));
        Assert.Equal(new EscopoDaEmpresa(4, 8, false), EscopoDaEmpresa.DasClaims(Papeis.Dev, "4", "8"));
        Assert.Null(EscopoDaEmpresa.DasClaims(Papeis.Admin, null, "1")); // o admin não tem empresa
        Assert.Null(EscopoDaEmpresa.DasClaims(Papeis.Dev, null, "8"));
        Assert.Null(EscopoDaEmpresa.DasClaims(Papeis.Dev, "-4", "8"));
        Assert.Null(EscopoDaEmpresa.DasClaims(Papeis.Gestor, "4", "x"));
    }
}
