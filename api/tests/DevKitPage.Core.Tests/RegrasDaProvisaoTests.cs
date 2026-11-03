using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras da provisão: de onde vem a liberação (a permissão vence; o plano é o tier de assinatura), os nomes no Azure,
/// a região, o recuo exponencial entre as tentativas, as transições e a máscara do erro guardado.
/// </summary>
public sealed class RegrasDaProvisaoTests
{
    private static OpcoesDaProvisao Opcoes(Action<OpcoesDaProvisao>? ajustar = null)
    {
        var opcoes = new OpcoesDaProvisao();
        ajustar?.Invoke(opcoes);
        return opcoes;
    }

    [Theory]
    [InlineData(true, null, new string[0], ProvisaoV1.OrigemPermissao)]
    [InlineData(true, "Empresarial", new[] { "Empresarial" }, ProvisaoV1.OrigemPermissao)]
    [InlineData(false, "empresarial", new[] { "Empresarial" }, ProvisaoV1.OrigemPlano)]
    [InlineData(false, "Básico", new[] { "Empresarial" }, ProvisaoV1.OrigemNenhuma)]
    [InlineData(false, "Empresarial", new string[0], ProvisaoV1.OrigemNenhuma)]
    [InlineData(false, null, new[] { "Empresarial" }, ProvisaoV1.OrigemNenhuma)]
    public void A_permissao_vence_e_o_plano_so_libera_quando_configurado(bool permissao, string? plano, string[] planos, string esperada)
        => Assert.Equal(esperada, RegrasDaProvisao.Origem(permissao, plano, planos));

    [Fact]
    public void O_nome_da_conta_e_valido_no_azure_e_leva_o_usuario_em_base_36()
    {
        var nome = RegrasDaProvisao.NomeDaConta(1295, "abcd1234");

        Assert.Equal("dkqzzabcd1234", nome);
        Assert.True(RegrasDaProvisao.ContaValida(nome));
        Assert.True(RegrasDaProvisao.ContaValida(RegrasDaProvisao.NomeDaConta(int.MaxValue)));
        Assert.True(RegrasDaProvisao.NomeDaConta(int.MaxValue).Length <= RegrasDaProvisao.TamanhoMaximoDaConta);
    }

    [Fact]
    public void Dois_sorteios_do_mesmo_usuario_dao_nomes_diferentes()
        => Assert.NotEqual(RegrasDaProvisao.NomeDaConta(7), RegrasDaProvisao.NomeDaConta(7));

    [Theory]
    [InlineData("dk")]
    [InlineData("DKQ1abc")]
    [InlineData("dkq-1abc")]
    [InlineData("dkq1234567890123456789012")]
    [InlineData(null)]
    public void Nome_fora_da_regra_do_azure_nao_vale(string? nome) => Assert.False(RegrasDaProvisao.ContaValida(nome));

    [Fact]
    public void O_grupo_e_um_por_usuario()
    {
        Assert.Equal("rg-devkit-u42", RegrasDaProvisao.NomeDoGrupo("rg-devkit-u", 42));
        Assert.Equal("rg-devkit-u42", RegrasDaProvisao.NomeDoGrupo(" ", 42));
    }

    [Fact]
    public void A_regiao_vazia_e_a_padrao_e_a_fora_da_lista_e_recusada()
    {
        var opcoes = Opcoes(o => o.Regioes = ["BrazilSouth", "eastus2", "eastus2"]);

        Assert.Equal(["brazilsouth", "eastus2"], RegrasDaProvisao.Regioes(opcoes));
        Assert.Equal("brazilsouth", RegrasDaProvisao.Regiao(null, opcoes));
        Assert.Equal("eastus2", RegrasDaProvisao.Regiao(" EASTUS2 ", opcoes));
        Assert.Null(RegrasDaProvisao.Regiao("westeurope", opcoes));
    }

    [Fact]
    public void Sem_regioes_configuradas_valem_as_padrao_e_a_padrao_fora_da_lista_vira_a_primeira()
    {
        Assert.Equal(RegrasDaProvisao.RegioesPadrao, RegrasDaProvisao.Regioes(Opcoes()));
        Assert.Equal("eastus2", RegrasDaProvisao.RegiaoPadrao(Opcoes(o => { o.Regioes = ["eastus2"]; o.RegiaoPadrao = "brazilsouth"; })));
    }

