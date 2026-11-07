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
    public void Sem_o_grupo_fixo_o_grupo_e_um_por_usuario()
    {
        Assert.Equal("rg-devkit-u42", RegrasDaProvisao.NomeDoGrupo("rg-devkit-u", 42));
        Assert.Equal("rg-devkit-u42", RegrasDaProvisao.NomeDoGrupo(" ", 42));
        Assert.Equal("rg-devkit-u42", RegrasDaProvisao.GrupoDoRecurso(new OpcoesDaProvisao(), 42));
    }

    [Fact]
    public void Com_o_grupo_fixo_todo_usuario_vai_para_ele()
    {
        var opcoes = new OpcoesDaProvisao { Grupo = " devkit-clients " };

        Assert.Equal("devkit-clients", RegrasDaProvisao.GrupoDoRecurso(opcoes, 42));
        Assert.Equal("devkit-clients", RegrasDaProvisao.GrupoDoRecurso(opcoes, 7));
        // Os nomes continuam únicos por usuário dentro do grupo compartilhado (o usuário em base 36 no nome).
        Assert.NotEqual(RegrasDaProvisao.NomeDoAplicativo(Contracts.V1.ProvisaoV1.TipoAppService, 42, "api", "abcd"),
            RegrasDaProvisao.NomeDoAplicativo(Contracts.V1.ProvisaoV1.TipoAppService, 7, "api", "abcd"));
        Assert.NotEqual(RegrasDaProvisao.NomeDaConta(42, "abcd1234"), RegrasDaProvisao.NomeDaConta(7, "abcd1234"));
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

    [Theory]
    [InlineData("api-pedidos", true)]
    [InlineData("site2", true)]
    [InlineData("abc", true)]
    [InlineData("ab", false)]
    [InlineData("api-", false)]
    [InlineData("1api", false)]
    [InlineData("api--pedidos", false)]
    [InlineData("Api-Pedidos", false)]
    [InlineData("api_pedidos", false)]
    [InlineData("um-nome-longo-demais-x", false)]
    [InlineData(null, false)]
    public void O_nome_do_usuario_e_curto_minusculo_e_sem_hifen_nas_pontas(string? rotulo, bool valido)
        => Assert.Equal(valido, RegrasDaProvisao.RotuloValido(rotulo));

    [Fact]
    public void Os_nomes_no_azure_levam_o_prefixo_o_usuario_o_nome_e_o_sufixo_e_cabem_no_static_web_app()
    {
        Assert.Equal("app-u2-api-pedidos-7f3k", RegrasDaProvisao.NomeDoAplicativo(ProvisaoV1.TipoAppService, 2, "api-pedidos", "7f3k"));
        Assert.Equal("swa-u2-site-docs-2m9q", RegrasDaProvisao.NomeDoAplicativo(ProvisaoV1.TipoStaticWebApp, 2, "site-docs", "2m9q"));
        Assert.Equal("asp-u2-api-pedidos-7f3k", RegrasDaProvisao.NomeDoPlanoDoServico(2, "api-pedidos", "7f3k"));
        Assert.Matches("^[a-z0-9]{4}$", RegrasDaProvisao.Sufixo());
        Assert.True(RegrasDaProvisao.NomeDoAplicativo(ProvisaoV1.TipoStaticWebApp, int.MaxValue, new string('a', 20), "zzzz").Length <= 40);
    }

    [Fact]
    public void O_pedido_de_aplicativo_valida_tipo_nome_regiao_do_tipo_e_plano_com_os_padroes()
    {
        var opcoes = Opcoes();

        var (padrao, semErro) = RegrasDaProvisao.ValidarAplicativo(new PedidoDeAplicativoV1("App-Service", " api-pedidos ", null, null), opcoes);
        Assert.Empty(semErro);
        Assert.Equal((ProvisaoV1.TipoAppService, "api-pedidos", "brazilsouth", "F1"), (padrao!.Tipo, padrao.Rotulo, padrao.Regiao, padrao.Plano.Id));

        // O Static Web App não existe em brazilsouth: a região padrão dele é a primeira das dele.
        var (site, _) = RegrasDaProvisao.ValidarAplicativo(new PedidoDeAplicativoV1(ProvisaoV1.TipoStaticWebApp, "site-docs", null, "standard"), opcoes);
        Assert.Equal(("eastus2", "Standard", 48.00m), (site!.Regiao, site.Plano.Id, site.Plano.CustoMensal));

        var (nada, erros) = RegrasDaProvisao.ValidarAplicativo(new PedidoDeAplicativoV1(ProvisaoV1.TipoStaticWebApp, "x", "brazilsouth", "P1v3"), opcoes);
        Assert.Null(nada);
        Assert.Equal(["nome", "plano", "regiao"], erros.Keys.Order());
        Assert.Equal(["tipo"], RegrasDaProvisao.ValidarAplicativo(new PedidoDeAplicativoV1(ProvisaoV1.TipoFila, "fila-x", null, null), opcoes).Erros.Keys);
        Assert.Equal(["tipo"], RegrasDaProvisao.ValidarAplicativo(null, opcoes).Erros.Keys);
    }

    [Fact]
    public void Os_planos_da_configuracao_substituem_os_padrao_e_a_cota_e_por_tipo()
    {
        var opcoes = Opcoes(o =>
        {
            o.PlanosDoAppService = [new PlanoConfigurado { Id = "S1", Rotulo = "S1 · Linux", CustoMensal = 300m }, new PlanoConfigurado { Id = " " }];
            o.MaximoDeAppServicesPorUsuario = 4;
            o.MaximoDeStaticWebAppsPorUsuario = 0;
        });

        Assert.Equal([new PlanoDeAplicativoV1("S1", "S1 · Linux", 300m)], RegrasDaProvisao.Planos(ProvisaoV1.TipoAppService, opcoes));
        Assert.Equal(RegrasDaProvisao.PlanosPadraoDoStaticWebApp, RegrasDaProvisao.Planos(ProvisaoV1.TipoStaticWebApp, opcoes));
        Assert.Equal((4, 1, 1), (RegrasDaProvisao.Maximo(ProvisaoV1.TipoAppService, opcoes), RegrasDaProvisao.Maximo(ProvisaoV1.TipoStaticWebApp, opcoes),
            RegrasDaProvisao.Maximo(ProvisaoV1.TipoFila, opcoes)));
        Assert.Equal(ProvisaoV1.TiposDeAplicativo, RegrasDaProvisao.Aplicativos(opcoes).Select(a => a.Tipo));
    }

    [Fact]
    public void A_vaga_e_a_primeira_livre_e_nao_ha_com_a_cota_cheia()
    {
        Assert.Equal(1, RegrasDaProvisao.Vaga([], 2));
        Assert.Equal(1, RegrasDaProvisao.Vaga([2], 2));
        Assert.Equal(3, RegrasDaProvisao.Vaga([1, 2], 3));
        Assert.Null(RegrasDaProvisao.Vaga([1, 2], 2));
    }

    [Theory]
    [InlineData(0, 61.70, false)]
    [InlineData(61.70, 61.70, false)]
    [InlineData(123.40, 0, false)]
    [InlineData(123.40, 48, true)]
    [InlineData(150, 0, true)]
    public void O_teto_do_custo_barra_o_que_passaria_dele_e_o_que_ja_esta_nele(double atual, double novo, bool passa)
        => Assert.Equal(passa, RegrasDaProvisao.PassaDoTeto((decimal)atual, (decimal)novo, Opcoes()));

    [Fact]
    public void A_cota_soma_so_os_aplicativos_e_marca_o_alerta_e_o_bloqueio()
    {
        var opcoes = Opcoes();
        var ativos = new[] { (ProvisaoV1.TipoAppService, 61.70m), (ProvisaoV1.TipoAppService, 61.70m), (ProvisaoV1.TipoStaticWebApp, 0m), (ProvisaoV1.TipoFila, 0m) };

        var cota = RegrasDaProvisao.Cota(ativos, opcoes);

        Assert.Equal((123.40m, 150m, 80, true, false), (cota.CustoDoMes, cota.LimiteDeCusto, cota.AlertaDeCustoPercentual, cota.Alerta, cota.Bloqueado));
        Assert.Equal([new UsoDoTipoV1(ProvisaoV1.TipoAppService, 2, 2), new UsoDoTipoV1(ProvisaoV1.TipoStaticWebApp, 1, 3)], cota.Tipos);
        Assert.False(RegrasDaProvisao.EmAlerta(119.99m, opcoes));
        Assert.True(RegrasDaProvisao.Bloqueado(150m, opcoes));
        // Sem teto, nada avisa nem bloqueia.
        var semTeto = Opcoes(o => o.LimiteDeCustoMensal = 0);
        Assert.False(RegrasDaProvisao.EmAlerta(10_000m, semTeto) || RegrasDaProvisao.PassaDoTeto(10_000m, 500m, semTeto));
    }

    [Fact]
    public void A_falha_parcial_e_a_criacao_que_falhou_depois_de_um_passo_feito()
    {
        Assert.Equal("plano", RegrasDaProvisao.ComPasso(null, ProvisaoV1.PassoPlano));
        Assert.Equal("plano", RegrasDaProvisao.ComPasso("plano", ProvisaoV1.PassoPlano));
        Assert.Equal(["plano", "aplicativo"], RegrasDaProvisao.Passos("plano, aplicativo"));

        Assert.True(RegrasDaProvisao.Parcial(ProvisaoV1.EstadoFalhou, ProvisaoV1.OperacaoCriar, "plano"));
        Assert.False(RegrasDaProvisao.Parcial(ProvisaoV1.EstadoFalhou, ProvisaoV1.OperacaoCriar, ""));
        Assert.False(RegrasDaProvisao.Parcial(ProvisaoV1.EstadoPronto, ProvisaoV1.OperacaoCriar, "plano"));
        Assert.False(RegrasDaProvisao.Parcial(ProvisaoV1.EstadoFalhou, ProvisaoV1.OperacaoRemover, "plano"));
        Assert.True(RegrasDaProvisao.RemocaoPedeSegundoFator(ProvisaoV1.TipoStaticWebApp));
        Assert.False(RegrasDaProvisao.RemocaoPedeSegundoFator(ProvisaoV1.TipoFila));
    }
}
