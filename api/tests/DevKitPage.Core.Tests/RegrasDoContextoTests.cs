using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras do contexto (US #556), sem banco: a matriz papel × nível × origem do que se VÊ e do que se GRAVA, a
/// participação aprovada como única porta da equipe, a validação e a barreira de credenciais (com os MESMOS exemplos que o
/// dev.kit confere na lista espelhada dele).
/// </summary>
public sealed class RegrasDoContextoTests
{
    private static readonly EscopoDoContexto Ana = new(1, 10, PapelNoContexto.Dev);
    private static readonly EscopoDoContexto Gestor = new(1, 20, PapelNoContexto.Gestor);
    private static readonly EscopoDoContexto Admin = EscopoDoContexto.DoAdmin(1, 99);
    private static readonly IReadOnlySet<string> DeVendas = new HashSet<string> { "vendas" };
    private static readonly IReadOnlySet<string> Nenhuma = new HashSet<string>();

    [Fact]
    public void O_dev_ve_o_proprio_o_das_equipes_aprovadas_e_o_da_empresa()
    {
        Assert.True(RegrasDoContexto.PodeVer(Ana, NiveisDeContexto.Usuario, "", 10, DeVendas));
        Assert.True(RegrasDoContexto.PodeVer(Ana, NiveisDeContexto.Equipe, "Vendas", 0, DeVendas));
        Assert.True(RegrasDoContexto.PodeVer(Ana, NiveisDeContexto.Empresa, "", 0, Nenhuma));

        Assert.False(RegrasDoContexto.PodeVer(Ana, NiveisDeContexto.Usuario, "", 11, DeVendas)); // o do colega
        Assert.False(RegrasDoContexto.PodeVer(Ana, NiveisDeContexto.Equipe, "financeiro", 0, DeVendas)); // equipe alheia
        Assert.False(RegrasDoContexto.PodeVer(Ana, "projeto", "", 0, DeVendas));
    }

    [Fact]
    public void O_dev_grava_o_proprio_e_o_das_equipes_dele_mas_nao_o_da_empresa_nem_o_de_outra_equipe()
    {
        Assert.Empty(RegrasDoContexto.PodeGravar(Ana, NiveisDeContexto.Usuario, "", 10, Nenhuma));
        Assert.Empty(RegrasDoContexto.PodeGravar(Ana, NiveisDeContexto.Equipe, "vendas", 0, DeVendas));

        Assert.Contains("gestor", RegrasDoContexto.PodeGravar(Ana, NiveisDeContexto.Empresa, "", 0, DeVendas));
        Assert.Contains("financeiro", RegrasDoContexto.PodeGravar(Ana, NiveisDeContexto.Equipe, "Financeiro", 0, DeVendas));
        Assert.NotEmpty(RegrasDoContexto.PodeGravar(Ana, NiveisDeContexto.Usuario, "", 11, DeVendas));
    }

    /// <summary>O pedido pendente não está entre as aprovadas: sem acesso nenhum, nem leitura.</summary>
    [Fact]
    public void Equipe_so_com_a_participacao_aprovada()
    {
        Assert.False(RegrasDoContexto.PodeVer(Ana, NiveisDeContexto.Equipe, "vendas", 0, Nenhuma));
        Assert.NotEmpty(RegrasDoContexto.PodeGravar(Ana, NiveisDeContexto.Equipe, "vendas", 0, Nenhuma));
    }

    [Fact]
    public void O_gestor_ve_e_grava_a_empresa_e_todas_as_equipes_mas_nao_o_contexto_pessoal_do_dev()
    {
        Assert.Empty(RegrasDoContexto.PodeGravar(Gestor, NiveisDeContexto.Empresa, "", 0, Nenhuma));
        Assert.Empty(RegrasDoContexto.PodeGravar(Gestor, NiveisDeContexto.Equipe, "qualquer", 0, Nenhuma));
        Assert.True(RegrasDoContexto.PodeVer(Gestor, NiveisDeContexto.Equipe, "qualquer", 0, Nenhuma));

        Assert.False(RegrasDoContexto.PodeVer(Gestor, NiveisDeContexto.Usuario, "", 10, Nenhuma));
        Assert.NotEmpty(RegrasDoContexto.PodeGravar(Gestor, NiveisDeContexto.Usuario, "", 10, Nenhuma));
    }

    [Theory]
    [InlineData(NiveisDeContexto.Usuario, "", 10)]
    [InlineData(NiveisDeContexto.Equipe, "financeiro", 0)]
    [InlineData(NiveisDeContexto.Empresa, "", 0)]
    public void O_admin_do_board_ve_e_grava_tudo(string nivel, string equipe, int dono)
    {
        Assert.True(RegrasDoContexto.PodeVer(Admin, nivel, equipe, dono, Nenhuma));
        Assert.Empty(RegrasDoContexto.PodeGravar(Admin, nivel, equipe, dono, Nenhuma));
    }

    [Fact]
    public void Escopos_do_token_e_do_admin()
    {
        Assert.Equal(PapelNoContexto.Gestor, EscopoDoContexto.DoToken(new EscopoDaEmpresa(3, 7, EhGestor: true)).Papel);
        Assert.Equal(new EscopoDoContexto(3, 7, PapelNoContexto.Dev), EscopoDoContexto.DoToken(new EscopoDaEmpresa(3, 7, EhGestor: false)));
        Assert.True(EscopoDoContexto.DoAdmin(5, 1).AdministraAEmpresa);
    }

    [Fact]
    public void Validacao_do_nivel_da_chave_da_origem_e_dos_tamanhos()
    {
        Assert.Empty(RegrasDoContexto.Validar(new GravacaoDeContextoV1("usuario", "contexto.testes", "xUnit e Vitest")));
        Assert.Empty(RegrasDoContexto.Validar(new GravacaoDeContextoV1("equipe", "branch.hotfix", "hotfix/{id}", "Vendas", "agente", 3)));

        Assert.NotEmpty(RegrasDoContexto.Validar(null));
        Assert.Contains("nível", RegrasDoContexto.Validar(new GravacaoDeContextoV1("time", "x", "y")));
        Assert.Contains("chave", RegrasDoContexto.Validar(new GravacaoDeContextoV1("usuario", "Contexto Testes", "y")));
        Assert.Contains("origem", RegrasDoContexto.Validar(new GravacaoDeContextoV1("usuario", "x", "y", Origem: "robo")));
        Assert.Contains("texto", RegrasDoContexto.Validar(new GravacaoDeContextoV1("usuario", "x", "  ")));
        Assert.Contains("equipe", RegrasDoContexto.Validar(new GravacaoDeContextoV1("equipe", "x", "y")));
        Assert.Contains("versão", RegrasDoContexto.Validar(new GravacaoDeContextoV1("usuario", "x", "y", VersaoEsperada: 0)));
    }

    [Fact]
    public void O_valor_vai_ate_4000_e_a_nota_do_agente_ate_1000()
    {
        var instrucao = new string('a', LimitesDoContexto.TamanhoMaximoDoValor);
        Assert.Empty(RegrasDoContexto.Validar(new GravacaoDeContextoV1("usuario", "contexto.longo", instrucao)));
        Assert.Contains("4000", RegrasDoContexto.Validar(new GravacaoDeContextoV1("usuario", "contexto.longo", instrucao + "a")));

        var nota = new string('a', RegrasDoContexto.TamanhoMaximoDaNota + 1);
        Assert.Contains("1000", RegrasDoContexto.Validar(new GravacaoDeContextoV1("usuario", "nota.longa", nota, Origem: "agente")));
    }

    [Fact]
    public void Origem_vazia_e_a_do_usuario_e_a_equipe_se_normaliza()
    {
        Assert.Equal(OrigensDeContexto.Usuario, RegrasDoContexto.NormalizarOrigem(null));
        Assert.Equal(OrigensDeContexto.Agente, RegrasDoContexto.NormalizarOrigem(" Agente "));
        Assert.Equal("vendas", RegrasDoContexto.NormalizarEquipe("  Vendas "));
    }

    public static TheoryData<string> Segredos => new(SegredosNoContexto.ExemplosQueSaoSegredo);

    public static TheoryData<string> TextosComuns => new(SegredosNoContexto.ExemplosQueNaoSaoSegredo);

    [Theory]
    [MemberData(nameof(Segredos))]
    public void Reconhece_cada_padrao_de_credencial(string texto) => Assert.True(RegrasDoContexto.ContemSegredo(texto), texto);

    [Theory]
    [MemberData(nameof(TextosComuns))]
    public void Nao_recusa_texto_comum(string texto) => Assert.False(RegrasDoContexto.ContemSegredo(texto), texto);

    [Fact]
    public void A_mensagem_da_recusa_nao_ecoa_o_valor()
        => Assert.DoesNotContain("ghp_", RegrasDoContexto.MensagemDeSegredo, StringComparison.Ordinal);

    [Fact]
    public void O_adaptador_das_convencoes_troca_time_por_equipe_e_so_leva_as_instrucoes()
    {
        var gravacao = AdaptadorDeConvencoes.ParaGravacao(new ConvencaoV1("time", "branch.bug", "bug/{id}", "Vendas"));
        Assert.Equal(("equipe", "Vendas", OrigensDeContexto.Usuario), (gravacao.Nivel, gravacao.Equipe, gravacao.Origem));

        var agora = DateTimeOffset.UnixEpoch;
        var itens = new[]
        {
            new ContextoV1(1, "equipe", "branch.bug", "bug/{id}", "vendas", "", "usuario", "Ana", agora, 1),
            new ContextoV1(2, "equipe", "nota.build", "rodar o build", "vendas", "", "agente", "Ana", agora, 1),
        };
        var convencao = Assert.Single(AdaptadorDeConvencoes.Convencoes(itens));
        Assert.Equal(("time", "vendas", "bug/{id}"), (convencao.Nivel, convencao.Time, convencao.Valor));
    }
}
