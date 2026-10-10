using DevKitPage.Contracts.V1;
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
            return (ResultadoDoLogin.Bloqueado, usuario); // com o usuário: a API diz ao app ATÉ quando

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

        // O bloqueio do admin só aparece para quem acertou a senha: sem ela, a resposta é a de sempre.
        if (usuario.BloqueadoPeloAdmin)
            return (ResultadoDoLogin.BloqueadoPeloAdmin, null);

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
/// A gestão de usuários pelo admin (US #405). O usuário criado ou redefinido recebe uma senha TEMPORÁRIA do
/// <see cref="GeradorDeSenha"/> — o mesmo do admin semeado e do gestor convidado —, devolvida UMA vez, e a
/// troca obrigatória no primeiro acesso (a regra que a API já aplica a todo token com a troca pendente).
/// </summary>
public sealed class GestaoDeUsuarios(DevKitPageDb db, IPasswordHasher<Usuario> hasher, TimeProvider relogio) : IGestaoDeUsuarios
{
    public async Task<IReadOnlyList<UsuarioResumo>> ListarAsync(CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        return (await db.Usuarios.AsNoTracking().Include(u => u.Empresa).OrderBy(u => u.Login).ToListAsync(ct))
            .Select(u => Resumo(u, agora))
            .ToArray();
    }

    public async Task<ResultadoDaGestao<UsuarioComSenha>> CriarAsync(UsuarioNovo novo, CancellationToken ct)
    {
        var login = novo.Login.Trim();
        if (await db.Usuarios.AnyAsync(u => u.Login == login, ct))
            return ResultadoDaGestao<UsuarioComSenha>.Falhou(FalhaNaGestao.Conflito, $"Já existe um usuário '{login}'.");
        if (await EmpresaInexistenteAsync(novo.EmpresaId, ct))
            return ResultadoDaGestao<UsuarioComSenha>.Falhou(FalhaNaGestao.Recusada, "A empresa informada não existe.");

        var usuario = new Usuario
        {
            Login = login,
            DeveTrocarSenha = true,
            CriadoEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        Aplicar(usuario, novo.Nome, novo.Papel, novo.EmpresaId);
        var senha = GeradorDeSenha.Temporaria();
        usuario.SenhaHash = hasher.HashPassword(usuario, senha);
        db.Usuarios.Add(usuario);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois cadastros do mesmo login ao mesmo tempo: o índice único decide, e o segundo é o conflito.
            db.Entry(usuario).State = EntityState.Detached;
            if (!await db.Usuarios.AsNoTracking().AnyAsync(u => u.Login == login, ct))
                throw;
            return ResultadoDaGestao<UsuarioComSenha>.Falhou(FalhaNaGestao.Conflito, $"Já existe um usuário '{login}'.");
        }

        return new(new UsuarioComSenha(await ResumoAsync(usuario.Id, ct), senha));
    }

    public async Task<ResultadoDaGestao<UsuarioResumo>> EditarAsync(int id, UsuarioEditado edicao, int quem, CancellationToken ct)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
            return ResultadoDaGestao<UsuarioResumo>.Falhou(FalhaNaGestao.NaoEncontrado, "Usuário não encontrado.");
        // O admin não tira o próprio papel: sem isto, o último admin se rebaixaria e ninguém mais administraria.
        if (id == quem && PapelDe(usuario) != edicao.Papel)
            return ResultadoDaGestao<UsuarioResumo>.Falhou(FalhaNaGestao.Recusada, "Você não pode mudar o seu próprio papel.");
        if (await EmpresaInexistenteAsync(edicao.EmpresaId, ct))
            return ResultadoDaGestao<UsuarioResumo>.Falhou(FalhaNaGestao.Recusada, "A empresa informada não existe.");

