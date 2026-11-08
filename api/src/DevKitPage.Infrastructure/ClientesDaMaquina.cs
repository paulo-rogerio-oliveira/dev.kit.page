using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// <see cref="IClientesDaMaquina"/> no banco: um <see cref="ClienteDaMaquina"/> por máquina e os <c>jti</c> gastos de cada um.
/// O vínculo novo da máquina substitui o anterior (outro usuário que entrou nela, a chave regerada); a revogação só marca a
/// data — o "desvincular" fica na trilha, e o vínculo seguinte o reabre.
/// </summary>
public sealed class ClientesDaMaquina(DevKitPageDb db, TimeProvider relogio) : IClientesDaMaquina
{
    public async Task RegistrarAsync(int maquinaId, int usuarioId, string chavePublica, string carimbo, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        for (var tentativa = 1; ; tentativa++)
        {
            var cliente = await db.ClientesDaMaquina.FirstOrDefaultAsync(c => c.MaquinaId == maquinaId, ct);
            if (cliente is null)
            {
                cliente = new ClienteDaMaquina { MaquinaId = maquinaId };
                db.ClientesDaMaquina.Add(cliente);
            }
            else if (cliente.UsuarioId != usuarioId || cliente.ChavePublica != chavePublica)
            {
                // Outro usuário (ou outra chave): os jti do vínculo antigo não dizem nada do novo.
                await db.AssercoesDoCliente.Where(a => a.ClienteId == cliente.Id).ExecuteDeleteAsync(ct);
            }

            cliente.UsuarioId = usuarioId;
            cliente.ChavePublica = chavePublica;
            cliente.Carimbo = carimbo;
            cliente.RegistradoEmUtc = agora;
            cliente.RevogadoEmUtc = null;
            try
            {
                await db.SaveChangesAsync(ct);
                return;
            }
            catch (DbUpdateException) when (tentativa == 1)
            {
                // A corrida do primeiro vínculo (dois logins ao mesmo tempo): o índice único decide, e o perdedor regrava.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<ClienteParaEmitir?> AtivoAsync(string clientId, CancellationToken ct)
    {
        var lido = await db.ClientesDaMaquina.AsNoTracking()
            .Where(c => c.Maquina!.MaquinaId == clientId && c.RevogadoEmUtc == null)
            .Select(c => new { c.Id, c.MaquinaId, ClientId = c.Maquina!.MaquinaId, c.UsuarioId, c.ChavePublica, c.Carimbo })
            .FirstOrDefaultAsync(ct);
        return lido is null ? null : new ClienteParaEmitir(lido.Id, lido.MaquinaId, lido.ClientId, lido.UsuarioId, lido.ChavePublica, lido.Carimbo);
    }

    public async Task<bool> GastarAssercaoAsync(int clienteId, string jti, DateTime expiraEmUtc, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        // Os vencidos deste cliente saem antes: a tabela fica do tamanho das asserções vivas dele.
        await db.AssercoesDoCliente.Where(a => a.ClienteId == clienteId && a.ExpiraEmUtc < agora).ExecuteDeleteAsync(ct);
        if (await db.AssercoesDoCliente.AnyAsync(a => a.ClienteId == clienteId && a.Jti == jti, ct))
            return false;
        db.AssercoesDoCliente.Add(new AssercaoDoCliente { ClienteId = clienteId, Jti = jti, ExpiraEmUtc = expiraEmUtc });
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // A mesma asserção em duas chamadas simultâneas: o índice único deixa passar só uma.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task MarcarEmissaoAsync(int clienteId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        return db.ClientesDaMaquina.Where(c => c.Id == clienteId).ExecuteUpdateAsync(s => s.SetProperty(c => c.UltimoTokenEmUtc, agora), ct);
    }

    public Task RevogarAsync(int clienteId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        return db.ClientesDaMaquina.Where(c => c.Id == clienteId && c.RevogadoEmUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevogadoEmUtc, agora), ct);
    }

    public async Task<bool> DesvincularAsync(int usuarioId, string maquinaPublica, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        return await db.ClientesDaMaquina
            .Where(c => c.UsuarioId == usuarioId && c.Maquina!.MaquinaId == maquinaPublica && c.RevogadoEmUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RevogadoEmUtc, agora), ct) > 0;
    }

    public async Task<IReadOnlySet<string>> VinculadasAsync(int usuarioId, CancellationToken ct)
        => (await db.ClientesDaMaquina.AsNoTracking()
            .Where(c => c.UsuarioId == usuarioId && c.RevogadoEmUtc == null)
            .Select(c => c.Maquina!.MaquinaId)
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

    public Task<bool> RevogadaAsync(int maquinaId, CancellationToken ct)
        => db.ClientesDaMaquina.AnyAsync(c => c.MaquinaId == maquinaId && c.RevogadoEmUtc != null, ct);

    public async Task<bool> ValeAsync(int clienteId, int usuarioId, CancellationToken ct)
    {
        var lido = await db.ClientesDaMaquina.AsNoTracking()
            .Where(c => c.Id == clienteId && c.UsuarioId == usuarioId && c.RevogadoEmUtc == null)
            .Select(c => new { c.Carimbo, c.Usuario!.SenhaHash, c.Usuario.BloqueadoPeloAdmin, c.Usuario.DeveTrocarSenha })
            .FirstOrDefaultAsync(ct);
        return lido is not null && !lido.BloqueadoPeloAdmin && !lido.DeveTrocarSenha
               && lido.Carimbo == RegrasDoClienteDaMaquina.Carimbo(lido.SenhaHash);
    }
}
