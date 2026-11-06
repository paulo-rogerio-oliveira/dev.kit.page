using DevKitPage.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O autenticador do usuário ativado pelo caminho de verdade (<see cref="ISegundoFator"/>): a senha, a preparação e o
/// primeiro código. Devolve a chave; o relógio avança um passo, para o código seguinte não ser o já gasto.
/// </summary>
public static class SegundoFatorDeTeste
{
    private const string Senha = "senha-do-autenticador-2026";

    public static long Passo(BaseDeTeste b) => b.Relogio.Agora.ToUnixTimeSeconds() / 30;

    /// <summary>O código de 6 dígitos de agora.</summary>
    public static string Codigo(BaseDeTeste b, string chave) => Totp.Codigo(chave, Passo(b));

    public static async Task<string> AtivarAsync(BaseDeTeste b, int usuarioId)
    {
        await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var usuario = await db.Usuarios.SingleAsync(u => u.Id == usuarioId);
            usuario.SenhaHash = sp.GetRequiredService<IPasswordHasher<Usuario>>().HashPassword(usuario, Senha);
            return await db.SaveChangesAsync();
        });
        var preparado = await b.ComAsync(sp => sp.GetRequiredService<ISegundoFator>().PrepararAsync(usuarioId, Senha, default));
        var chave = preparado.Valor!.Chave;
        var recusa = await b.ComAsync(sp => sp.GetRequiredService<ISegundoFator>().VerificarAsync(usuarioId, Codigo(b, chave), ativar: true, default));
        if (recusa is not null)
            throw new InvalidOperationException(recusa);
        b.Relogio.Agora = b.Relogio.Agora.AddSeconds(30);
        return chave;
    }
}
