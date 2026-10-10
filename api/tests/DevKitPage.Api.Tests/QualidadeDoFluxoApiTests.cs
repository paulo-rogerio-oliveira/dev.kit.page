using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A telemetria do impasse e do árbitro (US #405, #411) de ponta a ponta: o lote com os tipos novos é
/// GRAVADO (não ignorado), e <c>/api/dashboard/qualidade</c> devolve os cartões Impasses e Árbitro, com o
/// escopo do gestor (só a empresa dele) e o JSON exato que o dev.kit manda.
/// </summary>
public sealed class QualidadeDoFluxoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static TelemetryEventV1 Evento(string id, string tipo, long valor, string detalhe)
        => new(id, tipo, "s1", 1, valor, detalhe, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Lote_com_os_tipos_novos_e_gravado_e_a_qualidade_traz_impasses_e_arbitro_no_escopo_do_gestor()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var gestorA = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var deA = await _api.MaquinaAsync("maq-a", a.CodigoDeAdesao, "Ana");
        var anonima = await _api.MaquinaAsync("maq-x");

        // O JSON como o dev.kit o monta (camelCase, sem campo novo no v1).
        var lote = await deA.PostAsync("/api/telemetria/lote", new StringContent($$"""
            {"versao":"v1","maquinaId":"maq-a","versaoDevKit":"137","eventos":[
              {"eventId":"i1","tipo":"ImpasseDetectado","sessaoId":"s","quantidade":1,"valor":30,"detalhe":"mensagem-parada","em":"{{DateTimeOffset.UtcNow:O}}"},
              {"eventId":"i2","tipo":"ImpasseResolvido","sessaoId":"s","quantidade":1,"valor":12,"detalhe":"arbitro-reagiu","em":"{{DateTimeOffset.UtcNow:O}}"},
              {"eventId":"r1","tipo":"ArbitroAgiu","sessaoId":"s","quantidade":1,"valor":1,"detalhe":"cobrou|Arquivos alterados no turno","em":"{{DateTimeOffset.UtcNow:O}}"},
              {"eventId":"r2","tipo":"ArbitroAgiu","sessaoId":"s","quantidade":1,"valor":2,"detalhe":"corrigido|Arquivos alterados no turno","em":"{{DateTimeOffset.UtcNow:O}}"},
              {"eventId":"r3","tipo":"ArbitroAgiu","sessaoId":"s","quantidade":1,"valor":3,"detalhe":"escalou-ao-dev","em":"{{DateTimeOffset.UtcNow:O}}"}
            ]}
            """, System.Text.Encoding.UTF8, "application/json"));
        (await anonima.PostAsJsonAsync("/api/telemetria/lote", new TelemetryBatchV1("v1", "maq-x", "137", [
            Evento("x1", TiposDeEvento.ImpasseDetectado, 90, OrigensDoImpasse.ObjetivoParado),
            Evento("x2", TiposDeEvento.ArbitroAgiu, 1, "cobrou|Idioma e fluxo"),
        ]))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Accepted, lote.StatusCode);
        Assert.Equal(new BatchResultV1(5, 5, 0, 0), await lote.Content.ReadFromJsonAsync<BatchResultV1>()); // nenhum ignorado

        var doGestor = (await gestorA.GetFromJsonAsync<QualidadeResposta>("/api/dashboard/qualidade"))!;
        var doAdmin = (await admin.GetFromJsonAsync<QualidadeResposta>("/api/dashboard/qualidade"))!;

        Assert.Equal((1L, 30.0, 1L, 12.0), (doGestor.Impasses!.Detectados, doGestor.Impasses.MinutosParadoNaDeteccao, doGestor.Impasses.Destravados, doGestor.Impasses.MinutosAteDestravar));
        Assert.Equal(new ContagemPorRecorte("arbitro-reagiu", 1), Assert.Single(doGestor.Impasses.ComoDestravaram));
        Assert.Equal((1L, 1L, 1.0, 1L), (doGestor.Arbitro!.Cobrancas, doGestor.Arbitro.Corrigidas, doGestor.Arbitro.TaxaDeCorrecao, doGestor.Arbitro.EscaladasAoDev));
        Assert.Equal(new ContagemPorRecorte("Arquivos alterados no turno", 1), Assert.Single(doGestor.Arbitro.CobrancasPorRegra));
        // O admin vê também a anônima: dois impasses e duas cobranças (uma de cada regra).
        Assert.Equal((2L, 2L, 0.5), (doAdmin.Impasses!.Detectados, doAdmin.Arbitro!.Cobrancas, doAdmin.Arbitro.TaxaDeCorrecao));
        Assert.Equal(2, doAdmin.Arbitro.CobrancasPorRegra.Count);
    }

    [Fact]
    public async Task Sem_evento_os_cartoes_vem_zerados_com_as_medias_nulas()
    {
        var admin = await _api.AdminAsync();

        var qualidade = (await admin.GetFromJsonAsync<QualidadeResposta>("/api/dashboard/qualidade"))!;

        Assert.Equal(0, qualidade.Impasses!.Detectados);
        Assert.Null(qualidade.Impasses.MinutosParadoNaDeteccao);
        Assert.Null(qualidade.Arbitro!.TaxaDeCorrecao);
        Assert.Empty(qualidade.Arbitro.PorAcao);
    }
}
