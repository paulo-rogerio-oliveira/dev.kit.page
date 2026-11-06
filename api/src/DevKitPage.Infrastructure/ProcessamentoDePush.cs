using System.Security.Cryptography;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>Fila durável por assinatura/snapshot; reserva atômica e retries limitados sobrevivem ao reinício da API.</summary>
public sealed class ProcessamentoDePush(DevKitPageDb db, IDecisoesRemotas decisoes, ITransporteDePush transporte,
    IDataProtectionProvider protecao, IOptions<OpcoesDoPush> opcoes, TimeProvider relogio) : IProcessamentoDePush
{
    private readonly IDataProtector _protetor = protecao.CreateProtector("devkit.board.push.v1");
    public async Task<int> ProcessarAsync(CancellationToken ct)
    {
        if (!opcoes.Value.Disponivel) return 0;
        var agora = relogio.GetUtcNow().UtcDateTime;
        var retencao = agora.AddDays(-RegrasDoPush.RetencaoDias);
        await db.AssinaturasDoPush.Where(a => a.AtualizadaEmUtc < retencao || a.ChavePublicaServidor != opcoes.Value.ChavePublica).ExecuteDeleteAsync(ct);
        await db.EntregasDoPush.Where(e => e.CriadaEmUtc < retencao).ExecuteDeleteAsync(ct);
        await db.EntregasDoPush.Where(e => e.ExpiraEmUtc <= agora && (e.Estado == "pendente" || e.Estado == "enviando"))
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, "expirada"), ct);
        // As mais antigas primeiro, com marca persistida: o lote limitado não abandona os demais dispositivos.
        var assinaturas = await db.AssinaturasDoPush.AsNoTracking().OrderBy(a => a.VerificadaEmUtc).ThenBy(a => a.Id).Take(20).ToArrayAsync(ct);
        foreach (var a in assinaturas)
        {
            await db.AssinaturasDoPush.Where(x => x.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.VerificadaEmUtc, agora), ct);
            var maquinas = await db.MaquinasRemotas.AsNoTracking().Where(m => m.UsuarioId == a.UsuarioId && m.Retrato != "" && m.UltimoHeartbeatEmUtc > agora.AddSeconds(-RemotoV1.SegundosAteDesconectar))
                .Select(m => m.MaquinaId).ToArrayAsync(ct);
            foreach (var maquina in maquinas)
            {
                var pendentes = await decisoes.PendentesAsync(a.UsuarioId, maquina, ct) ?? [];
                foreach (var p in pendentes.Where(p => p.Pendencia.Tipo is "pergunta" or "aprovacao" && (p.Resposta is null || p.Resposta.Estado == "vencida")))
                {
                    if (await db.EntregasDoPush.AnyAsync(e => e.AssinaturaId == a.Id && e.MaquinaId == maquina && e.PendenciaId == p.Pendencia.Id && e.SnapshotId == p.SnapshotId, ct)) continue;
                    var entrega = new EntregaDoPush { AssinaturaId = a.Id, MaquinaId = maquina, PendenciaId = p.Pendencia.Id, SnapshotId = p.SnapshotId,
                        CriadaEmUtc = agora, ProximaTentativaUtc = agora, ExpiraEmUtc = agora.AddSeconds(RegrasDoPush.TtlSegundos) };
                    db.EntregasDoPush.Add(entrega);
                    try { await db.SaveChangesAsync(ct); }
                    catch (DbUpdateException)
                    {
                        db.Entry(entrega).State = EntityState.Detached;
                        // Outra instância reservou a mesma entrega, ou a assinatura foi cancelada durante a varredura.
                        if (await db.AssinaturasDoPush.AnyAsync(x => x.Id == a.Id, ct)
                            && !await db.EntregasDoPush.AnyAsync(e => e.AssinaturaId == a.Id && e.MaquinaId == maquina && e.PendenciaId == p.Pendencia.Id && e.SnapshotId == p.SnapshotId, ct)) throw;
                    }
                    db.Entry(entrega).State = EntityState.Detached;
                }
            }
        }
        var fila = await db.EntregasDoPush.AsNoTracking().Where(e => (e.Estado == "pendente" || e.Estado == "enviando") && e.ProximaTentativaUtc <= agora && e.ExpiraEmUtc > agora && e.Tentativas < 3)
            .OrderBy(e => e.ProximaTentativaUtc).Take(100).ToArrayAsync(ct);
        var aceitos = 0;
        foreach (var e in fila)
        {
            var reserva = Guid.NewGuid().ToString("N");
            if (await db.EntregasDoPush.Where(x => x.Id == e.Id && x.Reserva == e.Reserva && x.ProximaTentativaUtc <= agora)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Reserva, reserva).SetProperty(x => x.Estado, "enviando")
                    .SetProperty(x => x.Tentativas, x => x.Tentativas + 1).SetProperty(x => x.ProximaTentativaUtc, agora.AddMinutes(1)), ct) != 1) continue;
            var assinatura = await db.AssinaturasDoPush.AsNoTracking().SingleOrDefaultAsync(a => a.Id == e.AssinaturaId, ct);
            if (assinatura is null) continue;
            // Cancela pendência resolvida, removida ou com snapshot alterado antes de fazer I/O externo.
            var atual = (await decisoes.PendentesAsync(assinatura.UsuarioId, e.MaquinaId, ct))?.SingleOrDefault(p => p.Pendencia.Id == e.PendenciaId && p.SnapshotId == e.SnapshotId);
            if (atual is null || atual.Resposta is { Estado: not "vencida" }) { await Finalizar(e.Id, reserva, "cancelada", agora, ct); continue; }
            AssinaturaDePushV1 pedido;
            try { pedido = JsonSerializer.Deserialize<AssinaturaDePushV1>(_protetor.Unprotect(assinatura.DadosProtegidos))!; }
            catch (CryptographicException) { await db.AssinaturasDoPush.Where(a => a.Id == assinatura.Id).ExecuteDeleteAsync(ct); continue; }
            var resultado = await transporte.EnviarAsync(pedido, e.Id, ct);
            if (resultado == ResultadoDoEnvioDePush.AssinaturaInvalida)
                await db.AssinaturasDoPush.Where(a => a.Id == assinatura.Id).ExecuteDeleteAsync(ct);
            else
            {
                var estado = resultado == ResultadoDoEnvioDePush.Aceito ? "aceita" : e.Tentativas >= 2 ? "falha" : "pendente";
                await Finalizar(e.Id, reserva, estado, agora.AddSeconds(30 * (e.Tentativas + 1)), ct);
                if (resultado == ResultadoDoEnvioDePush.Aceito) aceitos++;
            }
        }
        return aceitos;
    }
    private Task<int> Finalizar(string id, string reserva, string estado, DateTime proxima, CancellationToken ct)
        => db.EntregasDoPush.Where(e => e.Id == id && e.Reserva == reserva).ExecuteUpdateAsync(s => s.SetProperty(e => e.Estado, estado).SetProperty(e => e.ProximaTentativaUtc, proxima), ct);
}
