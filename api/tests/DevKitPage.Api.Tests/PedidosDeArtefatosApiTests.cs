using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DevKitPage.Contracts.V1;
using DevKitPage.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

public sealed partial class ArtefatosApiTests
{
    [Fact]
    public async Task Pedido_agrupa_resultados_conserva_download_ACL_ids_e_retry_apos_falha_parcial()
    {
        var (ana, bia, caio, projeto) = await CenarioAsync();
        var upload = (await (await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip()))).Content.ReadFromJsonAsync<ArtefatoV1>())!;
        var id = Guid.NewGuid().ToString("N");
        var resultado = new ResultadoDoPedidoDeArtefatosV1(101, projeto, "pacote", upload.Repositorio, Sha, "pronto",
            @"C:\local\nao-publicar.zip", upload.ExpiraEm, upload.Hash, upload.Tamanho, "", "Pacote publicado", DateTimeOffset.UtcNow,
            IdRemoto: upload.Id, Ciclo: new("publicado", "concluido", "recibo-1", 1, "feature/dev/projeto-controle-v4", "pacote", id));
        var falha = resultado with { Id = 102, Tipo = "preview", Repositorio = "https://github.com/acme/portal-web", Sha = new string('a', 40),
            Situacao = "falhou", IdRemoto = 0, ValidoAte = null, Detalhe = "Build interrompido", Ciclo = new("falhou", "compilacao", "recibo-2", 2) };
        var semProdutor = falha with { Id = 103, Repositorio = "https://github.com/acme/lib", Tipo = "", Sha = new string('b', 40),
            Situacao = "nao-aplicavel", Detalhe = "Biblioteca sem produtor suportado.", Ciclo = new("nao-aplicavel", "deteccao") };
        var pedido = new PedidoDeArtefatosV1(id, projeto, DateTimeOffset.UtcNow, [resultado, falha, semProdutor]);
        // TestServer não usa rede/TLS; a URI HTTPS exercita o esquema público recebido pela API.
        var rota = $"https://localhost/api/projetos/{projeto}/artefatos/pedidos/{id}";

        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PutAsJsonAsync(rota, pedido)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caio.PutAsJsonAsync(rota, pedido)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync(rota, pedido)).StatusCode);
        var lido = (await bia.GetFromJsonAsync<PedidoDeArtefatosV1>(rota))!;
        Assert.Equal("parcial", lido.Estado);
        Assert.Equal(pedido.Resultados.Select(a => (a.Id, a.Sha, a.Repositorio, a.Tipo)), lido.Resultados.Select(a => (a.Id, a.Sha, a.Repositorio, a.Tipo)));
        Assert.Equal("nao-aplicavel", lido.Resultados[2].Estado);
        Assert.Equal(semProdutor.Detalhe, lido.Resultados[2].Detalhe);
        Assert.StartsWith("https://", lido.Resultados[0].Link);
        Assert.Empty(lido.Resultados[1].Link);
        Assert.Empty(lido.Resultados[2].Link);
        Assert.Equal(upload.Hash, Convert.ToHexStringLower(SHA256.HashData(await _api.CreateClient().GetByteArrayAsync(lido.Resultados[0].Link))));
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync(rota, pedido)).StatusCode);

        var retomado = pedido with { Resultados = [resultado, falha with { Situacao = "gerando", Ciclo = falha.Ciclo! with { Estado = "enfileirado", ComandoId = 3 } }, semProdutor] };
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync(rota, retomado)).StatusCode);
        var estado = (await ana.GetFromJsonAsync<PedidoDeArtefatosV1>(rota))!;
        Assert.Equal("em-andamento", estado.Estado);
        Assert.Equal(upload.Id, estado.Resultados[0].IdRemoto);
        // Snapshot antigo não desfaz a retomada (comando 3) nem duplica o pacote publicado.
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync(rota, pedido)).StatusCode);
        Assert.Equal("enfileirado", (await ana.GetFromJsonAsync<PedidoDeArtefatosV1>(rota))!.Resultados[1].Estado);
        using var escopo = _api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
        Assert.Single(await db.PedidosDeArtefatos.ToArrayAsync());
        Assert.Single(await db.ArtefatosDosProjetos.ToArrayAsync());
        Assert.DoesNotContain("C:\\local", (await db.PedidosDeArtefatos.SingleAsync()).Conteudo, StringComparison.Ordinal);

        var mutacao = pedido with { Resultados = [resultado with { Sha = new string('f', 40) }, falha, semProdutor] };
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync(rota, mutacao)).StatusCode);
        Assert.Equal(Sha, (await ana.GetFromJsonAsync<PedidoDeArtefatosV1>(rota))!.Resultados[0].Sha);
        var outroBranch = pedido with { Resultados = [resultado with { Ciclo = resultado.Ciclo! with { Branch = "outro-branch" } }, falha, semProdutor] };
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync(rota, outroBranch)).StatusCode);
    }

    [Fact]
    public async Task Pedido_antigo_individual_mantem_download_e_retencao_na_leitura_agrupada()
    {
        var (ana, _, _, projeto) = await CenarioAsync();
        var upload = (await (await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip()))).Content.ReadFromJsonAsync<ArtefatoV1>())!;
        var rota = $"/api/projetos/{projeto}/artefatos/pedidos/{upload.Id}";
        var singleton = (await ana.GetFromJsonAsync<PedidoDeArtefatosV1>(rota))!;
        Assert.Equal("publicado", singleton.Estado);
        Assert.Equal(upload.Id, Assert.Single(singleton.Resultados).IdRemoto);
        Assert.NotEmpty(await _api.CreateClient().GetByteArrayAsync(singleton.Resultados[0].Link));
        using (var escopo = _api.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
            (await db.ArtefatosDosProjetos.SingleAsync()).RetidoAteUtc = DateTime.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }
        var expirado = (await ana.GetFromJsonAsync<PedidoDeArtefatosV1>(rota))!;
        Assert.Equal("expirado", expirado.Estado);
        Assert.Empty(Assert.Single(expirado.Resultados).Link);
        Assert.Empty((await ana.GetFromJsonAsync<ArtefatoV1[]>($"/api/projetos/{projeto}/artefatos"))!);
    }
}
