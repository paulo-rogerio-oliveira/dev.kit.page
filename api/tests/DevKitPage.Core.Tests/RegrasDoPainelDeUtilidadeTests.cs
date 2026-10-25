using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// O painel de utilidade (US #687), as regras puras: os recortes dos eventos novos (a amostra e a sugestão, sem campo novo
/// no wire v1), a chave de cada foto, os turnos do dia pelo desfecho — com a conta de antes para o dev.kit antigo —, o
/// resultado por sessão e onde as tasks travam.
/// </summary>
public sealed class RegrasDoPainelDeUtilidadeTests
{
    [Fact]
    public void Le_a_amostra_e_recusa_o_recorte_torto()
    {
        var amostra = RegrasDoPainelDeUtilidade.LerAmostra("disco-mb=8;memoria-mb=124;limite=disco+memoria")!;

        Assert.Equal((8L, 124L, true), (amostra.DiscoMb, amostra.MemoriaMb, amostra.NoLimite));
        Assert.False(RegrasDoPainelDeUtilidade.LerAmostra("disco-mb=9000;memoria-mb=4000;limite=nao")!.NoLimite);
        Assert.Null(RegrasDoPainelDeUtilidade.LerAmostra("qualquer coisa"));
        Assert.Equal(-1, RegrasDoPainelDeUtilidade.LerAmostra("limite=disco")!.DiscoMb);
    }

    [Fact]
    public void Le_a_sugestao_com_o_estado_e_a_us()
    {
        Assert.Equal(new SugestaoLida("Pergunta sem chegar ao dev", 3, "pendente", "#568"),
            RegrasDoPainelDeUtilidade.LerSugestao("Pergunta sem chegar ao dev|sugestao-3|pendente|#568"));
        Assert.Equal(string.Empty, RegrasDoPainelDeUtilidade.LerSugestao("Causa|sugestao-4|promovida")!.Us);
        Assert.Null(RegrasDoPainelDeUtilidade.LerSugestao("Causa|sem-id|pendente"));
        Assert.Null(RegrasDoPainelDeUtilidade.LerSugestao("|sugestao-1|pendente"));
    }

