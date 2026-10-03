using DevKitPage.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O login dos usuários do dashboard, com o <see cref="IPasswordHasher{TUser}"/> do Identity (nada
/// de hash feito à mão) e o bloqueio depois de falhas seguidas.
/// </summary>
public sealed class Usuarios(DevKitPageDb db, IPasswordHasher<Usuario> hasher, IOptions<OpcoesDeAutenticacao> opcoes, TimeProvider relogio) : IUsuarios
{
    public async Task<(ResultadoDoLogin Resultado, Usuario? Usuario)> AutenticarAsync(string login, string senha, CancellationToken ct)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Login == (login ?? string.Empty).Trim(), ct);
        if (usuario is null)
            return (ResultadoDoLogin.CredencialInvalida, null);

        var agora = relogio.GetUtcNow().UtcDateTime;
        if (usuario.BloqueadoAteUtc is { } ate && ate > agora)
            return (ResultadoDoLogin.Bloqueado, null);

        var conferencia = hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senha ?? string.Empty);
        if (conferencia == PasswordVerificationResult.Failed)
        {
            usuario.FalhasSeguidas++;
            if (usuario.FalhasSeguidas >= Math.Max(1, opcoes.Value.MaxFalhas))
            {
                usuario.BloqueadoAteUtc = agora.AddMinutes(opcoes.Value.BloqueioMinutos);
                usuario.FalhasSeguidas = 0;
            }

            await db.SaveChangesAsync(ct);
            return (ResultadoDoLogin.CredencialInvalida, null);
        }

        if (conferencia == PasswordVerificationResult.SuccessRehashNeeded)
            usuario.SenhaHash = hasher.HashPassword(usuario, senha!);
        usuario.FalhasSeguidas = 0;
        usuario.BloqueadoAteUtc = null;
        await db.SaveChangesAsync(ct);
        return (ResultadoDoLogin.Ok, usuario);
    }

    public async Task<string> TrocarSenhaAsync(int usuarioId, string senhaAtual, string novaSenha, CancellationToken ct)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null)
            return "Usuário não encontrado.";
        if (hasher.VerifyHashedPassword(usuario, usuario.SenhaHash, senhaAtual ?? string.Empty) == PasswordVerificationResult.Failed)
            return "A senha atual não confere.";

        var problema = PoliticaDeSenha.Validar(novaSenha);
        if (problema.Length > 0)
            return problema;
        if (string.Equals(senhaAtual, novaSenha, StringComparison.Ordinal))
            return "A nova senha precisa ser diferente da atual.";

        usuario.SenhaHash = hasher.HashPassword(usuario, novaSenha);
        usuario.DeveTrocarSenha = false;
        await db.SaveChangesAsync(ct);
        return string.Empty;
    }

    public Task<Usuario?> ObterAsync(int usuarioId, CancellationToken ct)
        => db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
}

/// <summary>O registro das máquinas: a chave nasce aqui e só o hash fica.</summary>
public sealed class Maquinas(DevKitPageDb db, TimeProvider relogio) : IMaquinas
{
    public async Task<string> RegistrarAsync(string maquinaId, string versaoDevKit, CancellationToken ct)
    {
        var id = (maquinaId ?? string.Empty).Trim();
        var (chave, hash) = ChaveDeMaquina.Gerar();
        var maquina = await db.Maquinas.FirstOrDefaultAsync(m => m.MaquinaId == id, ct);
        if (maquina is null)
        {
            maquina = new Maquina
            {
                MaquinaId = id,
                Apelido = $"máquina {id[..Math.Min(8, id.Length)]}",
                RegistradaEmUtc = relogio.GetUtcNow().UtcDateTime,
            };
            db.Maquinas.Add(maquina);
        }

        // Registrar de novo GIRA a chave: o dev.kit só pede outra quando perdeu a dele (401, URL trocada).
        maquina.ChaveHash = hash;
        maquina.VersaoDevKit = ValidadorDeLote.Cortar(versaoDevKit);
        await db.SaveChangesAsync(ct);
        return chave;
    }

    public Task<Maquina?> AutenticarAsync(string chave, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chave))
            return Task.FromResult<Maquina?>(null);
        var hash = ChaveDeMaquina.Hash(chave.Trim());
        return db.Maquinas.AsNoTracking().FirstOrDefaultAsync(m => m.ChaveHash == hash, ct);
    }
}
