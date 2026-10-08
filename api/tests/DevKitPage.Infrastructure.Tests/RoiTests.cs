using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O ROI por work item (US #387) na base de verdade: a foto é uma por (máquina, item), o mais recente
/// vence (o atrasado não volta para trás), o evento ainda conta no total diário, e a consulta respeita
/// período, máquina e o escopo do gestor.
/// </summary>
public sealed class RoiTests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new();
    private static readonly DateTimeOffset Hoje = new(2026, 10, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Periodo Outubro = new(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<BatchResultV1> Enviar(int maquina, params TelemetryEventV1[] eventos)
        => _base.ComAsync(sp => sp.GetRequiredService<IIngestaoDeTelemetria>()
            .RegistrarAsync(maquina, new TelemetryBatchV1("v1", "x", "1.6.0", eventos), default));

    private Task<RoiResposta> Consultar(EscopoDoPainel escopo, int? maquina = null)
        => _base.ComAsync(sp => sp.GetRequiredService<IConsultasDoPainel>().RoiAsync(escopo, Outubro, maquina, default));

    private static RoiV1 Foto(int item, int turnos, decimal horas, bool aberto = false, double? lead = 3.5)
        => new(item, "User Story", aberto ? "Active" : "Closed", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3),
            turnos, 3, horas, 30m, null, aberto ? null : lead, aberto, 2, 1);

    private static TelemetryEventV1 Roi(string id, DateTimeOffset em, RoiV1? foto)
        => new(id, TiposDeEvento.RoiCalculado, string.Empty, 1, foto?.TurnosDoAgente, foto?.WorkItem.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0", em, Roi: foto);

    [Fact]
    public async Task O_mais_recente_vence_e_o_atrasado_nao_volta_a_foto_para_tras()
    {
        var maquina = await _base.MaquinaAsync("m-1");

        await Enviar(maquina, Roi("r1", Hoje, Foto(387, 42, 31.5m)));
        await Enviar(maquina, Roi("r2", Hoje.AddHours(2), Foto(387, 50, 40m)));
        await Enviar(maquina, Roi("r0", Hoje.AddHours(-1), Foto(387, 10, 5m))); // chegou depois, mas é mais antigo

        var fotos = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RoisDeWorkItem.AsNoTracking().ToListAsync());
        var foto = Assert.Single(fotos);
        Assert.Equal((50, 40m, "r2"), (foto.TurnosDoAgente, foto.Horas, foto.EventId));

        // Os três eventos contam no total diário ("quantos ROIs foram calculados").
        var totais = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().TotaisDiarios.AsNoTracking()
            .Where(t => t.Tipo == TiposDeEvento.RoiCalculado).SumAsync(t => t.Eventos));
        Assert.Equal(3L, totais);
    }

    [Fact]
    public async Task No_mesmo_lote_o_ultimo_vence_e_o_reenvio_nao_muda_nada()
    {
        var maquina = await _base.MaquinaAsync("m-1");
        var lote = new[] { Roi("r2", Hoje.AddMinutes(5), Foto(387, 20, 10m)), Roi("r1", Hoje, Foto(387, 5, 2m)) };

        await Enviar(maquina, lote);
        var reenvio = await Enviar(maquina, lote);

        Assert.Equal(new BatchResultV1(2, 0, 2, 0), reenvio);
        var foto = Assert.Single(await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RoisDeWorkItem.AsNoTracking().ToListAsync()));
        Assert.Equal(20, foto.TurnosDoAgente);
    }

    [Fact]
    public async Task Evento_sem_roi_ou_com_item_invalido_nao_cria_foto()
    {
        var maquina = await _base.MaquinaAsync("m-1");

        var resultado = await Enviar(maquina, Roi("r1", Hoje, null), Roi("r2", Hoje, Foto(0, 1, 1m)));

        Assert.Equal(new BatchResultV1(2, 2, 0, 0), resultado);
        Assert.Empty(await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RoisDeWorkItem.ToListAsync()));
    }

    [Fact]
    public async Task Consulta_calcula_horas_por_turno_e_totais_e_filtra_por_maquina_e_periodo()
    {
        var m1 = await _base.MaquinaAsync("m-1");
        var m2 = await _base.MaquinaAsync("m-2");
        await Enviar(m1, Roi("a1", Hoje, Foto(387, 42, 31.5m, lead: 3.5)), Roi("a2", Hoje.AddHours(1), Foto(390, 0, 2m, aberto: true)));
        await Enviar(m2, Roi("b1", Hoje.AddHours(2), Foto(387, 10, 8m, lead: 1.5)), Roi("b2", Hoje.AddMonths(-2), Foto(100, 1, 1m)));

        var todas = await Consultar(EscopoDoPainel.Tudo);
        var soM1 = await Consultar(EscopoDoPainel.Tudo, m1);

        // A mesma US em duas máquinas são duas linhas; a de fora do período não entra.
        Assert.Equal(new[] { (m2, 387), (m1, 390), (m1, 387) }, todas.Itens.Select(i => (i.MaquinaId, i.WorkItem)));
        var item = todas.Itens.Single(i => i.MaquinaId == m1 && i.WorkItem == 387);
        Assert.Equal((0.75m, 3.5, "máquina m-1"), (item.HorasPorTurno!.Value, item.LeadTimeDias!.Value, item.Apelido));
        Assert.Null(todas.Itens.Single(i => i.WorkItem == 390).HorasPorTurno); // sem turno: sem divisor
        Assert.Equal(new RoiTotais(3, 52, 41.5m, 2.5, 6, 3), todas.Totais); // o lead time médio só dos encerrados
        Assert.Equal(2, soM1.Totais.Itens);
    }

    [Fact]
    public async Task Gestor_ve_so_o_roi_das_maquinas_da_empresa_que_consentiram()
    {
        var empresas = (IServiceProvider sp) => sp.GetRequiredService<IEmpresas>();
        var a = await _base.ComAsync(sp => empresas(sp).CriarAsync(new EmpresaNova("A", "Empresarial", 5), default));
        var deA = await _base.MaquinaAsync("m-a", a.CodigoDeAdesao, "Ana");
        var anonima = await _base.MaquinaAsync("m-x");
        await Enviar(deA, Roi("a1", _base.Relogio.Agora.AddMinutes(1), Foto(387, 4, 2m)));
        await Enviar(deA, Roi("a0", _base.Relogio.Agora.AddDays(-1), Foto(380, 4, 2m))); // antes do consentimento
        await Enviar(anonima, Roi("x1", _base.Relogio.Agora.AddMinutes(1), Foto(999, 4, 2m)));

        var doGestor = await Consultar(new EscopoDoPainel(a.Id));

        var item = Assert.Single(doGestor.Itens);
        Assert.Equal((387, "Ana"), (item.WorkItem, item.Colaborador));
        Assert.Equal(3, (await Consultar(EscopoDoPainel.Tudo)).Totais.Itens);
    }

    [Fact]
    public void Foto_corta_os_textos_e_traz_os_numeros_para_a_faixa()
    {
        var torta = new RoiV1(7, new string('t', 80), " Closed ", new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1),
            -1, -2, -3m, 1_000_000_000m, -1m, double.NaN, false, 1, 5);

        var foto = RegrasDeRoi.Foto(torta)!;

        Assert.Equal(RegrasDeRoi.TamanhoMaximoDoTexto, foto.Tipo.Length);
        Assert.Equal("Closed", foto.Estado);
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)), (foto.De, foto.Ate));
        Assert.Equal((0, 0, 0m, RegrasDeRoi.HorasMaximas, 0m), (foto.TurnosDoAgente, foto.Sessoes, foto.Horas, foto.HorasNoBoard, foto.HorasNoTimesheet!.Value));
        Assert.Null(foto.LeadTimeDias);
        Assert.Equal(1, foto.PullRequestsMergeadas); // nunca mais que as PRs
        Assert.Null(RegrasDeRoi.Foto(null));
    }

    [Fact]
    public void Script_do_azure_sql_cria_a_tabela_do_roi_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("RoiDeWorkItem.sql");

        Assert.Contains("IF OBJECT_ID(N'[dbo].[RoisDeWorkItem]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("RoisDeWorkItem"))
            Assert.Contains(coluna, script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_RoisDeWorkItem_MaquinaId_WorkItemId]", script);
    }
}