    [Fact]
    public void A_chave_de_cada_foto()
    {
        Assert.Equal("s1", RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.SituacaoDaTask, "s1", SituacoesDaTask.Executando, 3));
        Assert.Equal("s1", RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.PerguntaAoDev, "s1", EstadosDaPergunta.Aberta, null));
        Assert.Equal("#669", RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.FechamentoDaEntrega, "s1", "falhou", 669));
        Assert.Equal("s1", RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.FechamentoDaEntrega, "s1", "ok", null));
        Assert.Equal(string.Empty, RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.AmostraDaMaquina, "", "disco-mb=1;memoria-mb=1;limite=nao", 1));
        Assert.Equal("sugestao-3", RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.SugestaoDeImpacto, "", "Causa|sugestao-3|pendente|", null));
        Assert.Null(RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.SituacaoDaTask, "", SituacoesDaTask.Executando, null));
        Assert.Null(RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.AmostraDaMaquina, "", "torta", null));
        Assert.Null(RegrasDoPainelDeUtilidade.ChaveDaFoto(TiposDeEvento.TurnoExecutado, "s1", "claude", 10));
    }

    [Fact]
    public void Os_turnos_do_dia_pelo_desfecho_e_pela_conta_de_antes()
    {
        var dia = new DateOnly(2026, 10, 20);
        var comDesfecho = RegrasDoPainelDeUtilidade.TurnosDoDia(dia,
        [
            (TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.Trabalhou, 9), (TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.EsperouDev, 2),
            (TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.LimiteDeUso, 1), (TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.Falhou, 1),
            (TiposDeEvento.TurnoExecutado, "claude", 13),
        ]);
        var semDesfecho = RegrasDoPainelDeUtilidade.TurnosDoDia(dia,
            [(TiposDeEvento.TurnoExecutado, "claude", 10), (TiposDeEvento.TurnoFalhou, "limite-de-uso", 2), (TiposDeEvento.TurnoFalhou, "nao-classificada", 1)]);

        Assert.Equal(new TurnosDoDiaV1(dia, 9, 2, 1, 1), comDesfecho);
        Assert.Equal(new TurnosDoDiaV1(dia, 7, 0, 2, 1), semDesfecho);
        Assert.Equal(new TurnosDoDiaV1(dia, 0, 0, 0, 0), RegrasDoPainelDeUtilidade.TurnosDoDia(dia, []));
    }

    [Fact]
    public void O_resultado_por_sessao()
    {
        var inicio = new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);
        EventoDaSessao E(string sessao, string tipo, int minutos, string detalhe = "", long? valor = null) => new(sessao, tipo, detalhe, valor, inicio.AddMinutes(minutos));

        var resultado = RegrasDoPainelDeUtilidade.Sessoes(
        [
            // A: aprovada na primeira, 2 avaliações até o objetivo, 120 min até aprovar, 60 deles esperando o dev.
            E("A", TiposDeEvento.SessaoIniciada, 0), E("A", TiposDeEvento.ObjetivoAvaliado, 30, valor: 80), E("A", TiposDeEvento.ObjetivoAvaliado, 50, valor: 95),
            E("A", TiposDeEvento.ObjetivoCumprido, 55), E("A", TiposDeEvento.RevisaoHumana, 120, "aprovada", 3_600_000),
            // B: devolvida e depois aprovada (não é na primeira), 400 min.
            E("B", TiposDeEvento.SessaoIniciada, 0), E("B", TiposDeEvento.RevisaoHumana, 100, "devolvida", 600_000),
            E("B", TiposDeEvento.RevisaoHumana, 400, "commit", 1_200_000),
            // C: sem aprovação: não entra no tempo, só nas rodadas se cumpriu.
            E("C", TiposDeEvento.SessaoIniciada, 0), E("C", TiposDeEvento.ObjetivoAvaliado, 10, valor: 70),
        ]);

        Assert.Equal((2, 1), (resultado.Aprovadas, resultado.NaPrimeira));
        Assert.Equal(new[] { 2 }, resultado.Rodadas);
        Assert.Equal(new[] { 120d, 400d }, resultado.MinutosAteAprovar);
        Assert.Equal(new[] { 60d, 30d }, resultado.MinutosEsperandoDev);
        Assert.Equal(260d, RegrasDoPainelDeUtilidade.Mediana(resultado.MinutosAteAprovar));
        Assert.Null(RegrasDoPainelDeUtilidade.Mediana([]));
        Assert.Equal(2d, RegrasDoPainelDeUtilidade.Mediana([3d, 1d, 2d]));
    }

    [Fact]
    public void Onde_travam_agrupa_pela_causa_com_o_selo_da_mais_nova()
    {
        var causas = RegrasDoPainelDeUtilidade.OndeTravam(
        [
            new SugestaoLida("Esperou processo em segundo plano que morreu", 1, "descartada", ""),
            new SugestaoLida("Esperou processo em segundo plano que morreu", 3, "pendente", ""),
            new SugestaoLida("esperou processo em segundo plano que morreu", 2, "pendente", ""),
            new SugestaoLida("Pergunta sem chegar ao dev", 4, "promovida", "#568"),
            new SugestaoLida("Limite de uso sem troca de agente", 5, "descartada", "#681"),
            new SugestaoLida("Conflito no fechamento", 6, "descartada", ""),
        ]);

        Assert.Equal(("Esperou processo em segundo plano que morreu", 3L, "sugestão #3", TiposDeSelo.Sugestao),
            (causas[0].Causa, causas[0].Vezes, causas[0].Selo, causas[0].TipoDoSelo));
        Assert.Contains(causas, c => c is { Causa: "Pergunta sem chegar ao dev", Selo: "corrigido #568", TipoDoSelo: TiposDeSelo.Corrigido });
        Assert.Contains(causas, c => c is { Causa: "Limite de uso sem troca de agente", Selo: "#681", TipoDoSelo: TiposDeSelo.Us });
        Assert.Contains(causas, c => c is { Causa: "Conflito no fechamento", Selo: "", TipoDoSelo: TiposDeSelo.Nenhum });
        Assert.Empty(RegrasDoPainelDeUtilidade.OndeTravam([]));
    }
}
