using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O controle remoto no banco. Toda consulta parte do usuário do token: é esse filtro, e não uma regra na rota, que isola os
/// donos. As gravações vão por <c>ExecuteUpdate</c> com a condição no SQL, para dois envios ao mesmo tempo não se pisarem.
/// </summary>
public sealed class ControleRemoto(DevKitPageDb db, IOptions<OpcoesDoRemoto> opcoes, TimeProvider relogio) : IControleRemoto
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoDaGestao<HeartbeatRecebidoV1>> RegistrarHeartbeatAsync(int usuario, HeartbeatV1 heartbeat, CancellationToken ct)
    {
        var id = RegrasDoRemoto.NormalizarMaquina(heartbeat.MaquinaId);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var apelido = string.IsNullOrWhiteSpace(heartbeat.Apelido) ? RegrasDoRemoto.ApelidoPadrao(id) : heartbeat.Apelido.Trim();
        var versao = heartbeat.VersaoDevKit.Trim();
        var recebido = new HeartbeatRecebidoV1(id, DevKitPageDb.Utc(agora), opcoes.Value.IntervaloDoHeartbeatSegundos);

        Task<int> AtualizarAsync() => db.MaquinasRemotas.Where(m => m.UsuarioId == usuario && m.MaquinaId == id)
            .ExecuteUpdateAsync(m => m
                .SetProperty(x => x.Apelido, apelido)
                .SetProperty(x => x.VersaoDevKit, versao)
                .SetProperty(x => x.DevKitAberto, heartbeat.DevKitAberto)
                .SetProperty(x => x.Agente, heartbeat.Agente)
                .SetProperty(x => x.TurnosEmAndamento, heartbeat.TurnosEmAndamento)
                .SetProperty(x => x.UltimoHeartbeatEmUtc, agora), ct);

        if (await AtualizarAsync() > 0)
            return new(recebido);

        var maximo = opcoes.Value.MaximoDeMaquinas;
        if (await db.MaquinasRemotas.CountAsync(m => m.UsuarioId == usuario, ct) >= maximo)
            return ResultadoDaGestao<HeartbeatRecebidoV1>.Falhou(FalhaNaGestao.Conflito, $"Você já tem {maximo} máquinas no controle remoto: a sem heartbeat há mais de {opcoes.Value.RetencaoDias} dias libera a vaga.");

        db.MaquinasRemotas.Add(new MaquinaRemota
        {
            UsuarioId = usuario, MaquinaId = id, Apelido = apelido, VersaoDevKit = versao, DevKitAberto = heartbeat.DevKitAberto,
            Agente = heartbeat.Agente, TurnosEmAndamento = heartbeat.TurnosEmAndamento, UltimoHeartbeatEmUtc = agora, CriadaEmUtc = agora,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // O primeiro heartbeat em dobro: o índice único decide, e o perdedor vira atualização.
            db.ChangeTracker.Clear();
            if (await AtualizarAsync() == 0)
                throw;
        }

        return new(recebido);
    }

    public async Task<ResultadoDaGestao<RetratoRecebidoV1>> RegistrarRetratoAsync(int usuario, RetratoV1 retrato, CancellationToken ct)
    {
        var id = RegrasDoRemoto.NormalizarMaquina(retrato.MaquinaId);
        var maquina = await db.MaquinasRemotas.Where(m => m.UsuarioId == usuario && m.MaquinaId == id).Select(m => (long?)m.Id).FirstOrDefaultAsync(ct);
        if (maquina is not { } chave)
            return ResultadoDaGestao<RetratoRecebidoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Publique o heartbeat da máquina antes do retrato.");

        // O relógio da máquina adiantado não congela a foto: o retrato "do futuro" vale como gerado agora.
        var agora = relogio.GetUtcNow().UtcDateTime;
        var gerado = retrato.GeradoEm.UtcDateTime < agora ? retrato.GeradoEm.UtcDateTime : agora;
        var documento = JsonSerializer.Serialize(retrato with { MaquinaId = id, GeradoEm = DevKitPageDb.Utc(gerado) }, Json);

        var aplicado = new RetratoRecebidoV1(id, true, DevKitPageDb.Utc(gerado), DevKitPageDb.Utc(agora));
        var alteradas = await db.RetratosRemotos.Where(r => r.MaquinaRemotaId == chave && r.GeradoEmUtc < gerado)
            .ExecuteUpdateAsync(r => r
                .SetProperty(x => x.GeradoEmUtc, gerado)
                .SetProperty(x => x.RecebidoEmUtc, agora)
                .SetProperty(x => x.Documento, documento), ct);
        if (alteradas > 0)
            return new(aplicado);

        if (await GravadoAsync(chave, id, ct) is { } gravado)
            return new(gravado);

        db.RetratosRemotos.Add(new RetratoRemoto { MaquinaRemotaId = chave, GeradoEmUtc = gerado, RecebidoEmUtc = agora, Documento = documento });
        try
        {
            await db.SaveChangesAsync(ct);
            return new(aplicado);
        }
        catch (DbUpdateException)
        {
            // O primeiro retrato em dobro: o outro gravou, e o próximo envio põe a foto em dia.
            db.ChangeTracker.Clear();
            return new(await GravadoAsync(chave, id, ct) ?? throw new InvalidOperationException("O retrato não foi gravado nem por este envio nem por outro."));
        }
    }

    public async Task<IReadOnlyList<MaquinaRemotaV1>> MaquinasAsync(int usuario, CancellationToken ct)
    {
        var linhas = await db.MaquinasRemotas.AsNoTracking().Where(m => m.UsuarioId == usuario)
            .OrderByDescending(m => m.UltimoHeartbeatEmUtc).ThenBy(m => m.MaquinaId)
            .Select(m => new { Maquina = m, RetratoEm = m.Retrato == null ? (DateTime?)null : m.Retrato.RecebidoEmUtc })
            .ToListAsync(ct);
        return linhas.Select(l => Leitura(l.Maquina, l.RetratoEm)).ToArray();
    }

    public async Task<PainelRemotoV1?> PainelAsync(int usuario, string maquinaId, CancellationToken ct)
    {
        if (!RegrasDoRemoto.MaquinaValida(maquinaId))
            return null;
        var id = RegrasDoRemoto.NormalizarMaquina(maquinaId);
        var maquina = await db.MaquinasRemotas.AsNoTracking().Include(m => m.Retrato).FirstOrDefaultAsync(m => m.UsuarioId == usuario && m.MaquinaId == id, ct);
        if (maquina is null)
            return null;
        var retrato = maquina.Retrato is { } gravado ? JsonSerializer.Deserialize<RetratoV1>(gravado.Documento, Json) : null;
        return new PainelRemotoV1(Leitura(maquina, maquina.Retrato?.RecebidoEmUtc), retrato);
    }

    /// <summary>O retrato que continua valendo, sem o documento: o envio que chegou não mudou nada.</summary>
    private async Task<RetratoRecebidoV1?> GravadoAsync(long maquina, string id, CancellationToken ct)
    {
        var gravado = await db.RetratosRemotos.Where(r => r.MaquinaRemotaId == maquina)
            .Select(r => new { r.GeradoEmUtc, r.RecebidoEmUtc }).FirstOrDefaultAsync(ct);
        return gravado is null ? null : new RetratoRecebidoV1(id, false, DevKitPageDb.Utc(gravado.GeradoEmUtc), DevKitPageDb.Utc(gravado.RecebidoEmUtc));
    }

    private MaquinaRemotaV1 Leitura(MaquinaRemota m, DateTime? retratoEm)
    {
        var ultimo = DevKitPageDb.Utc(m.UltimoHeartbeatEmUtc);
        return new MaquinaRemotaV1(
            m.MaquinaId, m.Apelido, m.VersaoDevKit, RegrasDoRemoto.Conectada(ultimo, relogio.GetUtcNow(), opcoes.Value.ToleranciaDoHeartbeatSegundos),
            m.DevKitAberto, m.Agente, m.TurnosEmAndamento, ultimo, retratoEm is { } em ? DevKitPageDb.Utc(em) : null);
    }
}

/// <summary>O expurgo do controle remoto: a máquina parada além da retenção sai, e o retrato vai junto pela cascata.</summary>
public sealed class ExpurgoDoRemoto(DevKitPageDb db, IOptions<OpcoesDoRemoto> opcoes, TimeProvider relogio) : IExpurgoDoRemoto
{
    public Task<int> ExpurgarAsync(CancellationToken ct)
    {
        var corte = relogio.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, opcoes.Value.RetencaoDias));
        return db.MaquinasRemotas.Where(m => m.UltimoHeartbeatEmUtc < corte).ExecuteDeleteAsync(ct);
    }
}
