using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class ImportacaoDasDecisoesApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    [Theory]
    [InlineData(CicloDosItensV1.Concluir, ProjetosV1.ItemConcluidoManualmente)]
    [InlineData(CicloDosItensV1.Cancelar, ProjetosV1.ItemCancelado)]
    public async Task Primeiro_compartilhamento_preserva_recibo_local_proveniencia_reservas_e_replay(string acao, string estado)
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Importação de decisões locais");
        var (dev, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "dev@importacao.test");
        var (leitor, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "leitor@importacao.test");
        var reserva = new ReservaDeEscritaDoItemV1("host-local", "instancia", "sessao", "tentativa");
        var anexos = new[] { "design/projeto/tela/v003/hash-fixado" };
        var snapshot = JsonSerializer.Serialize(new { Anexos = anexos, Item = new { Numero = 1, Situacao = "rodando",
            Tentativas = new[] { "tentativa" }, Entregas = new[] { "repo-a:commit-enviado" },
            Confirmacoes = new[] { "repo-b:commit-integrado" } } });
        var recibo = new DecisaoHumanaDoItemV1(Guid.NewGuid().ToString(), acao, "dominio/dev-local",
            "Dev confirmou no terminal", "Retirar restante", DateTimeOffset.UtcNow.AddMinutes(-1), new string('a', 64),
            "rodando", snapshot, true);
        var item = new ItemDoProjetoV1(1, "A", "", "", [], ["api"], [], 1, 0, estado)
            { DecisaoHumana = recibo, ReservasDeEscrita = new([reserva], []) };
        var envio = new EnvioDeProjetoV1("importacao", "Projeto local", "empresa", "", ["repo-a", "repo-b"],
            "feature/dev/importacao", false, "Pedido", anexos, true, 2, "develop", "aprovado", null,
            [item, new(2, "B", "", "", [], ["cli"], [1], 2, 0, "planejado")]);
        const string importar = "/api/projetos/importar-decisoes";
        Assert.Equal(HttpStatusCode.UpgradeRequired, (await dev.PostAsJsonAsync(importar, envio)).StatusCode);
        dev.DefaultRequestHeaders.Add(CicloDosItensV1.Cabecalho, "1");
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PutAsJsonAsync("/api/projetos", envio)).StatusCode);
        foreach (var invalido in new[] { recibo with { Pedido = "inválido" }, recibo with { Motivo = "" },
            recibo with { Confirmacao = "" }, recibo with { Autor = "" }, recibo with { Geracao = 2 },
            recibo with { VersaoEsperada = "1" }, recibo with { Snapshot = "{}" },
            recibo with { Snapshot = snapshot.Replace("\"Numero\":1", "\"Numero\":99") },
            recibo with { Acao = "integrar" }, recibo with { Em = DateTimeOffset.UtcNow.AddDays(1) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await dev.PostAsJsonAsync(importar, envio with
                { Itens = [item with { DecisaoHumana = invalido }, envio.Itens[1]] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PostAsJsonAsync(importar, envio with { Versao = 3 })).StatusCode);
        var forjado = item with { ReservasDeEscrita = new([reserva],
            [new(recibo.Pedido, reserva, 1, DateTimeOffset.UtcNow)]) };
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PostAsJsonAsync(importar, envio with
            { Itens = [forjado, envio.Itens[1]] })).StatusCode);
        Assert.Empty((await dev.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!);

        var resposta = await dev.PostAsJsonAsync(importar, envio);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var criado = (await resposta.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(1, criado.Versao);
        Assert.Equal(HttpStatusCode.BadRequest, (await dev.PutAsJsonAsync($"/api/projetos/{criado.Id}", envio with
            { Versao = 1, Itens = [item with { ImportacaoDaDecisao = new("forjado", DateTimeOffset.UtcNow, "hash") }, envio.Itens[1]] })).StatusCode);
        var salvo = criado.Itens[0];
        Assert.Equal(recibo, salvo.DecisaoHumana);
        Assert.Equal(reserva, Assert.Single(salvo.ReservasDeEscrita!.Reservas));
        Assert.True(CicloDosItensV1.ReservaPendente(salvo));
        Assert.NotNull(salvo.ImportacaoDaDecisao);
        Assert.NotEqual(recibo.Autor, salvo.ImportacaoDaDecisao.AutorAutenticado);
        Assert.Equal(64, salvo.ImportacaoDaDecisao.HashDoEnvio.Length);
        var rota = $"/api/projetos/{criado.Id}";
        // Outra conexão e outro escopo EF leem o SQLite real, sem round-trip construído à mão.
        using var outraMaquina = _api.CreateClient();
        outraMaquina.DefaultRequestHeaders.Authorization = dev.DefaultRequestHeaders.Authorization;
        outraMaquina.DefaultRequestHeaders.Add(CicloDosItensV1.Cabecalho, "1");
        var lido = (await outraMaquina.GetFromJsonAsync<ProjetoV1>(rota))!;
        Assert.Equal(salvo.DecisaoHumana, lido.Itens[0].DecisaoHumana);
        Assert.Equal(salvo.ImportacaoDaDecisao, lido.Itens[0].ImportacaoDaDecisao);
        Assert.Equal(anexos, lido.Anexos);
        Assert.Equal(new[] { 1 }, lido.Itens[1].Dependencias);
        var replay = await outraMaquina.PostAsJsonAsync(importar, envio);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(criado.Versao, (await replay.Content.ReadFromJsonAsync<ProjetoV1>())!.Versao);
        Assert.Equal(HttpStatusCode.Conflict, (await dev.PostAsJsonAsync(importar, envio with
            { Itens = [item with { DecisaoHumana = recibo with { Motivo = "Outro motivo" } }, envio.Itens[1]] })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await dev.PostAsJsonAsync(rota + "/acessos",
            new ConcessaoDeAcessoV1("leitor@importacao.test", "leitor"))).StatusCode);
        leitor.DefaultRequestHeaders.Add(CicloDosItensV1.Cabecalho, "1");
        Assert.Equal(HttpStatusCode.Forbidden, (await leitor.PostAsJsonAsync(importar, envio)).StatusCode);
        var atrasado = envio with { Versao = criado.Versao, Itens = [item with { Situacao = "integrado", DecisaoHumana = null }, envio.Itens[1]] };
        var atualizado = await dev.PutAsJsonAsync(rota, atrasado);
        Assert.Equal(HttpStatusCode.OK, atualizado.StatusCode);
        var preservado = (await atualizado.Content.ReadFromJsonAsync<ProjetoV1>())!.Itens[0];
        Assert.Equal(salvo.DecisaoHumana, preservado.DecisaoHumana);
        Assert.Equal(salvo.ImportacaoDaDecisao, preservado.ImportacaoDaDecisao);
        Assert.Equal(estado, preservado.Situacao);
        Assert.Equal(reserva, Assert.Single(preservado.ReservasDeEscrita!.Reservas));
    }
}
