using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// As avaliações de entrega (US #417) na base de verdade: uma por (máquina, sessão, turno), a mais recente
/// vence (o atrasado e o reenvio não mudam nada), o evento ainda conta no total diário, e as métricas
/// respeitam período, máquina e o escopo do gestor.
/// </summary>
public sealed class AvaliacoesDeEntregaTests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new();
    private static readonly DateTimeOffset Hoje = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Periodo Outubro = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<BatchResultV1> Enviar(int maquina, params TelemetryEventV1[] eventos)
        => _base.ComAsync(sp => sp.GetRequiredService<IIngestaoDeTelemetria>()
            .RegistrarAsync(maquina, new TelemetryBatchV1("v1", "x", "140", eventos), default));

    private Task<MetricasDeFeedbackV1> Metricas(EscopoDoPainel escopo, int? maquina = null)
        => _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().MetricasDeFeedbackAsync(escopo, Outubro, maquina, default));

    private Task<List<AvaliacaoDeEntrega>> Gravadas()
        => _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().AvaliacoesDeEntrega.AsNoTracking().ToListAsync());

    private static TelemetryEventV1 Avaliada(string id, DateTimeOffset em, string sessao, int turno, bool boa, string fluxo = "revisao", string agente = "claude")
        => new(id, TiposDeEvento.EntregaAvaliada, sessao, 1, boa ? 1 : 0, boa ? string.Empty : "outro", em,
            Avaliacao: new AvaliacaoV1(boa, boa ? string.Empty : "outro", agente, "opus-5", fluxo, 417, turno));

    [Fact]
    public async Task O_mais_recente_vence_e_o_atrasado_nao_volta_a_avaliacao_para_tras()
    {
        var maquina = await _base.MaquinaAsync("m-1");

        await Enviar(maquina, Avaliada("a1", Hoje, "s1", 3, true));
        await Enviar(maquina, Avaliada("a2", Hoje.AddHours(1), "s1", 3, false));
        await Enviar(maquina, Avaliada("a0", Hoje.AddHours(-1), "s1", 3, true)); // chegou depois, mas é mais antigo

        var avaliacao = Assert.Single(await Gravadas());
        Assert.Equal((false, "a2", "outro"), (avaliacao.Boa, avaliacao.EventId, avaliacao.Motivo));

        // Os três eventos contam no total diário ("quantas avaliações foram dadas").
        var totais = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().TotaisDiarios.AsNoTracking()
            .Where(t => t.Tipo == TiposDeEvento.EntregaAvaliada).SumAsync(t => t.Eventos));
        Assert.Equal(3L, totais);
    }

    [Fact]
    public async Task No_mesmo_lote_o_ultimo_vence_e_o_reenvio_nao_muda_nada()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        var lote = new[] { Avaliada("a2", Hoje.AddMinutes(5), "s1", 1, false), Avaliada("a1", Hoje, "s1", 1, true) };

        await Enviar(maquina, lote);
        var reenvio = await Enviar(maquina, lote);

        Assert.Equal(new BatchResultV1(2, 0, 2, 0), reenvio);
        Assert.False(Assert.Single(await Gravadas()).Boa);
    }

    [Fact]
    public async Task Evento_sem_avaliacao_ou_sem_sessao_nao_cria_avaliacao()
    {
        var maquina = await _base.MaquinaAsync("m-1");

        var resultado = await Enviar(maquina,
            new TelemetryEventV1("v1", TiposDeEvento.EntregaAvaliada, "s1", 1, 1, string.Empty, Hoje),
            Avaliada("v2", Hoje, " ", 1, true),
            Avaliada("v3", Hoje, "s1", -1, true));

        Assert.Equal(new BatchResultV1(3, 3, 0, 0), resultado);
        Assert.Empty(await Gravadas());
    }

    [Fact]
    public async Task Metricas_somam_por_fluxo_e_agente_e_filtram_por_maquina_e_periodo()
    {
        var m1 = await _base.MaquinaAsync("m-1");
        var m2 = await _base.MaquinaAsync("m-2");
        await Enviar(m1, Avaliada("a1", Hoje, "s1", 1, true), Avaliada("a2", Hoje.AddDays(1), "s1", 2, false, fluxo: "", agente: ""));
        await Enviar(m2, Avaliada("b1", Hoje, "s1", 1, true, agente: "glm"), Avaliada("b2", Hoje.AddMonths(-2), "s1", 2, false));

        var todas = await Metricas(EscopoDoPainel.Tudo);
        var soM1 = await Metricas(EscopoDoPainel.Tudo, m1);

        // A mesma (sessão, turno) em duas máquinas são duas avaliações; a de fora do período não entra.
        Assert.Equal((3, 2, 1), (todas.Total, todas.Positivos, todas.Negativos));
        Assert.Equal(new[] { new FeedbackPorChaveV1("revisao", 2, 2, 0), new FeedbackPorChaveV1(ChavesDoFeedback.SemFluxo, 1, 0, 1) }, todas.PorFluxo);
        Assert.Equal(
            new[] { new FeedbackPorChaveV1(ChavesDoFeedback.SemAgente, 1, 0, 1), new FeedbackPorChaveV1("claude", 1, 1, 0), new FeedbackPorChaveV1("glm", 1, 1, 0) },
            todas.PorAgente);
        Assert.Equal(new[] { new FeedbackPorDiaV1(new DateOnly(2026, 10, 3), 2, 0), new FeedbackPorDiaV1(new DateOnly(2026, 10, 4), 0, 1) }, todas.PorDia);
        Assert.Equal(2, soM1.Total);
    }

    [Fact]
    public async Task Gestor_ve_so_as_avaliacoes_das_maquinas_da_empresa_depois_do_consentimento()
    {
        var a = await _base.ComAsync(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova("A", "Empresarial", 5), default));
        var deA = await _base.MaquinaAsync("m-a", a.CodigoDeAdesao, "Ana");
        var anonima = await _base.MaquinaAsync("m-x");
        await Enviar(deA, Avaliada("a1", _base.Relogio.Agora.AddMinutes(1), "s1", 1, true));
        await Enviar(deA, Avaliada("a0", _base.Relogio.Agora.AddDays(-1), "s0", 1, false)); // antes do consentimento
        await Enviar(anonima, Avaliada("x1", _base.Relogio.Agora.AddMinutes(1), "s1", 1, false));

        var doGestor = await Metricas(new EscopoDoPainel(a.Id));
        var lista = await _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().FeedbackAsync(new EscopoDoPainel(a.Id), Outubro, null, 1, 50, default));

        Assert.Equal((1, 1, 0), (doGestor.Total, doGestor.Positivos, doGestor.Negativos));
        Assert.Equal(deA, Assert.Single(lista.Itens).Maquina);
        Assert.Equal(3, (await Metricas(EscopoDoPainel.Tudo)).Total);
    }

    [Fact]
    public void Avaliacao_corta_os_textos_e_descarta_a_torta()
    {
        var torta = new AvaliacaoV1(true, new string('m', 300), new string('a', 80), " opus ", "  ", 0, 2);

        var avaliacao = RegrasDeAvaliacao.Avaliacao(torta, new string('s', 150))!;

        Assert.Equal((ValidadorDeLote.TamanhoMaximoDoTexto, RegrasDeAvaliacao.TamanhoMaximoDoAgente, RegrasDeAvaliacao.TamanhoMaximoDaSessao),
            (avaliacao.Motivo.Length, avaliacao.Agente.Length, avaliacao.SessaoId.Length));
        Assert.Equal(("opus", "", (int?)null), (avaliacao.Modelo, avaliacao.Fluxo, avaliacao.WorkItemId));
        Assert.Null(RegrasDeAvaliacao.Avaliacao(null, "s1"));
        Assert.Null(RegrasDeAvaliacao.Avaliacao(torta, ""));
        Assert.Null(RegrasDeAvaliacao.Avaliacao(torta with { Turno = -1 }, "s1"));
        Assert.Equal((ChavesDoFeedback.SemFluxo, ChavesDoFeedback.SemAgente, "revisao"),
            (RegrasDeAvaliacao.ChaveDoFluxo(""), RegrasDeAvaliacao.ChaveDoAgente(null), RegrasDeAvaliacao.ChaveDoFluxo("revisao")));
    }

    [Fact]
    public void Periodo_invertido_e_invalido_e_o_resto_vale()
    {
        Assert.NotEmpty(RegrasDeAvaliacao.ValidarPeriodo(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 1)));
        Assert.Empty(RegrasDeAvaliacao.ValidarPeriodo(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)));
        Assert.Empty(RegrasDeAvaliacao.ValidarPeriodo(null, new DateOnly(2026, 10, 1)));
        Assert.Empty(RegrasDeAvaliacao.ValidarPeriodo(new DateOnly(2026, 10, 10), null));
    }

    [Fact]
    public void Script_do_azure_sql_cria_a_tabela_das_avaliacoes_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("AvaliacoesDeEntrega.sql");

        Assert.Contains("IF OBJECT_ID(N'[dbo].[AvaliacoesDeEntrega]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("AvaliacoesDeEntrega"))
            Assert.Contains(coluna, script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_AvaliacoesDeEntrega_MaquinaId_SessaoId_Turno]", script);
    }
}
