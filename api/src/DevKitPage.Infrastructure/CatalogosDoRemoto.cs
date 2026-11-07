using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O catálogo das máquinas no banco (o handshake do chat remoto): um <see cref="CatalogoRemoto"/> por (usuário, máquina). Só
/// grava na máquina que o usuário já registrou pelo heartbeat — a de outro usuário não aparece nem com o id certo. O primeiro
/// envio de duas chamadas ao mesmo tempo é decidido pelo índice único: o perdedor relê e grava por cima.
/// </summary>
public sealed class CatalogosDoRemoto(DevKitPageDb db, TimeProvider relogio) : ICatalogoRemoto
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CatalogoRecebidoV1?> GravarAsync(int usuarioId, CatalogoRemotoV1 catalogo, CancellationToken ct)
    {
        if (!await db.MaquinasRemotas.AsNoTracking().AnyAsync(m => m.UsuarioId == usuarioId && m.MaquinaId == catalogo.MaquinaId, ct))
            return null;

        var agora = relogio.GetUtcNow().UtcDateTime;
        var gerado = catalogo.GeradoEm.UtcDateTime;
        for (var tentativa = 1; ; tentativa++)
        {
            var gravado = await db.CatalogosRemotos.FirstOrDefaultAsync(c => c.UsuarioId == usuarioId && c.MaquinaId == catalogo.MaquinaId, ct);
            // O envio fora de ordem (o dev.kit respondeu dois pedidos e o mais velho chegou depois) não volta o catálogo para trás.
            if (gravado is not null && gerado < gravado.GeradoEmUtc)
            {
                db.ChangeTracker.Clear();
                return new CatalogoRecebidoV1(catalogo.MaquinaId, DevKitPageDb.Utc(agora), Aplicado: false);
            }

            if (gravado is null)
            {
                gravado = new CatalogoRemoto { UsuarioId = usuarioId, MaquinaId = catalogo.MaquinaId };
                db.CatalogosRemotos.Add(gravado);
            }
            gravado.Catalogo = JsonSerializer.Serialize(catalogo, Json);
            gravado.GeradoEmUtc = gerado;
            gravado.RecebidoEmUtc = agora;
            try
            {
                await db.SaveChangesAsync(ct);
                return new CatalogoRecebidoV1(catalogo.MaquinaId, DevKitPageDb.Utc(agora), Aplicado: true);
            }
            catch (DbUpdateException) when (tentativa == 1)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<CatalogoDaMaquinaV1?> LerAsync(int usuarioId, string maquinaId, CancellationToken ct)
    {
        var gravado = await db.CatalogosRemotos.AsNoTracking().FirstOrDefaultAsync(c => c.UsuarioId == usuarioId && c.MaquinaId == maquinaId, ct);
        return gravado is null || gravado.Catalogo.Length == 0
            ? null
            : new CatalogoDaMaquinaV1(gravado.MaquinaId, DevKitPageDb.Utc(gravado.RecebidoEmUtc), JsonSerializer.Deserialize<CatalogoRemotoV1>(gravado.Catalogo, Json)!);
    }
}
