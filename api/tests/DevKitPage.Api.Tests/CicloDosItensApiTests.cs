using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class CicloDosItensApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static EnvioDeProjetoV1 Envio() => new("ciclo", "Ciclo humano", "empresa", "", ["repo-a", "repo-b"],
        "feature/dev/projeto-ciclo", false, "Pedido", ["design/projeto/tela/v003/hash-fixado"], true, 2, "develop", "aprovado", null,
        [new(1, "A", "Referência projeto/tela/v003/hash", "", [], ["api"], [], 1, 0, "planejado"),
            new(2, "B", "", "", [], ["cli"], [1], 2, 0, "planejado"),
            new(3, "C", "", "", [], ["servico"], [2], 3, 0, "planejado")]);

    [Fact]
    public async Task Dois_hosts_mantem_reservas_ate_recibos_autenticados_sem_apagar_resultados()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Parada distribuída");
        var (dev, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "dev@parada.test");
        await _api.MaquinaAsync("host-a");
        await _api.MaquinaAsync("host-b");
        dev.DefaultRequestHeaders.Add(CicloDosItensV1.Cabecalho, "1");
        var a = new ReservaDeEscritaDoItemV1("host-a", "instancia-a", "sessao-a", "tentativa-a");
        var b = new ReservaDeEscritaDoItemV1("host-b", "instancia-b", "sessao-b", "tentativa-b");
        var envio = Envio();
        envio = envio with { Itens = envio.Itens.Select(i => i.Numero == 1 ? i with
            { Situacao = "rodando", ReservasDeEscrita = new([a, b], []) } : i).ToArray() };
        var projeto = (await (await dev.PutAsJsonAsync("/api/projetos", envio)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";
        var pedido = new PedidoDeDecisaoDoItemV1(Guid.NewGuid().ToString(), CicloDosItensV1.Cancelar, 1, true, true, "Dev retirou", "diálogo humano");
        var decidida = await dev.PostAsJsonAsync(rota + "/itens/1/decisao", pedido);
        Assert.Equal(HttpStatusCode.OK, decidida.StatusCode);
        var item = (await decidida.Content.ReadFromJsonAsync<ImpactoDaDecisaoDoItemV1>())!.Projeto.Itens[0];
        Assert.True(CicloDosItensV1.ReservaPendente(item));
        var recibo = new ParadaDeEscritaDoItemV1(pedido.Pedido, a, 17, DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, (await dev.PostAsJsonAsync(rota + "/itens/1/parada", recibo)).StatusCode);
        dev.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["host-b"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PostAsJsonAsync(rota + "/itens/1/parada", recibo)).StatusCode);
        dev.DefaultRequestHeaders.Remove(ContratoV1.CabecalhoDaChave);
        dev.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["host-a"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PostAsJsonAsync(rota + "/itens/1/parada",
            recibo with { Reserva = a with { Tentativa = "outra" } })).StatusCode);
        var primeira = await dev.PostAsJsonAsync(rota + "/itens/1/parada", recibo);
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        var comPrimeira = (await primeira.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.True(CicloDosItensV1.ReservaPendente(comPrimeira.Itens[0]));
        Assert.Single(comPrimeira.Itens[0].ReservasDeEscrita!.Paradas);
        var repetida = await dev.PostAsJsonAsync(rota + "/itens/1/parada", recibo);
        Assert.Equal(comPrimeira.Versao, (await repetida.Content.ReadFromJsonAsync<ProjetoV1>())!.Versao);
        // Um snapshot ordinário não pode produzir o recibo da outra máquina.
        var forjado = Envio() with { Versao = comPrimeira.Versao, Itens = comPrimeira.Itens.Select(i => i.Numero == 1 ? i with
            { ReservasDeEscrita = new([a, b], [recibo, recibo with { Reserva = b }]) } : i).ToArray() };
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PutAsJsonAsync(rota, forjado)).StatusCode);
        dev.DefaultRequestHeaders.Remove(ContratoV1.CabecalhoDaChave);
        dev.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["host-b"]);
        var segunda = await dev.PostAsJsonAsync(rota + "/itens/1/parada", recibo with { Reserva = b, Recibo = 18 });
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var parada = (await segunda.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.False(CicloDosItensV1.ReservaPendente(parada.Itens[0]));
        Assert.Equal(item.DecisaoHumana, parada.Itens[0].DecisaoHumana);
        Assert.Equal(ProjetosV1.ItemCancelado, parada.Itens[0].Situacao);
        var atrasado = await dev.PutAsJsonAsync(rota, Envio() with { Versao = parada.Versao });
        Assert.Equal(HttpStatusCode.OK, atrasado.StatusCode);
        Assert.Equal(2, (await atrasado.Content.ReadFromJsonAsync<ProjetoV1>())!.Itens[0].ReservasDeEscrita!.Paradas.Length);
    }

    [Theory]
    [InlineData(CicloDosItensV1.Concluir, ProjetosV1.ItemConcluidoManualmente)]
    [InlineData(CicloDosItensV1.Cancelar, ProjetosV1.ItemCancelado)]
    public async Task Simula_confirma_preserva_recibo_e_recusa_cliente_anterior(string acao, string estado)
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Ciclo humano");
        var (dev, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "dev@ciclo.test");
        var criado = await dev.PutAsJsonAsync("/api/projetos", Envio());
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        var projeto = (await criado.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";
        var pedido = new PedidoDeDecisaoDoItemV1(Guid.NewGuid().ToString(), acao, projeto.Versao);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await dev.PostAsJsonAsync(rota + "/itens/1/decisao", pedido)).StatusCode);
        dev.DefaultRequestHeaders.Add(CicloDosItensV1.Cabecalho, "1");
        var simulada = await dev.PostAsJsonAsync(rota + "/itens/1/decisao", pedido);
        Assert.Equal(HttpStatusCode.OK, simulada.StatusCode);
        var impacto = (await simulada.Content.ReadFromJsonAsync<ImpactoDaDecisaoDoItemV1>())!;
        Assert.True(impacto.Simulacao);
        Assert.Equal(new[] { 2 }, impacto.Dependentes);
        Assert.Empty(impacto.GrafoEfetivo[0].Dependencias);
        Assert.Equal(1, impacto.GrafoEfetivo[0].Onda);
        Assert.Equal(2, impacto.GrafoEfetivo[1].Onda);
        Assert.Equal(new[] { 2 }, impacto.GrafoEfetivo[1].Dependencias);
        Assert.Equal(1, (await dev.GetFromJsonAsync<ProjetoV1>(rota))!.Versao);
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PostAsJsonAsync(rota + "/itens/1/decisao", pedido with { Aplicar = true })).StatusCode);
        pedido = pedido with { Aplicar = true, Confirmar = true, Motivo = "Retirar o restante", Confirmacao = "Dev confirmou no diálogo" };
        var aplicada = await dev.PostAsJsonAsync(rota + "/itens/1/decisao", pedido);
        Assert.Equal(HttpStatusCode.OK, aplicada.StatusCode);
        var depois = (await aplicada.Content.ReadFromJsonAsync<ImpactoDaDecisaoDoItemV1>())!.Projeto;
        Assert.Equal(2, depois.Versao);
        Assert.Equal(estado, depois.Itens[0].Situacao);
        var recibo = depois.Itens[0].DecisaoHumana;
        Assert.NotNull(recibo);
        Assert.Contains("hash-fixado", recibo.Snapshot);
        Assert.Equal(projeto.Anexos, depois.Anexos);
        Assert.Equal(new[] { 1 }, depois.Itens[1].Dependencias);
        var replay = await dev.PostAsJsonAsync(rota + "/itens/1/decisao", pedido);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(2, (await replay.Content.ReadFromJsonAsync<ImpactoDaDecisaoDoItemV1>())!.Projeto.Versao);
        Assert.Equal(HttpStatusCode.Conflict, (await dev.PostAsJsonAsync(rota + "/itens/1/decisao", pedido with { Motivo = "Outro" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await dev.PostAsJsonAsync(rota + "/itens/2/decisao", pedido)).StatusCode);
        // Snapshot atrasado com versão atual não restaura o estado do item.
        var atrasado = await dev.PutAsJsonAsync(rota, Envio() with { Versao = 2 });
        Assert.Equal(HttpStatusCode.OK, atrasado.StatusCode);
        Assert.Equal(recibo, (await atrasado.Content.ReadFromJsonAsync<ProjetoV1>())!.Itens[0].DecisaoHumana);
        dev.DefaultRequestHeaders.Remove(CicloDosItensV1.Cabecalho);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await dev.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await dev.PutAsJsonAsync(rota, Envio() with { Versao = 3 })).StatusCode);
        dev.DefaultRequestHeaders.Add(CicloDosItensV1.Cabecalho, "1");
        Assert.Equal(3, (await dev.GetFromJsonAsync<ProjetoV1>(rota))!.Versao);
    }

    [Fact]
    public async Task Leitor_recusado_editor_decide_e_versao_antiga_nao_grava()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Ciclo");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@ciclo.test");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@ciclo.test");
        var p = (await (await ana.PutAsJsonAsync("/api/projetos", Envio())).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{p.Id}";
        var acesso = await ana.PostAsJsonAsync(rota + "/acessos", new ConcessaoDeAcessoV1("bia@ciclo.test", "leitor"));
        Assert.Equal(HttpStatusCode.OK, acesso.StatusCode);
        var biaId = (await acesso.Content.ReadFromJsonAsync<AcessosDoProjetoV1>())!.Acessos.Single().Usuario.Id;
        bia.DefaultRequestHeaders.Add(CicloDosItensV1.Cabecalho, "1");
        var pedido = new PedidoDeDecisaoDoItemV1(Guid.NewGuid().ToString(), CicloDosItensV1.Cancelar, 1, true, true, "Retirar", "Bia confirmou");
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PostAsJsonAsync(rota + "/itens/1/decisao", pedido)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync(rota + $"/acessos/{biaId}", new ConcessaoDeAcessoV1(null, "editor"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await bia.PostAsJsonAsync(rota + "/itens/1/decisao", pedido with { VersaoEsperada = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bia.PostAsJsonAsync(rota + "/itens/1/decisao", pedido)).StatusCode);
    }
}
