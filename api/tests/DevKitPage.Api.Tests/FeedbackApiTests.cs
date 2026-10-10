using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using Microsoft.AspNetCore.Mvc;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O feedback das entregas (US #417) de ponta a ponta: o <c>EntregaAvaliada</c> chega na MESMA ingestão do
/// v1, no JSON exato que o dev.kit manda (com o campo opcional <c>avaliacao</c>), e as rotas
/// <c>/api/dashboard/feedback</c> e <c>/feedback/metricas</c> devolvem a avaliação mais recente de cada
/// (máquina, sessão, turno) e as métricas — com a autenticação, o período e o escopo das rotas do painel.
/// </summary>
public sealed class FeedbackApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    /// <summary>O lote como o dev.kit o serializa (camelCase), com o evento no formato do contrato.</summary>
    private static StringContent Lote(string maquina, params string[] eventos) => new(
        $$"""{"versao":"v1","maquinaId":"{{maquina}}","versaoDevKit":"140","eventos":[{{string.Join(',', eventos)}}]}""",
        System.Text.Encoding.UTF8, "application/json");

    private static string Avaliada(string id, DateTimeOffset em, string sessao, int turno, bool boa, string fluxo, string agente, string motivo = "", string workItem = "417")
        => $$"""
           { "eventId": "{{id}}", "tipo": "EntregaAvaliada", "sessaoId": "{{sessao}}", "quantidade": 1, "valor": {{(boa ? 1 : 0)}},
             "detalhe": "{{motivo}}", "em": "{{em:yyyy-MM-ddTHH:mm:ssZ}}",
             "avaliacao": { "boa": {{(boa ? "true" : "false")}}, "motivo": "{{motivo}}", "agente": "{{agente}}", "modelo": "opus-5",
                            "fluxo": "{{fluxo}}", "workItem": {{workItem}}, "turno": {{turno}} } }
           """;

    private static readonly DateTimeOffset Hoje = new(DateTimeOffset.UtcNow.Year, DateTimeOffset.UtcNow.Month, DateTimeOffset.UtcNow.Day, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("/api/dashboard/feedback")]
    [InlineData("/api/dashboard/feedback/metricas")]
    public async Task Sem_autenticacao_e_401_e_a_chave_da_maquina_tambem_nao_le(string rota)
    {
        var anonimo = _api.CreateClient();
        var maquina = await _api.MaquinaAsync("maq-1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await maquina.GetAsync(rota)).StatusCode);
    }

    [Theory]
    [InlineData("/api/dashboard/feedback?de=2026-10-10&ate=2026-10-01")]
    [InlineData("/api/dashboard/feedback/metricas?de=2026-10-10&ate=2026-10-01")]
    public async Task Periodo_invertido_e_400_com_problem_details(string rota)
    {
        var admin = await _api.AdminAsync();

        var resposta = await admin.GetAsync(rota);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Período inválido", (await resposta.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Periodo_de_um_dia_so_e_valido()
    {
        var admin = await _api.AdminAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/dashboard/feedback/metricas?de=2026-10-10&ate=2026-10-10")).StatusCode);
    }

    [Fact]
    public async Task Agregacao_confere_o_total_e_a_quebra_por_fluxo_por_agente_e_por_dia()
    {
        var maquina = await _api.MaquinaAsync("maq-1");
        var admin = await _api.AdminAsync();
        var ontem = Hoje.AddDays(-1);

        var envio = await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1",
            Avaliada("a1", Hoje.AddHours(1), "s1", 1, true, "revisao", "claude"),
            Avaliada("a2", Hoje.AddHours(2), "s1", 2, false, "revisao", "glm", "quebrou-build-ou-teste"),
            Avaliada("a3", ontem.AddHours(3), "s2", 1, true, "", "claude", workItem: "null"),
            Avaliada("a4", ontem.AddHours(4), "s3", 1, false, "implementar", "", "fora-do-padrao"),
            $$"""{"eventId":"t1","tipo":"TurnoExecutado","sessaoId":"s1","quantidade":1,"valor":1000,"detalhe":"claude","em":"{{Hoje:yyyy-MM-ddTHH:mm:ssZ}}"}"""));
        Assert.Equal(new BatchResultV1(5, 5, 0, 0), await envio.Content.ReadFromJsonAsync<BatchResultV1>());

        var metricas = (await admin.GetFromJsonAsync<MetricasDeFeedbackV1>("/api/dashboard/feedback/metricas"))!;

        Assert.Equal((4, 2, 2), (metricas.Total, metricas.Positivos, metricas.Negativos));
        Assert.Equal(
            new[] { new FeedbackPorChaveV1("revisao", 2, 1, 1), new FeedbackPorChaveV1(ChavesDoFeedback.SemFluxo, 1, 1, 0), new FeedbackPorChaveV1("implementar", 1, 0, 1) },
            metricas.PorFluxo);
        Assert.Equal(
            new[] { new FeedbackPorChaveV1("claude", 2, 2, 0), new FeedbackPorChaveV1(ChavesDoFeedback.SemAgente, 1, 0, 1), new FeedbackPorChaveV1("glm", 1, 0, 1) },
            metricas.PorAgente);
        Assert.Equal(
            new[] { new FeedbackPorDiaV1(DateOnly.FromDateTime(ontem.UtcDateTime), 1, 1), new FeedbackPorDiaV1(DateOnly.FromDateTime(Hoje.UtcDateTime), 1, 1) },
            metricas.PorDia);

        // Os dados do feedback, das avaliações mais novas para as mais antigas, paginados.
        var pagina = (await admin.GetFromJsonAsync<Pagina<FeedbackV1>>("/api/dashboard/feedback?tamanho=3"))!;
        Assert.Equal((4, 1, 3), (pagina.Total, pagina.NumeroDaPagina, pagina.Tamanho));
        Assert.Equal(new[] { ("s1", 2), ("s1", 1), ("s3", 1) }, pagina.Itens.Select(i => (i.SessaoId, i.Turno)));
        var primeiro = pagina.Itens[0];
        Assert.Equal((false, "quebrou-build-ou-teste", "glm", "opus-5", "revisao", (int?)417), (primeiro.Boa, primeiro.Motivo, primeiro.Agente, primeiro.Modelo, primeiro.Fluxo, primeiro.WorkItem));
        Assert.Equal(Hoje.AddHours(2), primeiro.Em);
        var ultima = Assert.Single((await admin.GetFromJsonAsync<Pagina<FeedbackV1>>("/api/dashboard/feedback?tamanho=3&pagina=2"))!.Itens);
        Assert.Equal(("s2", (int?)null, ""), (ultima.SessaoId, ultima.WorkItem, ultima.Fluxo));

        // O filtro de período corta: só hoje.
        var soHoje = (await admin.GetFromJsonAsync<MetricasDeFeedbackV1>($"/api/dashboard/feedback/metricas?de={Hoje:yyyy-MM-dd}&ate={Hoje:yyyy-MM-dd}"))!;
        Assert.Equal((2, 1, 1), (soHoje.Total, soHoje.Positivos, soHoje.Negativos));
    }

    [Fact]
    public async Task Mesmo_turno_avaliado_de_novo_nao_duplica_e_vale_o_mais_recente()
    {
        var maquina = await _api.MaquinaAsync("maq-1");
        var admin = await _api.AdminAsync();

        await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1", Avaliada("a1", Hoje.AddHours(1), "s1", 7, true, "revisao", "claude")));
        await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1", Avaliada("a2", Hoje.AddHours(2), "s1", 7, false, "revisao", "claude", "arquitetura")));
        // Um evento mais antigo do mesmo turno que chega atrasado, e o reenvio do primeiro: nenhum muda nada.
        await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1", Avaliada("a0", Hoje.AddMinutes(30), "s1", 7, true, "revisao", "claude")));
        var reenvio = await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1", Avaliada("a1", Hoje.AddHours(1), "s1", 7, true, "revisao", "claude")));

        Assert.Equal(new BatchResultV1(1, 0, 1, 0), await reenvio.Content.ReadFromJsonAsync<BatchResultV1>());
        var item = Assert.Single((await admin.GetFromJsonAsync<Pagina<FeedbackV1>>("/api/dashboard/feedback"))!.Itens);
        Assert.Equal((false, "arquitetura", Hoje.AddHours(2)), (item.Boa, item.Motivo, item.Em));
        var metricas = (await admin.GetFromJsonAsync<MetricasDeFeedbackV1>("/api/dashboard/feedback/metricas"))!;
        Assert.Equal((1, 0, 1), (metricas.Total, metricas.Positivos, metricas.Negativos));

        // O mesmo turno de OUTRA sessão é outra avaliação.
        await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1", Avaliada("b1", Hoje.AddHours(3), "s2", 7, true, "revisao", "claude")));
        Assert.Equal(2, (await admin.GetFromJsonAsync<MetricasDeFeedbackV1>("/api/dashboard/feedback/metricas"))!.Total);
    }

    [Fact]
    public async Task Base_vazia_devolve_o_agregado_zerado_e_nao_500()
    {
        var admin = await _api.AdminAsync();

        var resposta = await admin.GetAsync("/api/dashboard/feedback/metricas");
        var lista = await admin.GetAsync("/api/dashboard/feedback");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var metricas = (await resposta.Content.ReadFromJsonAsync<MetricasDeFeedbackV1>())!;
        Assert.Equal((0, 0, 0), (metricas.Total, metricas.Positivos, metricas.Negativos));
        Assert.Empty(metricas.PorFluxo);
        Assert.Empty(metricas.PorAgente);
        Assert.Empty(metricas.PorDia);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), metricas.Ate); // o padrão: os últimos 30 dias até hoje
        Assert.Equal(metricas.Ate.AddDays(-29), metricas.De);

        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        var pagina = (await lista.Content.ReadFromJsonAsync<Pagina<FeedbackV1>>())!;
        Assert.Equal(0, pagina.Total);
        Assert.Empty(pagina.Itens);
    }

    [Fact]
    public async Task Evento_sem_avaliacao_e_aceito_e_contado_mas_nao_entra_no_feedback()
    {
        var maquina = await _api.MaquinaAsync("maq-1");
        var admin = await _api.AdminAsync();

        // O dev.kit anterior à US #417: o EntregaAvaliada sem o campo avaliacao.
        var envio = await maquina.PostAsync("/api/telemetria/lote", Lote("maq-1",
            $$"""{"eventId":"v1","tipo":"EntregaAvaliada","sessaoId":"s1","quantidade":1,"valor":1,"detalhe":"","em":"{{Hoje:yyyy-MM-ddTHH:mm:ssZ}}"}"""));

        Assert.Equal(new BatchResultV1(1, 1, 0, 0), await envio.Content.ReadFromJsonAsync<BatchResultV1>());
        Assert.Equal(0, (await admin.GetFromJsonAsync<MetricasDeFeedbackV1>("/api/dashboard/feedback/metricas"))!.Total);
    }

    [Fact]
    public async Task Gestor_le_so_o_feedback_da_empresa_dele()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var deA = await _api.MaquinaAsync("maq-a", a.CodigoDeAdesao, "Ana");
        var deB = await _api.MaquinaAsync("maq-b", b.CodigoDeAdesao, "Bruno");
        // Um minuto à frente: o JSON corta os milissegundos, e o gestor só vê o que veio DEPOIS do consentimento.
        var depois = DateTimeOffset.UtcNow.AddMinutes(1);
        await deA.PostAsync("/api/telemetria/lote", Lote("maq-a", Avaliada("a1", depois, "s1", 1, true, "revisao", "claude")));
        await deB.PostAsync("/api/telemetria/lote", Lote("maq-b", Avaliada("b1", depois, "s1", 1, false, "revisao", "glm")));
        var gestorA = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var idDeB = (await admin.GetFromJsonAsync<MaquinaResumo[]>("/api/dashboard/maquinas"))!.Single(m => m.MaquinaId == "maq-b").Id;

        var doGestor = (await gestorA.GetFromJsonAsync<MetricasDeFeedbackV1>("/api/dashboard/feedback/metricas"))!;

        Assert.Equal((1, 1), (doGestor.Total, doGestor.Positivos));
        Assert.Equal("claude", Assert.Single(doGestor.PorAgente).Chave);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync($"/api/dashboard/feedback?maquina={idDeB}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.GetAsync($"/api/dashboard/feedback/metricas?maquina={idDeB}")).StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<MetricasDeFeedbackV1>("/api/dashboard/feedback/metricas"))!.Total);
    }

    /// <summary>
    /// O contrato V1 é público (o dev.kit e a ferramenta de análise o leem): os nomes, em camelCase, não mudam.
    /// Mudar um nome aqui é um v2.
    /// </summary>
    [Fact]
    public void Contrato_v1_do_feedback_tem_os_nomes_estaveis()
    {
        static string[] Campos(object valor)
            => JsonSerializer.SerializeToElement(valor, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                .EnumerateObject().Select(p => p.Name).ToArray();

        var avaliacao = new AvaliacaoV1(true, "", "claude", "opus-5", "revisao", 417, 3);
        Assert.Equal(new[] { "boa", "motivo", "agente", "modelo", "fluxo", "workItem", "turno" }, Campos(avaliacao));
        Assert.Contains("avaliacao", Campos(new TelemetryEventV1("e", TiposDeEvento.EntregaAvaliada, "s", 1, 1, "", DateTimeOffset.UnixEpoch, Avaliacao: avaliacao)));

        var feedback = new FeedbackV1(DateTimeOffset.UnixEpoch, 1, "s", 3, true, "", "claude", "opus-5", "revisao", 417);
        Assert.Equal(new[] { "em", "maquina", "sessaoId", "turno", "boa", "motivo", "agente", "modelo", "fluxo", "workItem" }, Campos(feedback));
        Assert.Equal(new[] { "itens", "total", "numeroDaPagina", "tamanho" }, Campos(new Pagina<FeedbackV1>([feedback], 1, 1, 50)));

        var metricas = new MetricasDeFeedbackV1(DateOnly.MinValue, DateOnly.MinValue, 0, 0, 0, [], [], []);
        Assert.Equal(new[] { "de", "ate", "total", "positivos", "negativos", "porFluxo", "porAgente", "porDia" }, Campos(metricas));
        Assert.Equal(new[] { "chave", "total", "positivos", "negativos" }, Campos(new FeedbackPorChaveV1("x", 0, 0, 0)));
        Assert.Equal(new[] { "dia", "positivos", "negativos" }, Campos(new FeedbackPorDiaV1(DateOnly.MinValue, 0, 0)));
        Assert.Equal(("(sem fluxo)", "(sem agente)"), (ChavesDoFeedback.SemFluxo, ChavesDoFeedback.SemAgente));
    }

    [Fact]
    public void O_json_do_dev_kit_com_avaliacao_e_lido_no_contrato()
    {
        var evento = JsonSerializer.Deserialize<TelemetryEventV1>(
            Avaliada("a1", Hoje, "s1", 3, false, "revisao", "glm", "seguranca"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal(new AvaliacaoV1(false, "seguranca", "glm", "opus-5", "revisao", 417, 3), evento.Avaliacao);
    }
}
