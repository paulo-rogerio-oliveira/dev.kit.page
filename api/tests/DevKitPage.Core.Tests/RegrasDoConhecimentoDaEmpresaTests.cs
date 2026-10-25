using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras do contexto por nível (US #550) sobre as convenções da #491: o que é chave de contexto, o teto do
/// valor (4000 no contexto, 200 no resto) e QUEM grava cada nível — por papel, por nível e pelo prefixo da chave.
/// </summary>
public sealed class RegrasDoConhecimentoDaEmpresaTests
{
    private static readonly EscopoDaEmpresa Dev = new(1, 10, EhGestor: false);
    private static readonly EscopoDaEmpresa Gestor = new(1, 20, EhGestor: true);
    private static readonly EscopoDaEmpresa Admin = EscopoDaEmpresa.DoAdmin(1, 99);

    [Theory]
    [InlineData("contexto.stack", true)]
    [InlineData(" Contexto.Revisao ", true)]
    [InlineData("branch.feature", false)]
    [InlineData("contexto", false)]
    [InlineData(null, false)]
    public void Contexto_e_a_familia_de_chaves_com_o_prefixo(string? chave, bool contexto)
        => Assert.Equal(contexto, RegrasDoConhecimentoDaEmpresa.EhContexto(chave));

    [Fact]
    public void O_contexto_aceita_texto_longo_de_varias_linhas_ate_4000_e_o_branch_continua_com_200()
    {
        var longo = string.Join('\n', Enumerable.Repeat("Use .NET 10 e testes xUnit.", 200))[..4000];

        Assert.Empty(RegrasDoConhecimentoDaEmpresa.ValidarConvencao(new ConvencaoV1("empresa", "contexto.stack", longo)));
        Assert.Contains("4000", RegrasDoConhecimentoDaEmpresa.ValidarConvencao(new ConvencaoV1("empresa", "contexto.stack", longo + "x")));
        Assert.Contains("200", RegrasDoConhecimentoDaEmpresa.ValidarConvencao(new ConvencaoV1("empresa", "branch.feature", new string('a', 201))));
        Assert.Equal(4000, RegrasDoConhecimentoDaEmpresa.TetoDoValor("contexto.x"));
        Assert.Equal(200, RegrasDoConhecimentoDaEmpresa.TetoDoValor("branch.bug"));
    }

    [Fact]
    public void O_admin_e_gestor_na_empresa_do_caminho()
    {
        Assert.Equal(new EscopoDaEmpresa(7, 99, true, true), EscopoDaEmpresa.DoAdmin(7, 99));
        Assert.Null(EscopoDaEmpresa.DasClaims(Papeis.Admin, "7", "99")); // nunca vem das claims
    }

    /// <summary>A matriz papel × nível × prefixo da chave.</summary>
    [Theory]
    // empresa: só gestor e admin, qualquer chave
    [InlineData("dev", "empresa", "contexto.stack", 0, false)]
    [InlineData("dev", "empresa", "branch.feature", 0, false)]
    [InlineData("gestor", "empresa", "contexto.stack", 0, true)]
    [InlineData("admin", "empresa", "contexto.stack", 0, true)]
    // equipe: o contexto é do gestor; o branch (#491), de qualquer um
    [InlineData("dev", "time", "contexto.stack", 0, false)]
    [InlineData("dev", "time", "branch.feature", 0, true)]
    [InlineData("gestor", "time", "contexto.stack", 0, true)]
    [InlineData("admin", "time", "contexto.stack", 0, true)]
    // usuário: o dono; o gestor não edita o de outro; o admin, sim
    [InlineData("dev", "usuario", "contexto.stack", 0, true)]
    [InlineData("dev", "usuario", "contexto.stack", 10, true)]
    [InlineData("dev", "usuario", "contexto.stack", 11, false)]
    [InlineData("gestor", "usuario", "contexto.stack", 10, false)]
    [InlineData("admin", "usuario", "contexto.stack", 10, true)]
    [InlineData("gestor", "projeto", "contexto.stack", 0, false)]
    public void Quem_grava_cada_nivel(string papel, string nivel, string chave, int alvo, bool pode)
    {
        var escopo = papel switch { "dev" => Dev, "gestor" => Gestor, _ => Admin };
        Assert.Equal(pode, RegrasDoConhecimentoDaEmpresa.PodeGravar(escopo, nivel, chave, alvo));
    }

    /// <summary>US #663 — a matriz papel × camada da SUGESTÃO: o agente sugere em toda camada que o usuário vê.</summary>
    [Theory]
    [InlineData("dev", "usuario", 0, true)]
    [InlineData("dev", "usuario", 10, true)]
    [InlineData("dev", "usuario", 11, false)]
    [InlineData("dev", "time", 0, true)]
    [InlineData("dev", "empresa", 0, true)]
    [InlineData("gestor", "empresa", 0, true)]
    [InlineData("gestor", "usuario", 10, false)]
    [InlineData("admin", "usuario", 10, true)]
    [InlineData("dev", "projeto", 0, false)]
    public void Quem_sugere_em_cada_camada(string papel, string nivel, int alvo, bool pode)
    {
        var escopo = papel switch { "dev" => Dev, "gestor" => Gestor, _ => Admin };
        Assert.Equal(pode, RegrasDoConhecimentoDaEmpresa.PodeSugerir(escopo, nivel, alvo));
    }

    /// <summary>US #663 — PROMOVER é de quem grava a regra da camada: o dev na dele, o gestor na equipe e na empresa, o admin em todas.</summary>
    [Theory]
    [InlineData("dev", "usuario", 10, true)]
    [InlineData("dev", "time", 0, false)]
    [InlineData("dev", "empresa", 0, false)]
    [InlineData("gestor", "time", 0, true)]
    [InlineData("gestor", "empresa", 0, true)]
    [InlineData("gestor", "usuario", 10, false)]
    [InlineData("admin", "empresa", 0, true)]
    [InlineData("admin", "time", 0, true)]
    [InlineData("admin", "usuario", 10, true)]
    public void Quem_promove_a_sugestao_a_regra(string papel, string nivel, int alvo, bool pode)
    {
        var escopo = papel switch { "dev" => Dev, "gestor" => Gestor, _ => Admin };
        Assert.Equal(pode, RegrasDoConhecimentoDaEmpresa.PodePromover(escopo, nivel, "contexto.stack", alvo));
    }

    /// <summary>US #663 — DESCARTAR: quem grava a camada, ou o autor da sugestão (o dev descarta a que o agente dele fez na empresa).</summary>
    [Theory]
    [InlineData("dev", "empresa", 0, 10, true)]   // a sugestão dele
    [InlineData("dev", "empresa", 0, 30, false)]  // a de outro, numa camada que ele não grava
    [InlineData("dev", "time", 0, 30, false)]
    [InlineData("dev", "usuario", 10, 30, true)]  // a camada dele
    [InlineData("gestor", "time", 0, 30, true)]
    [InlineData("gestor", "empresa", 0, 30, true)]
    [InlineData("admin", "usuario", 10, 30, true)]
    public void Quem_descarta_a_sugestao(string papel, string nivel, int alvo, int autor, bool pode)
    {
        var escopo = papel switch { "dev" => Dev, "gestor" => Gestor, _ => Admin };
        Assert.Equal(pode, RegrasDoConhecimentoDaEmpresa.PodeDescartar(escopo, nivel, "contexto.stack", alvo, autor));
    }

    [Theory]
    [InlineData("stack", "contexto.stack")]
    [InlineData(" Contexto.Revisao ", "contexto.revisao")]
    [InlineData("", "")]
    public void O_tema_vira_a_chave_de_contexto(string tema, string chave)
        => Assert.Equal(chave, RegrasDoConhecimentoDaEmpresa.ChaveDoTema(tema));

    [Fact]
    public void A_sugestao_e_so_de_contexto_com_texto_e_no_teto()
    {
        Assert.Empty(RegrasDoConhecimentoDaEmpresa.ValidarSugestao(new SugestaoDeContextoV1("usuario", "stack", "Use .NET 10.")));
        Assert.Empty(RegrasDoConhecimentoDaEmpresa.ValidarSugestao(new SugestaoDeContextoV1("time", "contexto.testes", "xUnit", "Vendas")));
        // O branch.* continua só regra do usuário: o agente não o sugere.
        Assert.Contains("só sugere CONTEXTO", RegrasDoConhecimentoDaEmpresa.ValidarSugestao(new SugestaoDeContextoV1("empresa", "branch.bug", "bug/{id}")));
        Assert.Contains("Escreva", RegrasDoConhecimentoDaEmpresa.ValidarSugestao(new SugestaoDeContextoV1("usuario", "stack", "  ")));
        Assert.Contains("4000", RegrasDoConhecimentoDaEmpresa.ValidarSugestao(new SugestaoDeContextoV1("usuario", "stack", new string('x', 4001))));
        Assert.Contains("time", RegrasDoConhecimentoDaEmpresa.ValidarSugestao(new SugestaoDeContextoV1("time", "stack", "x")));
        Assert.Contains("nível", RegrasDoConhecimentoDaEmpresa.ValidarSugestao(new SugestaoDeContextoV1("projeto", "stack", "x")));
        Assert.NotEmpty(RegrasDoConhecimentoDaEmpresa.ValidarSugestao(null));
    }
}
