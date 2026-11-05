using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

public sealed class SegundoFator(DevKitPageDb db, IDataProtectionProvider protecao, IPasswordHasher<Usuario> senhas, TimeProvider relogio) : ISegundoFator
{
    private readonly IDataProtector _protetor = protecao.CreateProtector("devkit.decisoes.totp.v1");

    public Task<bool> AtivoAsync(int usuario, CancellationToken ct)
        => db.Autenticadores.AnyAsync(a => a.UsuarioId == usuario && a.Ativo, ct);

    public async Task<ResultadoDaDecisao<AutenticadorPreparadoV1>> PrepararAsync(int usuario, string senha, CancellationToken ct)
    {
        var dono = await db.Usuarios.AsNoTracking().SingleAsync(u => u.Id == usuario, ct);
        if (senha is null || senhas.VerifyHashedPassword(dono, dono.SenhaHash, senha) == PasswordVerificationResult.Failed)
            return new(null, "Senha atual incorreta.", 403);
        var a = await db.Autenticadores.SingleOrDefaultAsync(a => a.UsuarioId == usuario, ct);
        if (a?.Ativo == true) return new(null, "O autenticador já está ativo; ele não pode ser substituído por esta operação.");
        var agora = relogio.GetUtcNow();
        if (a is null) { a = new AutenticadorDoUsuario { UsuarioId = usuario }; db.Autenticadores.Add(a); }
        // Não reinicia uma preparação válida: não permite zerar o limite de tentativas recriando a chave.
        if (a.SegredoProtegido.Length > 0 && a.PreparadoEmUtc > agora.UtcDateTime.AddMinutes(-10))
            return new(null, "Já há uma configuração em andamento. Conclua-a ou aguarde dez minutos.");
        var chave = Totp.NovaChave();
        a.SegredoProtegido = _protetor.Protect(chave); a.PreparadoEmUtc = agora.UtcDateTime;
        a.UltimoPasso = -1; a.Tentativas = 0; a.JanelaEmUtc = agora.UtcDateTime;
        await db.SaveChangesAsync(ct);
        return new(new(chave, $"otpauth://totp/dev.kit:{usuario}?secret={chave}&issuer=dev.kit&digits=6&period=30", agora.AddMinutes(10)));
    }

    public async Task<string?> VerificarAsync(int usuario, string? codigo, bool ativar, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var a = await db.Autenticadores.AsNoTracking().SingleOrDefaultAsync(a => a.UsuarioId == usuario, ct);
        if (a is null || (!ativar && !a.Ativo)) return "Configure o aplicativo autenticador antes de aprovar.";
        if (ativar && a.Ativo) return "Autenticador já ativo.";
        if (!a.Ativo && a.PreparadoEmUtc < agora.UtcDateTime.AddMinutes(-10)) return "Configuração expirada. Prepare o autenticador novamente.";
        await db.Autenticadores.Where(x => x.UsuarioId == usuario && x.JanelaEmUtc < agora.UtcDateTime.AddMinutes(-5))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Tentativas, 0).SetProperty(x => x.JanelaEmUtc, agora.UtcDateTime), ct);
        if (await db.Autenticadores.Where(x => x.UsuarioId == usuario && x.Tentativas < 5)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Tentativas, x => x.Tentativas + 1), ct) != 1)
            return "Limite de tentativas. Aguarde cinco minutos e tente novamente.";
        var passo = Totp.Conferir(_protetor.Unprotect(a.SegredoProtegido), codigo, agora, a.UltimoPasso);
        if (passo is null) return "Código incorreto, expirado ou já utilizado. Tente novamente com um código válido.";
        var mudou = await db.Autenticadores.Where(x => x.UsuarioId == usuario && x.UltimoPasso == a.UltimoPasso && x.SegredoProtegido == a.SegredoProtegido)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.UltimoPasso, passo.Value).SetProperty(x => x.Ativo, true), ct);
        return mudou == 1 ? null : "Código já utilizado. Aguarde o próximo código.";
    }
}
