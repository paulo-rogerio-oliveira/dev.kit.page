using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKit.Matrix;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

public sealed partial class ConversaApiTests
{
    [Theory]
    [InlineData("disparar", null, false)]
    [InlineData("disparar", null, true)]
    [InlineData("disparar", 1, false)]
    [InlineData("disparar", 1, true)]
    [InlineData("retomar", null, false)]
    [InlineData("retomar", null, true)]
    public async Task Disparo_remoto_revisao_apos_insert_nao_deixa_pendente_e_permite_reenvio(string acao, int? item, bool comHistorico)
    {
        var revisao = new MatrizRevisadaAposInsert();
        using var cenario = new ConversaApiTests(new ApiDeTeste(configurarServicos: s =>
            s.AddDbContext<DevKitPageDb>((_, opcoes) => opcoes.AddInterceptors(revisao))));
        var (_, dev) = await cenario.DevComFilaAsync();
        (await dev.PutAsJsonAsync("/api/projetos", ProjetoParaDisparo(acao == "retomar" ? "pausado" : "aprovado"))).EnsureSuccessStatusCode();
        await DefinirDisparoAsync(dev);
        if (comHistorico)
            Assert.Equal(HttpStatusCode.Accepted, (await dev.PostAsJsonAsync("/api/remoto/comandos", Mensagem("anterior"))).StatusCode);
        var anteriores = comHistorico ? 1 : 0;
        string? historico = null;
        using (var escopo = cenario._api.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
            if (comHistorico) historico = JsonSerializer.Serialize(await db.ComandosRemotos.AsNoTracking().SingleAsync());
        }

        revisao.Vazia = MatrizDoDisparo() with { Matriz = MatrizDoDisparo().Matriz! with { Celulas = [] } };
        var pedido = PedidoDeDisparo(acao, item, "corrida-matrix");
        var recusada = await dev.PostAsJsonAsync("/api/remoto/comandos", pedido);
        Assert.Equal(HttpStatusCode.Conflict, recusada.StatusCode);
        Assert.Equal("matrix-pendente", await CodigoAsync(recusada));
        Assert.Equal(1, revisao.Aplicadas);
        Assert.Equal(anteriores, cenario._api.Fila.Enviadas.Count);
        using (var escopo = cenario._api.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
            Assert.Equal(anteriores, await db.ComandosRemotos.CountAsync());
            Assert.False(await db.ComandosRemotos.AnyAsync(c => c.ComandoId == pedido.Id));
            Assert.Equal(anteriores, await db.EventosDaConversa.CountAsync());
            Assert.Equal(2, await db.Set<RevisaoMatrixNoBanco>().CountAsync(r => r.Tipo == "matriz"));
            if (comHistorico)
                Assert.Equal(historico, JsonSerializer.Serialize(await db.ComandosRemotos.AsNoTracking().SingleAsync()));
        }

        (await dev.PutAsJsonAsync("/api/matrix/disparo/matriz/disparo?aplicar=true", new EdicaoMatrix(2, MatrizDoDisparo()))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Accepted, (await dev.PostAsJsonAsync("/api/remoto/comandos", pedido)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dev.PostAsJsonAsync("/api/remoto/comandos", pedido)).StatusCode);
        Assert.Equal(anteriores + 1, cenario._api.Fila.Enviadas.Count);
        using (var escopo = cenario._api.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
            Assert.Equal(anteriores + 1, await db.ComandosRemotos.CountAsync());
            Assert.Equal(ConversaV1.EstadoNaFila, (await db.ComandosRemotos.AsNoTracking().SingleAsync(c => c.ComandoId == pedido.Id)).Estado);
            Assert.Equal(anteriores + 1, await db.EventosDaConversa.CountAsync());
            Assert.Equal(3, await db.Set<RevisaoMatrixNoBanco>().CountAsync(r => r.Tipo == "matriz"));
            if (comHistorico)
                Assert.Equal(historico, JsonSerializer.Serialize(await db.ComandosRemotos.AsNoTracking().SingleAsync(c => c.ComandoId == "anterior")));
        }
    }

    /// <summary>Revisão determinística no intervalo reproduzido pelo avaliador, após o INSERT e antes da segunda guarda.</summary>
    private sealed class MatrizRevisadaAposInsert : SaveChangesInterceptor
    {
        public DocumentoMatrix? Vazia { get; set; }
        public int Aplicadas { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData dados, int resultado, CancellationToken ct = default)
        {
            if (Vazia is not { } documento || dados.Context is not DevKitPageDb db
                || !db.ChangeTracker.Entries<ComandoRemoto>().Any(e => e.Entity.ComandoId == "corrida-matrix")) return resultado;
            Vazia = null;
            var anterior = await db.Set<RevisaoMatrixNoBanco>().AsNoTracking().SingleAsync(r => r.Tipo == "matriz", ct);
            db.Add(new RevisaoMatrixNoBanco { Espaco = anterior.Espaco, Aplicacao = anterior.Aplicacao, Tipo = anterior.Tipo,
                DocumentoId = anterior.DocumentoId, Versao = 2, DonoId = anterior.DonoId, ProjetoId = anterior.ProjetoId,
                AutorId = anterior.AutorId, EmUtc = DateTime.UtcNow, Documento = JsonSerializer.Serialize(documento, RegrasMatrix.Json) });
            await db.SaveChangesAsync(ct);
            Aplicadas++;
            return resultado;
        }
    }
}
