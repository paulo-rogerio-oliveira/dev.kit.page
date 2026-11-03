using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O controle remoto no banco: uma <see cref="MaquinaRemota"/> por (usuário, máquina). Toda consulta filtra pelo usuário
/// do token — a máquina de outro usuário não aparece nem com o id certo. O primeiro envio de duas chamadas ao mesmo tempo
/// é decidido pelo índice único: o perdedor relê e grava por cima.
/// </summary>
public sealed class Remoto(DevKitPageDb db, TimeProvider relogio) : IRemoto
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<HeartbeatRecebidoV1> RegistrarHeartbeatAsync(int usuarioId, HeartbeatV1 heartbeat, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        await GravarAsync(usuarioId, heartbeat.MaquinaId, agora, maquina =>
        {
            maquina.Nome = heartbeat.Nome?.Trim() ?? string.Empty;
            maquina.VersaoDevKit = heartbeat.VersaoDevKit?.Trim() ?? string.Empty;
            maquina.Origem = RegrasDoRemoto.Normalizar(heartbeat.Origem);
            maquina.AppAberto = heartbeat.AppAberto;
            var estado = RegrasDoRemoto.Normalizar(heartbeat.EstadoDoAgente);
            maquina.EstadoDoAgente = estado.Length == 0 ? RemotoV1.AgenteParado : estado;
            maquina.TurnoEmAndamento = heartbeat.TurnoEmAndamento;
            maquina.UltimoHeartbeatEmUtc = agora;
            return true;
        }, ct);
        return new HeartbeatRecebidoV1(heartbeat.MaquinaId, DevKitPageDb.Utc(agora), RemotoV1.IntervaloDoHeartbeatSegundos);
    }

    public async Task<RetratoRecebidoV1> GravarRetratoAsync(int usuarioId, RetratoV1 retrato, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var gerado = retrato.GeradoEm.UtcDateTime;
        var aplicado = await GravarAsync(usuarioId, retrato.MaquinaId, agora, maquina =>
        {
            // O envio fora de ordem (o serviço reenviou um retrato velho) não volta a foto para trás.
            if (maquina.RetratoGeradoEmUtc is { } gravado && gerado < gravado)
                return false;
            maquina.Retrato = JsonSerializer.Serialize(retrato, Json);
            maquina.RetratoGeradoEmUtc = gerado;
            maquina.RetratoRecebidoEmUtc = agora;
            return true;
        }, ct);
        return new RetratoRecebidoV1(retrato.MaquinaId, DevKitPageDb.Utc(agora), aplicado);
    }

    public async Task<IReadOnlyList<MaquinaRemotaV1>> MaquinasAsync(int usuarioId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var maquinas = await db.MaquinasRemotas.AsNoTracking().Where(m => m.UsuarioId == usuarioId).ToListAsync(ct);
        return maquinas.Select(m => Leitura(m, agora))
            .OrderByDescending(m => m.Conectada).ThenByDescending(m => m.UltimoHeartbeatEm).ThenBy(m => m.MaquinaId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<RetratoDaMaquinaV1?> RetratoAsync(int usuarioId, string maquinaId, CancellationToken ct)
    {
        var maquina = await db.MaquinasRemotas.AsNoTracking().FirstOrDefaultAsync(m => m.UsuarioId == usuarioId && m.MaquinaId == maquinaId, ct);
        if (maquina is null || maquina.Retrato.Length == 0 || maquina.RetratoRecebidoEmUtc is not { } recebido)
            return null;
        var retrato = JsonSerializer.Deserialize<RetratoV1>(maquina.Retrato, Json)!;
        return new RetratoDaMaquinaV1(Leitura(maquina, relogio.GetUtcNow().UtcDateTime), retrato, DevKitPageDb.Utc(recebido));
    }

    /// <summary>
    /// Cria a máquina no primeiro envio e aplica <paramref name="alterar"/>; o falso dele não grava nada. A corrida do
    /// primeiro envio (dois inserts) cai no índice único: limpa o rastreador e repete UMA vez, já como atualização.
    /// </summary>
    private async Task<bool> GravarAsync(int usuarioId, string maquinaId, DateTime agora, Func<MaquinaRemota, bool> alterar, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var maquina = await db.MaquinasRemotas.FirstOrDefaultAsync(m => m.UsuarioId == usuarioId && m.MaquinaId == maquinaId, ct);
            if (maquina is null)
            {
                maquina = new MaquinaRemota { UsuarioId = usuarioId, MaquinaId = maquinaId, CriadaEmUtc = agora, EstadoDoAgente = RemotoV1.AgenteParado };
                db.MaquinasRemotas.Add(maquina);
            }

            if (!alterar(maquina))
            {
                db.ChangeTracker.Clear();
                return false;
            }
            maquina.AtualizadaEmUtc = agora;
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException) when (tentativa == 1)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private static MaquinaRemotaV1 Leitura(MaquinaRemota m, DateTime agora)
        => new(m.MaquinaId, m.Nome, m.VersaoDevKit, m.Origem, m.AppAberto, m.EstadoDoAgente, m.TurnoEmAndamento,
            m.UltimoHeartbeatEmUtc is { } ultimo && (agora - ultimo).TotalSeconds <= RemotoV1.SegundosAteDesconectar,
            m.UltimoHeartbeatEmUtc is { } hb ? DevKitPageDb.Utc(hb) : null,
            m.RetratoGeradoEmUtc is { } gerado ? DevKitPageDb.Utc(gerado) : null);
}

/// <summary>A retenção do controle remoto (<see cref="OpcoesDoRemoto.RetencaoDias"/>): pelo último contato da máquina.</summary>
public sealed class ExpurgoDoRemoto(DevKitPageDb db, TimeProvider relogio, IOptions<OpcoesDoRemoto> opcoes) : IExpurgoDoRemoto
{
    public Task<int> ExpurgarAsync(CancellationToken ct)
    {
        var corte = relogio.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, opcoes.Value.RetencaoDias));
        return db.MaquinasRemotas.Where(m => m.AtualizadaEmUtc < corte).ExecuteDeleteAsync(ct);
    }
}
