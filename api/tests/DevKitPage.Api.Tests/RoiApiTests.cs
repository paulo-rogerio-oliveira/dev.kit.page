using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O ROI por work item (US #387) de ponta a ponta: o <c>RoiCalculado</c> chega na MESMA ingestão do v1,
/// no JSON exato que o dev.kit manda, e o painel mostra a foto mais recente de cada (máquina, item).
/// </summary>
public sealed class RoiApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    /// <summary>O lote como o dev.kit o serializa (camelCase), com o evento no formato do contrato.</summary>
    private static StringContent Lote(string maquina, params string[] eventos) => new(
        $$"""{"versao":"v1","maquinaId":"{{maquina}}","versaoDevKit":"1.6.0","eventos":[{{string.Join(',', eventos)}}]}""",
        System.Text.Encoding.UTF8, "application/json");

    private static string RoiCalculado(string id, DateTimeOffset em, int turnos, decimal horas, string? timesheet = "31.5", string? leadTime = "3.5")
        => $$"""
           { "eventId": "{{id}}", "tipo": "RoiCalculado", "sessaoId": "", "quantidade": 1, "valor": {{turnos}},
             "detalhe": "387", "em": "{{em:yyyy-MM-ddTHH:mm:ssZ}}",
             "roi": { "workItem": 387, "tipo": "User Story", "estado": "Closed", "de": "2026-10-01", "ate": "2026-10-04",
                      "turnosDoAgente": {{turnos}}, "sessoes": 3, "horas": {{horas.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "horasNoBoard": 30,
                      "horasNoTimesheet": {{timesheet ?? "null"}}, "leadTimeDias": {{leadTime ?? "null"}}, "aberto": false,
                      "pullRequests": 2, "pullRequestsMergeadas": 1 } }
           """;

    private static readonly DateTimeOffset Agora = new(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, DateTimeOffset.UtcNow.Day, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_foto_aparece_o_envio_mais_novo_substitui_e_o_mais_antigo_nao()
    {
        var maquina = await _api.MaquinaAsync("maq-1");
        var admin = await _api.AdminAsync();

        var primeiro = await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1", RoiCalculado("r1", Agora.AddHours(1), 42, 31.5m)));
        Assert.Equal(HttpStatusCode.Accepted, primeiro.StatusCode);
        Assert.Equal(new BatchResultV1(1, 1, 0, 0), await primeiro.Content.ReadFromJsonAsync<BatchResultV1>());
        var depoisDoPrimeiro = (await admin.GetFromJsonAsync<RoiResposta>("/api/dashboard/roi"))!;

        await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1", RoiCalculado("r2", Agora.AddHours(2), 60, 45m, timesheet: null, leadTime: null)));
        var depoisDoNovo = (await admin.GetFromJsonAsync<RoiResposta>("/api/dashboard/roi"))!;

        await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1", RoiCalculado("r0", Agora.AddMinutes(30), 10, 5m)));
        var depoisDoAntigo = (await admin.GetFromJsonAsync<RoiResposta>("/api/dashboard/roi"))!;

        var foto = Assert.Single(depoisDoPrimeiro.Itens);
        Assert.Equal((387, "User Story", "Closed", 42, 3), (foto.WorkItem, foto.Tipo, foto.Estado, foto.TurnosDoAgente, foto.Sessoes));
        Assert.Equal((31.5m, 30m, (decimal?)31.5m, (decimal?)0.75m), (foto.Horas, foto.HorasNoBoard, foto.HorasNoTimesheet, foto.HorasPorTurno));
        Assert.Equal((3.5, false, 2, 1), (foto.LeadTimeDias, foto.Aberto, foto.PullRequests, foto.PullRequestsMergeadas));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 4)), (foto.De, foto.Ate));
        Assert.Equal("máquina maq-1", foto.Apelido);

        var nova = Assert.Single(depoisDoNovo.Itens);
        Assert.Equal((60, 45m, (decimal?)null, (double?)null), (nova.TurnosDoAgente, nova.Horas, nova.HorasNoTimesheet, nova.LeadTimeDias));
        Assert.Equal(Agora.AddHours(2), nova.AtualizadoEm);

        Assert.Equal(60, Assert.Single(depoisDoAntigo.Itens).TurnosDoAgente); // o atrasado não volta para trás
        Assert.Equal(new RoiTotais(1, 60, 45m, null, 2, 1), depoisDoAntigo.Totais);

        // Os três eventos contam como uso (o log), mesmo o que não mudou a foto.
        var eventos = (await admin.GetFromJsonAsync<Pagina<EventoDoLog>>("/api/dashboard/eventos"))!;
        Assert.Equal(3, eventos.Itens.Count(e => e.Tipo == TiposDeEvento.RoiCalculado));
    }

    [Fact]
    public async Task Evento_sem_roi_e_aceito_mas_nao_cria_foto()
    {
        var maquina = await _api.MaquinaAsync("maq-1");

        var envio = await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1",
            $$"""{"eventId":"r1","tipo":"RoiCalculado","sessaoId":"","quantidade":1,"valor":42,"detalhe":"387","em":"{{Agora:yyyy-MM-ddTHH:mm:ssZ}}"}"""));

        Assert.Equal(new BatchResultV1(1, 1, 0, 0), await envio.Content.ReadFromJsonAsync<BatchResultV1>());
        var admin = await _api.AdminAsync();
        var roi = (await admin.GetFromJsonAsync<RoiResposta>("/api/dashboard/roi"))!;
        Assert.Empty(roi.Itens);
        Assert.Equal(0, roi.Totais.Itens);
    }

    [Fact]
    public async Task Gestor_nao_le_o_roi_de_outra_empresa_e_a_chave_da_maquina_nao_le_o_painel()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var deB = await _api.MaquinaAsync("maq-b", b.CodigoDeAdesao, "Bruno");
        await deB.PostAsync("/api/telemetria/lote", Lote("maq-b", RoiCalculado("r1", DateTimeOffset.UtcNow, 4, 2m)));
        var gestorA = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var idDeB = (await admin.GetFromJsonAsync<MaquinaResumo[]>("/api/dashboard/maquinas"))!.Single(m => m.MaquinaId == "maq-b").Id;

        Assert.Empty((await gestorA.GetFromJsonAsync<RoiResposta>("/api/dashboard/roi"))!.Itens);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync($"/api/dashboard/roi?maquina={idDeB}")).StatusCode);
        Assert.Single((await admin.GetFromJsonAsync<RoiResposta>($"/api/dashboard/roi?maquina={idDeB}"))!.Itens);
        Assert.Equal(HttpStatusCode.Unauthorized, (await deB.GetAsync("/api/dashboard/roi")).StatusCode);
    }
}
