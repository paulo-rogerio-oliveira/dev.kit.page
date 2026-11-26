using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed partial class ArtefatosApiTests
{
    [Fact]
    public async Task Lista_pedidos_com_todas_as_origens_limite_ACL_legado_e_lista_branca_no_HTTP()
    {
        var (ana, bia, caio, projeto) = await CenarioAsync();
        var rota = $"/api/projetos/{projeto}/artefatos/pedidos";
        var inicio = DateTimeOffset.UtcNow;
        for (var i = 0; i < 7; i++)
        {
            var id = Guid.NewGuid().ToString("N");
            var resultados = Enumerable.Range(1, 3).Select(n => new ResultadoDoPedidoDeArtefatosV1(i * 3 + n, projeto,
                "pacote", $"https://github.com/acme/repo-{n}", Sha, "falhou", @"C:\SENTINELA_LOCAL\zip", null,
                "", 0, "", "SDK ausente", inicio.AddMinutes(i + 1),
                Ciclo: new("falhou", "compilacao", Branch: "feature/demo", PedidoId: id), Produtor: new("pacote-x", "skill"))).ToArray();
            var json = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new PedidoDeArtefatosV1(id, projeto,
                inicio.AddMinutes(i + 1), resultados), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)))!;
            foreach (var a in json["resultados"]!.AsArray())
            {
                a!["conteudo"] = "SENTINELA_CONTEUDO";
                a["produtor"]!["comando"] = "SENTINELA_COMANDO";
                a["produtor"]!["argumentos"] = "SENTINELA_ARGUMENTOS";
                a["ciclo"]!["caminhoLocal"] = @"C:\SENTINELA_LOCAL\zip";
                a["ciclo"]!["skillDoPacote"] = new JsonObject { ["conteudo"] = "SENTINELA_SKILL" };
            }
            Assert.Equal(HttpStatusCode.OK, (await ana.PutAsync(rota + "/" + id,
                new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json"))).StatusCode);
        }
        var antigo = (await (await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip()))).Content.ReadFromJsonAsync<ArtefatoV1>())!;
        var resposta = await bia.GetAsync(rota + "?limite=50");
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var texto = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SENTINELA", texto, StringComparison.Ordinal);
        foreach (var proibido in new[] { "conteudo", "comando", "argumentos", "caminhoLocal", "skillDoPacote" })
            Assert.DoesNotContain('"' + proibido + '"', texto, StringComparison.OrdinalIgnoreCase);
        var lista = (await resposta.Content.ReadFromJsonAsync<PedidoDeArtefatosV1[]>())!;
        Assert.Equal(8, lista.Length);
        Assert.Equal(lista.OrderByDescending(p => p.CriadoEm).Select(p => p.Id), lista.Select(p => p.Id));
        Assert.All(lista.Take(7), p => { Assert.Equal(3, p.Resultados.Count); Assert.Equal("falhou", p.Estado); });
        Assert.Equal(antigo.Id.ToString(), lista[^1].Id);
        Assert.StartsWith("http", Assert.Single(lista[^1].Resultados).Link);
        var limitado = (await bia.GetFromJsonAsync<PedidoDeArtefatosV1[]>(rota + "?limite=2"))!;
        Assert.Equal(2, limitado.Length); Assert.All(limitado, p => Assert.Equal(3, p.Resultados.Count));
        Assert.Equal(HttpStatusCode.BadRequest, (await bia.GetAsync(rota + "?limite=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync(rota)).StatusCode);
        // O leitor consulta; permanece proibido registrar pedidos.
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PutAsJsonAsync(rota + "/" + lista[0].Id, lista[0])).StatusCode);
        Assert.Equal(lista[0].Id, (await bia.GetFromJsonAsync<PedidoDeArtefatosV1>(rota + "/" + lista[0].Id))!.Id);
    }
}
