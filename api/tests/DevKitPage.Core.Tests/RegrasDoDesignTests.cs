using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras do design compartilhado (US #669): a URL normalizada (a mesma do dev.kit), o nível de quem não tem
/// empresa, a visibilidade por escopo, quem apaga, o teto do plano e a validação do envio.
/// </summary>
public sealed class RegrasDoDesignTests
{
    private static readonly EscopoDoDesign Dev = new(10, 1, EhGestor: false);
    private static readonly EscopoDoDesign Gestor = new(20, 1, EhGestor: true);
    private static readonly EscopoDoDesign Solo = new(30, null, EhGestor: false);

    [Theory]
    [InlineData("https://github.com/Acme/Portal.git", "github.com/acme/portal")]
    [InlineData("https://github.com/acme/portal/", "github.com/acme/portal")]
    [InlineData("  http://github.com/acme/portal  ", "github.com/acme/portal")]
    [InlineData("git@github.com:acme/portal.git", "github.com/acme/portal")]
    [InlineData("ssh://git@github.com/acme/portal", "github.com/acme/portal")]
    [InlineData("https://paulo@dev.azure.com/org/proj/_git/Repo", "dev.azure.com/org/proj/_git/repo")]
    [InlineData(@"D:\ProjetosGIT\t_645\git.kit", "d:/projetosgit/t_645/git.kit")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void A_url_do_repositorio_e_normalizada_em_https_ssh_git_e_maiusculas(string? url, string chave)
        => Assert.Equal(chave, DesignV1.ChaveDoRepositorio(url));

    [Fact]
    public void Sem_empresa_o_nivel_e_sempre_usuario_e_com_empresa_o_padrao_e_empresa()
    {
        Assert.Equal("usuario", RegrasDoDesign.NivelEfetivo(Solo, "empresa"));
        Assert.Equal("empresa", RegrasDoDesign.NivelEfetivo(Dev, null));
        Assert.Equal("time", RegrasDoDesign.NivelEfetivo(Dev, " Time "));
        Assert.Equal("usuario:30", RegrasDoDesign.EspacoDe(Solo));
        Assert.Equal("empresa:1", RegrasDoDesign.EspacoDe(Dev));
    }

    [Fact]
    public void O_escopo_das_claims_existe_sem_empresa_e_o_gestor_so_com_ela()
    {
        Assert.Equal(new EscopoDoDesign(5, null, false), EscopoDoDesign.DasClaims(Papeis.Dev, null, "5"));
        Assert.Equal(new EscopoDoDesign(5, 2, true), EscopoDoDesign.DasClaims(Papeis.Gestor, "2", "5"));
        Assert.Equal(new EscopoDoDesign(5, null, false), EscopoDoDesign.DasClaims(Papeis.Gestor, null, "5"));
        Assert.Null(EscopoDoDesign.DasClaims(Papeis.Dev, "2", null));
    }

    [Fact]
    public void A_visibilidade_e_a_do_contexto()
    {
        // O nível usuário: só o dono.
        Assert.True(RegrasDoDesign.Visivel(Dev, "usuario", 1, "", 10, null));
        Assert.False(RegrasDoDesign.Visivel(Gestor, "usuario", 1, "", 10, null));
        // A empresa: todos dela, ninguém de fora.
        Assert.True(RegrasDoDesign.Visivel(Gestor, "empresa", 1, "", 0, null));
        Assert.False(RegrasDoDesign.Visivel(new EscopoDoDesign(40, 2, false), "empresa", 1, "", 0, null));
        Assert.False(RegrasDoDesign.Visivel(Solo, "empresa", 1, "", 0, null));
        // O time: o declarado, ou qualquer um sem declarar (o site).
        Assert.True(RegrasDoDesign.Visivel(Dev, "time", 1, "vendas", 0, "Vendas"));
        Assert.False(RegrasDoDesign.Visivel(Dev, "time", 1, "vendas", 0, "suporte"));
        Assert.True(RegrasDoDesign.Visivel(Dev, "time", 1, "vendas", 0, null));
    }

    [Fact]
    public void Apaga_o_autor_ou_o_gestor_da_empresa_do_projeto()
    {
        Assert.True(RegrasDoDesign.PodeApagar(Dev, autor: 10, empresaDoProjeto: 1));
        Assert.False(RegrasDoDesign.PodeApagar(Dev, autor: 11, empresaDoProjeto: 1));
        Assert.True(RegrasDoDesign.PodeApagar(Gestor, autor: 11, empresaDoProjeto: 1));
        Assert.False(RegrasDoDesign.PodeApagar(Gestor, autor: 11, empresaDoProjeto: 2));
        Assert.True(RegrasDoDesign.PodeApagar(Solo, autor: 30, empresaDoProjeto: null));
    }

    [Fact]
    public void O_teto_e_o_do_plano_e_o_padrao_fora_da_lista()
    {
        var opcoes = new OpcoesDoDesign();
        Assert.Equal(500, RegrasDoDesign.TetoDo(opcoes, "empresarial").Versoes);
        Assert.Equal(100, RegrasDoDesign.TetoDo(opcoes, "Outro").Versoes);
        Assert.Equal(100, RegrasDoDesign.TetoDo(opcoes, null).Versoes);

        var teto = new TetoDoDesign { Versoes = 2, MegaBytes = 1 };
        Assert.Empty(RegrasDoDesign.ConferirTeto(teto, 1, 0, 100, "X"));
        Assert.Contains("2 versões", RegrasDoDesign.ConferirTeto(teto, 2, 0, 100, "X"));
        Assert.Contains("1 MB", RegrasDoDesign.ConferirTeto(teto, 0, 1024 * 1024, 1, "X"));
    }

    [Fact]
    public void A_validacao_confere_o_png_a_tela_o_tipo_a_base_e_os_tamanhos()
    {
        var png = Png(600, 400);
        var ok = new EnvioDeVersaoDeDesign("https://github.com/acme/portal", null, null, "checkout", "Checkout", "", 2, "proposta", 1,
            "light", "proposta", "a.html", "semana5", "abc", "", "agente", "claude", null);

        Assert.Empty(RegrasDoDesign.Validar(ok, png));
        Assert.Equal((600, 400), RegrasDoDesign.DimensoesDoPng(png));
        Assert.Contains("PNG", RegrasDoDesign.Validar(ok, [1, 2, 3, 4]));
        Assert.Contains("identificador", RegrasDoDesign.Validar(ok with { Tela = "../x" }, png));
        Assert.Contains("tipo", RegrasDoDesign.Validar(ok with { Tipo = "rascunho" }, png));
        Assert.Contains("base", RegrasDoDesign.Validar(ok with { Base = 2 }, png));
        Assert.Contains("nível", RegrasDoDesign.Validar(ok with { Nivel = "projeto" }, png));
        Assert.Contains("repositório", RegrasDoDesign.Validar(ok with { Repositorio = " " }, png));
        Assert.Contains("tamanho", RegrasDoDesign.Validar(ok with { Descricao = new string('a', DesignV1.TamanhoMaximoDaDescricao + 1) }, png));
        Assert.Equal("/api/design/7/checkout/v2", DesignV1.Link(7, "checkout", 2));
        Assert.Equal("/design/7/checkout/v2", DesignV1.Pagina(7, "checkout", 2));
    }

    private static byte[] Png(int largura, int altura)
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        png.AddRange(BitConverter.GetBytes(largura).Reverse());
        png.AddRange(BitConverter.GetBytes(altura).Reverse());
        png.AddRange([8, 6, 0, 0, 0, 0, 0, 0, 0]);
        return png.ToArray();
    }
}
