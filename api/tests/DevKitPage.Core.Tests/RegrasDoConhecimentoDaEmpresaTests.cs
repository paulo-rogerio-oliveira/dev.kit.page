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
}
