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
        var usuario = await db.Usuarios.Include(u => u.Empresa).FirstOrDefaultAsync(u => u.Login == (login ?? string.Empty).Trim(), ct);
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
        => db.Usuarios.AsNoTracking().Include(u => u.Empresa).FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
}

/// <summary>
/// O registro das máquinas: a chave nasce aqui e só o hash fica. É também onde a máquina ADERE a uma
/// empresa (US #381): o dev.kit só manda o código depois de o colaborador aceitar o aviso de coleta, e
/// muda de adesão registrando de novo.
/// </summary>
public sealed class Maquinas(DevKitPageDb db, TimeProvider relogio) : IMaquinas
{
    public async Task<RegistroDaMaquina> RegistrarAsync(string maquinaId, string versaoDevKit, string? codigoEmpresa, string? colaborador, CancellationToken ct)
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
        // Sem o campo (o dev.kit antigo, ou só a chave girando), o vínculo fica como está.
        string? empresa, adesao = null;
        if (codigoEmpresa is null)
            empresa = maquina.EmpresaId is { } atual ? (await db.Empresas.FindAsync([atual], ct))?.Nome : null;
        else
            (empresa, adesao) = await AderirAsync(maquina, codigoEmpresa, colaborador, ct);

        await db.SaveChangesAsync(ct);

        // O ÚLTIMO assento disputado por duas adesões ao mesmo tempo: as duas passaram na contagem antes
        // de gravar. Depois de gravar, a ordem de consentimento decide — fica quem chegou primeiro, e a
        // que passou do teto desfaz o próprio vínculo (a regra é a mesma nos dois processos, então só
        // a excedente sai).
        if (maquina.EmpresaId is { } vinculada && codigoEmpresa is not null && !await DentroDosAssentosAsync(maquina, vinculada, ct))
        {
            var nome = empresa;
            Desvincular(maquina);
            await db.SaveChangesAsync(ct);
            (empresa, adesao) = (null, $"A empresa {nome} não tem assento livre: a máquina continua anônima. Fale com o gestor.");
        }

        return new RegistroDaMaquina(chave, empresa, adesao);
    }

    /// <summary>A máquina está entre as N primeiras da empresa pela ordem de consentimento (e o id, no empate)?</summary>
    private async Task<bool> DentroDosAssentosAsync(Maquina maquina, int empresaId, CancellationToken ct)
    {
        var assentos = await db.Empresas.Where(e => e.Id == empresaId).Select(e => e.Assentos).FirstAsync(ct);
        var primeiras = await db.Maquinas.AsNoTracking()
            .Where(m => m.EmpresaId == empresaId && m.ConsentiuEmUtc != null)
            .OrderBy(m => m.ConsentiuEmUtc).ThenBy(m => m.Id)
            .Take(assentos)
            .Select(m => m.Id)
            .ToListAsync(ct);
        return primeiras.Contains(maquina.Id);
    }

    /// <summary>
    /// Aplica o código de adesão: vazio sai da empresa; o código conhecido e com assento livre vincula,
    /// com o nome do colaborador e o instante do consentimento (mantido quando ele só registra de novo);
    /// o desconhecido ou sem assento deixa a máquina ANÔNIMA — nunca num vínculo que ninguém pediu.
    /// </summary>
    private async Task<(string? Empresa, string Adesao)> AderirAsync(Maquina maquina, string codigoEmpresa, string? colaborador, CancellationToken ct)
    {
        var codigo = CodigoDeAdesao.Normalizar(codigoEmpresa);
        var empresa = codigo.Length == 0 ? null : await db.Empresas.FirstOrDefaultAsync(e => e.CodigoDeAdesao == codigo, ct);

        if (empresa is not null && maquina.EmpresaId != empresa.Id)
        {
            var ocupados = await db.Maquinas.CountAsync(m => m.EmpresaId == empresa.Id && m.ConsentiuEmUtc != null, ct);
            if (ocupados >= empresa.Assentos)
            {
                Desvincular(maquina);
                return (null, $"A empresa {empresa.Nome} não tem assento livre: a máquina continua anônima. Fale com o gestor.");
            }
        }

        if (empresa is null)
        {
            Desvincular(maquina);
            return (null, codigo.Length == 0
                ? "Sem adesão a empresa: a máquina é anônima."
                : "Código de empresa não reconhecido: a máquina continua anônima. Confira o código com o gestor.");
        }

        if (maquina.EmpresaId != empresa.Id || maquina.ConsentiuEmUtc is null)
        {
            // O consentimento vale daqui em diante: o uso anterior não vai para o gestor.
            maquina.ConsentiuEmUtc = relogio.GetUtcNow().UtcDateTime;
            maquina.DadosDesde = DateOnly.FromDateTime(maquina.ConsentiuEmUtc.Value);
        }

        maquina.EmpresaId = empresa.Id;
        maquina.Colaborador = ValidadorDeEmpresa.Colaborador(colaborador);
        return (empresa.Nome, $"Vinculada à empresa {empresa.Nome}: o gestor vê o uso desta máquina.");
    }

    private static void Desvincular(Maquina maquina)
    {
        maquina.EmpresaId = null;
        maquina.Colaborador = string.Empty;
        maquina.ConsentiuEmUtc = null;
        maquina.DadosDesde = null;
    }

    public Task<Maquina?> AutenticarAsync(string chave, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(chave))
            return Task.FromResult<Maquina?>(null);
        var hash = ChaveDeMaquina.Hash(chave.Trim());
        return db.Maquinas.AsNoTracking().FirstOrDefaultAsync(m => m.ChaveHash == hash, ct);
    }
}
