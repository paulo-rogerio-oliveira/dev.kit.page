using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

/// <summary>
/// (boards-estados, item 5) Aprovar e Disparar separados no pedido remoto: cada ação só na sua situação, aprovar sem item,
/// o leitor recusado antes da fila e a prontidão do site pela MESMA conferência da fila, para a ação pedida.
/// </summary>
public sealed partial class ConversaApiTests
{
    /// <summary>O pedido remoto e, na recusa, o <c>codigo</c> e o <c>detail</c> do problema (lido do JSON, sem depender do escape).</summary>
    private static async Task<(HttpResponseMessage Resposta, string? Codigo, string Corpo)> PedirAsync(HttpClient cliente, ComandoNovoV1 pedido)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/remoto/comandos", pedido);
        if (resposta.IsSuccessStatusCode) return (resposta, null, string.Empty);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        var codigo = json.RootElement.TryGetProperty("codigo", out var c) ? c.GetString() : null;
        var detalhe = json.RootElement.TryGetProperty("detail", out var d) ? d.GetString() ?? string.Empty : string.Empty;
        return (resposta, codigo, detalhe);
    }

    private void NadaFoiGravadoNemEnfileirado()
    {
        Assert.Empty(_api.Fila.Enviadas);
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
        Assert.Empty(db.ComandosRemotos);
        Assert.Empty(db.EventosDaConversa);
    }

    [Theory]
    [InlineData("aprovar", "aprovado", "não está aguardando aprovação")]
    [InlineData("aprovar", "rascunho", "não está aguardando aprovação")]
    [InlineData("aprovar", "pausado", "não está aguardando aprovação")]
    [InlineData("disparar", "aguardando", "não está aprovado")]
    [InlineData("disparar", "rascunho", "não está aprovado")]
    [InlineData("retomar", "aguardando", "não está pausado")]
    public async Task Cada_acao_remota_so_vale_na_sua_situacao_e_a_recusa_nao_grava_nem_enfileira(string acao, string situacao, string motivo)
    {
        var (_, dev) = await DevComFilaAsync();
        (await dev.PutAsJsonAsync("/api/projetos", ProjetoParaDisparo(situacao))).EnsureSuccessStatusCode();
        await DefinirDisparoAsync(dev);

        var (resposta, codigo, corpo) = await PedirAsync(dev, PedidoDeDisparo(acao));

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("matrix-pendente", codigo);
        Assert.Contains(motivo, corpo, StringComparison.Ordinal);
        NadaFoiGravadoNemEnfileirado();
    }

    [Theory]
    [InlineData("aprovar", 1)]
    [InlineData("retomar", 1)]
    [InlineData("disparar", 0)]
    public async Task Aprovar_e_retomar_com_item_ou_disparo_com_item_invalido_sao_recusados_antes_da_fila(string acao, int item)
    {
        var (_, dev) = await DevComFilaAsync();
        (await dev.PutAsJsonAsync("/api/projetos", ProjetoParaDisparo(SituacaoDaAcao(acao)))).EnsureSuccessStatusCode();
        await DefinirDisparoAsync(dev);

        var (resposta, _, corpo) = await PedirAsync(dev, PedidoDeDisparo(acao, item));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("Item inválido para a ação.", corpo, StringComparison.Ordinal);
        NadaFoiGravadoNemEnfileirado();
    }

    [Fact]
    public async Task Leitor_nao_aprova_nem_dispara_e_o_dono_aprova_o_mesmo_plano()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Boards estados");
        var (_, ana) = await DevComFilaAsync("ana", admin, empresa.Id);
        var (_, bia) = await DevComFilaAsync("bia", admin, empresa.Id);
        var projeto = (await (await ana.PutAsJsonAsync("/api/projetos", ProjetoParaDisparo("aguardando") with { Nivel = "empresa" }))
            .Content.ReadFromJsonAsync<ProjetoV1>())!;
        await DefinirDisparoAsync(ana);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/api/projetos/{projeto.Id}/acessos",
            new ConcessaoDeAcessoV1("bia", "leitor"))).StatusCode);

        foreach (var acao in new[] { "aprovar", "disparar" })
        {
            var (resposta, codigo, corpo) = await PedirAsync(bia, PedidoDeDisparo(acao, id: $"bia-{acao}"));
            Assert.Equal((HttpStatusCode.Conflict, "matrix-pendente"), (resposta.StatusCode, codigo));
            Assert.Contains(acao == "aprovar" ? "Aprovação exige editor ou dono do projeto." : "Disparo exige editor ou dono do projeto.",
                corpo, StringComparison.Ordinal);
        }
        var prontidao = await bia.GetFromJsonAsync<JsonElement>($"/api/projetos/{projeto.Id}/prontidao-disparo?acao=aprovar");
        Assert.Contains("exige editor ou dono", prontidao.GetProperty("pendencia").GetString() ?? "", StringComparison.Ordinal);
        NadaFoiGravadoNemEnfileirado();

        var (aprovado, _, _) = await PedirAsync(ana, PedidoDeDisparo("aprovar", id: "ana-aprovar"));
        Assert.Equal(HttpStatusCode.Accepted, aprovado.StatusCode);
        var mensagem = JsonSerializer.Deserialize<MensagemDaFilaV1>(Assert.Single(_api.Fila.Enviadas).Mensagem, Json)!;
        Assert.Equal("ana-aprovar", mensagem.Id);
        // Enfileirar não aprova: a situação gravada só muda quando o host aprova e sincroniza.
        Assert.Equal("aguardando", (await ana.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{projeto.Id}"))!.Situacao);
    }

    [Fact]
    public async Task Prontidao_confere_a_acao_pedida_e_sem_acao_e_o_disparo_de_antes()
    {
        var (_, dev) = await DevComFilaAsync();
        var projeto = (await (await dev.PutAsJsonAsync("/api/projetos", ProjetoParaDisparo("aguardando")))
            .Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}/prontidao-disparo";

        var semMatriz = await dev.GetFromJsonAsync<JsonElement>(rota + "?acao=aprovar");
        Assert.Contains("Projeto sem matriz definida.", semMatriz.GetProperty("pendencia").GetString() ?? "", StringComparison.Ordinal);

        await DefinirDisparoAsync(dev);
        var aprovar = await dev.GetFromJsonAsync<JsonElement>(rota + "?acao=aprovar");
        Assert.Equal("aprovar", aprovar.GetProperty("acao").GetString());
        Assert.Equal(JsonValueKind.Null, aprovar.GetProperty("pendencia").ValueKind);

        foreach (var consulta in new[] { "?acao=disparar", "" })
        {
            var disparar = await dev.GetFromJsonAsync<JsonElement>(rota + consulta);
            Assert.Equal("disparar", disparar.GetProperty("acao").GetString());
            Assert.Contains("não está aprovado", disparar.GetProperty("pendencia").GetString() ?? "", StringComparison.Ordinal);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await dev.GetAsync(rota + "?acao=aprovar-e-disparar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dev.GetAsync("/api/projetos/9223372036854775807/prontidao-disparo?acao=aprovar")).StatusCode);
        using var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(rota + "?acao=aprovar")).StatusCode);
        // A consulta não grava nada: nem comando, nem evento, nem fila.
        NadaFoiGravadoNemEnfileirado();
    }
}
