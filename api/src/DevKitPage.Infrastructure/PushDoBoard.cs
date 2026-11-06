using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

public sealed class PushDoBoard(DevKitPageDb db, IDecisoesRemotas decisoes, IOptions<OpcoesDoPush> opcoes,
    IDataProtectionProvider protecao, TimeProvider relogio) : IPushDoBoard
{
    private readonly IDataProtector _protetor = protecao.CreateProtector("devkit.board.push.v1");
    public ConfiguracaoDePushV1 Configuracao() => new(opcoes.Value.Disponivel, opcoes.Value.Disponivel ? opcoes.Value.ChavePublica : null);

    public async Task<ResultadoDaDecisao<AssinaturaRegistradaV1>> AssinarAsync(int usuario, AssinaturaDePushV1 pedido, CancellationToken ct)
    {
        if (RegrasDoPush.Validar(pedido) is { } erro) return new(null, erro, 400);
        if (!opcoes.Value.Disponivel) return new(null, "Notificações indisponíveis neste ambiente.", 503);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pedido.Endpoint)));
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var assinatura = await db.AssinaturasDoPush.SingleOrDefaultAsync(a => a.EndpointHash == hash, ct);
        // Um endpoint de outra conta nunca é transferido por conhecer sua URL.
        if (assinatura is not null && assinatura.UsuarioId != usuario) return new(null, "Assinatura indisponível. Desative as notificações na conta anterior e tente novamente.");
        if (assinatura is null)
        {
            if (await db.AssinaturasDoPush.CountAsync(a => a.UsuarioId == usuario, ct) >= RegrasDoPush.LimiteDeDispositivos)
                return new(null, "Limite de dispositivos atingido. Desative um dispositivo antes de adicionar outro.", 409);
            assinatura = new AssinaturaDoPush { UsuarioId = usuario, EndpointHash = hash };
            db.AssinaturasDoPush.Add(assinatura);
        }
        assinatura.DadosProtegidos = _protetor.Protect(JsonSerializer.Serialize(pedido));
        assinatura.ChavePublicaServidor = opcoes.Value.ChavePublica;
        assinatura.AtualizadaEmUtc = relogio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct); await transacao.CommitAsync(ct);
        return new(new(assinatura.Id));
    }

    public Task<bool> AssinadaAsync(int usuario, string id, CancellationToken ct)
        => db.AssinaturasDoPush.AnyAsync(a => a.UsuarioId == usuario && a.Id == id && a.AtualizadaEmUtc > relogio.GetUtcNow().UtcDateTime.AddDays(-RegrasDoPush.RetencaoDias)
            && a.ChavePublicaServidor == opcoes.Value.ChavePublica, ct);

    public async Task CancelarAsync(int usuario, string id, CancellationToken ct)
        => _ = await db.AssinaturasDoPush.Where(a => a.UsuarioId == usuario && a.Id == id).ExecuteDeleteAsync(ct);

    public async Task<DestinoDoPushV1?> DestinoAsync(int usuario, string entrega, CancellationToken ct)
    {
        var d = await (from e in db.EntregasDoPush.AsNoTracking()
                       join a in db.AssinaturasDoPush on e.AssinaturaId equals a.Id
                       where e.Id == entrega && a.UsuarioId == usuario
                       select e).SingleOrDefaultAsync(ct);
        if (d is null) return null;
        var pendentes = await decisoes.PendentesAsync(usuario, d.MaquinaId, ct);
        var atual = pendentes?.SingleOrDefault(p => p.Pendencia.Id == d.PendenciaId && p.SnapshotId == d.SnapshotId);
        return atual is not null && (atual.Resposta is null || atual.Resposta.Estado == "vencida")
            ? new(d.MaquinaId, d.PendenciaId, d.SnapshotId) : null;
    }
}