        Aplicar(usuario, edicao.Nome, edicao.Papel, edicao.EmpresaId);
        await db.SaveChangesAsync(ct);
        return new(await ResumoAsync(id, ct));
    }

    public async Task<ResultadoDaGestao<UsuarioResumo>> BloquearAsync(int id, bool bloqueado, int quem, CancellationToken ct)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
            return ResultadoDaGestao<UsuarioResumo>.Falhou(FalhaNaGestao.NaoEncontrado, "Usuário não encontrado.");
        if (id == quem && bloqueado)
            return ResultadoDaGestao<UsuarioResumo>.Falhou(FalhaNaGestao.Recusada, "Você não pode bloquear a si mesmo.");

        usuario.BloqueadoPeloAdmin = bloqueado;
        if (!bloqueado)
        {
            // Desbloquear devolve o acesso por inteiro: o bloqueio temporário por falhas sai junto.
            usuario.FalhasSeguidas = 0;
            usuario.BloqueadoAteUtc = null;
        }

        await db.SaveChangesAsync(ct);
        return new(await ResumoAsync(id, ct));
    }

    public async Task<ResultadoDaGestao<UsuarioComSenha>> RedefinirSenhaAsync(int id, CancellationToken ct)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (usuario is null)
            return ResultadoDaGestao<UsuarioComSenha>.Falhou(FalhaNaGestao.NaoEncontrado, "Usuário não encontrado.");

        var senha = GeradorDeSenha.Temporaria();
        usuario.SenhaHash = hasher.HashPassword(usuario, senha);
        usuario.DeveTrocarSenha = true;
        usuario.FalhasSeguidas = 0;
        usuario.BloqueadoAteUtc = null;
        await db.SaveChangesAsync(ct);
        return new(new UsuarioComSenha(await ResumoAsync(id, ct), senha));
    }

    /// <summary>
    /// O papel e a empresa como o token os leva: o admin não tem empresa (vê tudo) e tem o <see cref="Usuario.EhAdmin"/>,
    /// que o <c>EmissorDeToken</c> lê — os dois andam juntos para o papel do token nunca divergir do da lista.
    /// </summary>
    private static void Aplicar(Usuario usuario, string? nome, string papel, int? empresaId)
    {
        usuario.Nome = (nome ?? string.Empty).Trim();
        usuario.Papel = papel;
        usuario.EhAdmin = papel == Papeis.Admin;
        usuario.EmpresaId = usuario.EhAdmin ? null : empresaId;
    }

    private static string PapelDe(Usuario usuario) => usuario.EhAdmin ? Papeis.Admin : usuario.Papel;

    private async Task<bool> EmpresaInexistenteAsync(int? empresaId, CancellationToken ct)
        => empresaId is { } id && !await db.Empresas.AnyAsync(e => e.Id == id, ct);

    private async Task<UsuarioResumo> ResumoAsync(int id, CancellationToken ct)
        => Resumo(await db.Usuarios.AsNoTracking().Include(u => u.Empresa).FirstAsync(u => u.Id == id, ct), relogio.GetUtcNow().UtcDateTime);

    private static UsuarioResumo Resumo(Usuario u, DateTime agora)
        => new(u.Id, u.Login, u.Nome, PapelDe(u), u.EmpresaId, u.Empresa?.Nome, u.BloqueadoPeloAdmin,
            u.BloqueadoAteUtc is { } ate && ate > agora ? DevKitPageDb.Utc(ate) : null, u.DeveTrocarSenha, DevKitPageDb.Utc(u.CriadoEmUtc));
}

/// <summary>
/// O registro das máquinas: a chave nasce aqui e só o hash fica. É também onde a máquina ADERE a uma
/// empresa (US #381): o dev.kit só manda o código depois de o colaborador aceitar o aviso de coleta, e
/// muda de adesão registrando de novo.
/// </summary>
public sealed class Maquinas(DevKitPageDb db, TimeProvider relogio) : IMaquinas
{
    public async Task<RegistroDaMaquina?> RegistrarAsync(
        string maquinaId, string versaoDevKit, string? codigoEmpresa, string? colaborador, string? chaveAtual, bool peloAdmin, CancellationToken ct)
    {
        var id = (maquinaId ?? string.Empty).Trim();
        var (chave, hash) = ChaveDeMaquina.Gerar();
        var maquina = await db.Maquinas.FirstOrDefaultAsync(m => m.MaquinaId == id, ct);

        // A máquina que já existe: só ela mesma (a chave atual) ou o admin a registram de novo.
        if (maquina is not null && !peloAdmin
            && (string.IsNullOrWhiteSpace(chaveAtual) || !ChaveDeMaquina.Iguais(ChaveDeMaquina.Hash(chaveAtual.Trim()), maquina.ChaveHash)))
            return null;

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

        // Registrar de novo GIRA a chave (com a atual, ou pelo admin). O dev.kit que PERDEU a dele recebe
        // a recusa e se registra como máquina nova, com outro id anônimo.
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