    [Fact]
    public void A_espera_dobra_a_cada_tentativa_ate_o_teto()
    {
        var opcoes = Opcoes(o => { o.EsperaInicialSegundos = 30; o.EsperaMaximaSegundos = 100; });

        Assert.Equal(TimeSpan.FromSeconds(30), RegrasDaProvisao.Espera(1, opcoes));
        Assert.Equal(TimeSpan.FromSeconds(60), RegrasDaProvisao.Espera(2, opcoes));
        Assert.Equal(TimeSpan.FromSeconds(100), RegrasDaProvisao.Espera(3, opcoes));
        Assert.Equal(TimeSpan.FromSeconds(100), RegrasDaProvisao.Espera(50, opcoes));
    }

    [Fact]
    public void So_a_falha_transitoria_com_tentativa_sobrando_volta_ao_worker()
    {
        var opcoes = Opcoes(o => o.MaximoDeTentativas = 3);

        Assert.True(RegrasDaProvisao.TentaDeNovo(transitoria: true, tentativas: 2, opcoes));
        Assert.False(RegrasDaProvisao.TentaDeNovo(transitoria: true, tentativas: 3, opcoes));
        Assert.False(RegrasDaProvisao.TentaDeNovo(transitoria: false, tentativas: 1, opcoes));
    }

    [Theory]
    [InlineData(ProvisaoV1.EstadoPronto, ProvisaoV1.OperacaoCriar, Remocao.Pedir)]
    [InlineData(ProvisaoV1.EstadoFalhou, ProvisaoV1.OperacaoCriar, Remocao.Pedir)]
    [InlineData(ProvisaoV1.EstadoFalhou, ProvisaoV1.OperacaoRemover, Remocao.Pedir)]
    [InlineData(ProvisaoV1.EstadoPedido, ProvisaoV1.OperacaoCriar, Remocao.AguardeACriacao)]
    [InlineData(ProvisaoV1.EstadoEmProvisao, ProvisaoV1.OperacaoCriar, Remocao.AguardeACriacao)]
    [InlineData(ProvisaoV1.EstadoPedido, ProvisaoV1.OperacaoRemover, Remocao.JaPedida)]
    [InlineData(ProvisaoV1.EstadoEmProvisao, ProvisaoV1.OperacaoRemover, Remocao.JaPedida)]
    [InlineData(ProvisaoV1.EstadoRemovido, ProvisaoV1.OperacaoRemover, Remocao.JaFeita)]
    public void A_remocao_depende_do_estado_e_da_operacao(string estado, string operacao, Remocao esperada)
        => Assert.Equal(esperada, RegrasDaProvisao.DecidirRemocao(estado, operacao));

    [Fact]
    public void So_o_que_falhou_se_repete_e_so_o_removido_sai_da_cota()
    {
        Assert.Equal([ProvisaoV1.EstadoFalhou], ProvisaoV1.Estados.Where(RegrasDaProvisao.PodeRepetir));
        Assert.Equal([ProvisaoV1.EstadoRemovido], ProvisaoV1.Estados.Where(e => !RegrasDaProvisao.Ativo(e)));
    }

    [Fact]
    public void O_erro_guardado_e_uma_linha_sem_segredo_nem_guid_e_cabe_na_coluna()
    {
        var erro = RegrasDaProvisao.Erro(
            "O Azure recusou\r\na assinatura 3f2b9c0e-5d7a-4c1b-9e2f-1a2b3c4d5e6f com Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.assinatura " + new string('x', 900));

        Assert.DoesNotContain('\n', erro);
        Assert.DoesNotContain("3f2b9c0e-5d7a-4c1b-9e2f-1a2b3c4d5e6f", erro);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", erro);
        Assert.True(erro.Length <= RegrasDaProvisao.TamanhoMaximoDoErro);
        Assert.StartsWith("O Azure recusou a assinatura", erro);
    }
}
