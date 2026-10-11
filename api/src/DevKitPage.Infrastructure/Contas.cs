using System.Net.Mail;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// Os assentos da empresa (US #425): contam por USUÁRIO — os devs dela que não estão bloqueados, na ordem em que
/// entraram. Bloquear libera o assento; a corrida pelo último fica com quem entrou primeiro (o id, no empate), a mesma
/// regra das máquinas na adesão à telemetria.
/// </summary>
internal static class Assentos
{
    public static IQueryable<Usuario> Ocupantes(DevKitPageDb db, int empresaId)
        => db.Usuarios.Where(u => u.EmpresaId == empresaId && u.Papel == Papeis.Dev && !u.BloqueadoPeloAdmin);

    public static Task<int> OcupadosAsync(DevKitPageDb db, int empresaId, CancellationToken ct) => Ocupantes(db, empresaId).CountAsync(ct);

    /// <summary>O dev está entre os N primeiros da empresa? Quem não é dev (ou não tem empresa) não ocupa assento: sempre dentro.</summary>
    public static async Task<bool> DentroAsync(DevKitPageDb db, Usuario usuario, CancellationToken ct)
    {
        if (usuario.Papel != Papeis.Dev || usuario.EmpresaId is not { } empresaId)
            return true;
        var assentos = await db.Empresas.Where(e => e.Id == empresaId).Select(e => e.Assentos).FirstOrDefaultAsync(ct);
        var primeiros = await Ocupantes(db, empresaId).OrderBy(u => u.CriadoEmUtc).ThenBy(u => u.Id).Take(assentos).Select(u => u.Id).ToListAsync(ct);
        return primeiros.Contains(usuario.Id);
    }
}

/// <summary>
/// A conta do dev (US #425): a adesão por código e e-mail, o link de definir a senha (uso único, 24 h, só o hash no
/// banco) e a conta de quem está logado. O link é a ÚNICA forma de o dev ganhar senha — nem a API, nem o gestor a
/// conhecem.
/// </summary>
public sealed class Contas(
    DevKitPageDb db, IPasswordHasher<Usuario> hasher, IEnvioDeEmail email, IOptions<OpcoesDaConta> conta, IOptions<OpcoesDeLogin> login,
    IOptions<OpcoesDePagamento> pagamento, TimeProvider relogio) : IContas
{
    public async Task<ResultadoDaGestao<AdesaoFeitaV1>> AderirAsync(AdesaoV1 adesao, CancellationToken ct)
    {
        var codigo = CodigoDeAdesao.Normalizar(adesao.Codigo);
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.CodigoDeAdesao == codigo, ct);
        if (empresa is null)
            return ResultadoDaGestao<AdesaoFeitaV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Código de adesão não reconhecido. Confira o código com o gestor.");

        var endereco = RegrasDaConta.Email(adesao.Email);
        var existente = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == endereco || u.Login == endereco, ct);
        if (existente is not null)
        {
            // Um e-mail é de UMA empresa: o de outra é recusado, e o da mesma só recebe um link novo.
            if (existente.EmpresaId != empresa.Id)
                return ResultadoDaGestao<AdesaoFeitaV1>.Falhou(FalhaNaGestao.Conflito, "Este e-mail já está cadastrado em outra empresa. Fale com o gestor dela.");
            await EnviarLinkAsync(existente, "Defina a sua senha do dev.kit", ct);
            return new(new AdesaoFeitaV1($"Você já faz parte de {empresa.Nome}: enviamos um link novo para {endereco} definir a senha."));
        }

        if (await Assentos.OcupadosAsync(db, empresa.Id, ct) >= empresa.Assentos)
            return SemAssento(empresa);

        var usuario = new Usuario
        {
            Login = endereco,
            Email = endereco,
            Nome = (adesao.Nome ?? string.Empty).Trim(),
            Papel = Papeis.Dev,
            EmpresaId = empresa.Id,
            CriadoEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        // Uma senha que ninguém conhece: a conta só abre pelo link do e-mail.
        usuario.SenhaHash = hasher.HashPassword(usuario, GeradorDeSenha.Temporaria() + Guid.NewGuid().ToString("N"));
        db.Usuarios.Add(usuario);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duas adesões do mesmo e-mail ao mesmo tempo: o índice único do login decide.
            db.Entry(usuario).State = EntityState.Detached;
            return ResultadoDaGestao<AdesaoFeitaV1>.Falhou(FalhaNaGestao.Conflito, "Este e-mail acabou de ser cadastrado. Peça um link novo para definir a senha.");
        }

        // O último assento disputado: as duas adesões passaram na contagem; fica quem entrou primeiro.
        if (!await Assentos.DentroAsync(db, usuario, ct))
        {
            db.Usuarios.Remove(usuario);
            await db.SaveChangesAsync(ct);
            return SemAssento(empresa);
        }

        await EnviarLinkAsync(usuario, "Defina a sua senha do dev.kit", ct);
        return new(new AdesaoFeitaV1($"Pronto: enviamos para {endereco} o link para definir a senha (vale {Horas} horas)."));
    }

    public async Task PedirLinkAsync(string endereco, CancellationToken ct)
    {
        var limpo = RegrasDaConta.Email(endereco);
        if (limpo.Length == 0)
            return;
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == limpo, ct);
        if (usuario is not null && !usuario.BloqueadoPeloAdmin)
            await EnviarLinkAsync(usuario, "Link para definir a sua senha do dev.kit", ct);
    }

    public async Task EnviarLinkAsync(Usuario usuario, string assunto, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        // Uso único também entre links: o novo invalida os anteriores que ainda não foram usados.
        await db.LinksDeSenha.Where(l => l.UsuarioId == usuario.Id && l.UsadoEmUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.UsadoEmUtc, agora), ct);

        var (token, hash) = RegrasDaConta.TokenDoLink();
        db.LinksDeSenha.Add(new LinkDeSenha { UsuarioId = usuario.Id, TokenHash = hash, CriadoEmUtc = agora, ExpiraEmUtc = agora.AddHours(Horas) });
        await db.SaveChangesAsync(ct);

        var link = RegrasDaConta.Link(conta.Value.UrlDoSite, token);
        await email.EnviarAsync(new MensagemDeEmail(
            usuario.Email.Length > 0 ? usuario.Email : usuario.Login,
            assunto,
            $"""
            Olá{(usuario.Nome.Length > 0 ? ", " + usuario.Nome : string.Empty)}!

            Para definir a sua senha do dev.kit, abra o link abaixo. Ele vale {Horas} horas e pode ser usado uma vez só:

            {link}

            Depois, entre no dev.kit com o login {usuario.Login}. Se você não pediu isto, ignore este e-mail.
            """), ct);
    }

    public async Task<string> DefinirSenhaAsync(string token, string novaSenha, CancellationToken ct)
    {
        var hash = ChaveDeMaquina.Hash((token ?? string.Empty).Trim());
        var link = await db.LinksDeSenha.Include(l => l.Usuario).FirstOrDefaultAsync(l => l.TokenHash == hash, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (link?.Usuario is null)
            return "Link inválido. Peça um link novo.";
        if (link.UsadoEmUtc is not null)
            return "Este link já foi usado. Peça um link novo.";
        if (link.ExpiraEmUtc <= agora)
            return $"Este link expirou (ele vale {Horas} horas). Peça um link novo.";
        var problema = PoliticaDeSenha.Validar(novaSenha);
        if (problema.Length > 0)
            return problema;

        // O uso único é do BANCO: de dois pedidos ao mesmo tempo com o mesmo link, só um marca o uso.
        if (await db.LinksDeSenha.Where(l => l.Id == link.Id && l.UsadoEmUtc == null).ExecuteUpdateAsync(s => s.SetProperty(l => l.UsadoEmUtc, agora), ct) == 0)
            return "Este link já foi usado. Peça um link novo.";

        var usuario = link.Usuario;
        usuario.SenhaHash = hasher.HashPassword(usuario, novaSenha);
        usuario.DeveTrocarSenha = false;
        usuario.FalhasSeguidas = 0;
        usuario.BloqueadoAteUtc = null;
        await db.SaveChangesAsync(ct);
        return string.Empty;
    }

    public async Task<ContaV1?> ContaAsync(int usuarioId, CancellationToken ct)
    {
        var usuario = await db.Usuarios.AsNoTracking().Include(u => u.Empresa).FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null)
            return null;
        var empresa = usuario.Empresa;
        var individual = empresa?.Individual == true;
        var portal = pagamento.Value.UrlDoPortal;
        return new ContaV1(
            usuario.Login, usuario.Nome, usuario.Email, usuario.EhAdmin ? Papeis.Admin : usuario.Papel, empresa?.Nome, individual,
            empresa is not null && empresa.AssinaturaCanceladaEmUtc is null,
            individual && portal.Length > 0 ? portal : null,
            RegrasDaConta.LoginObrigatorio(empresa?.LoginObrigatorio, login.Value.Obrigatorio));
    }

    public async Task<bool> LoginObrigatorioAsync(int? empresaId, CancellationToken ct)
    {
        var daEmpresa = empresaId is { } id ? await db.Empresas.Where(e => e.Id == id).Select(e => e.LoginObrigatorio).FirstOrDefaultAsync(ct) : null;
        return RegrasDaConta.LoginObrigatorio(daEmpresa, login.Value.Obrigatorio);
    }

    private int Horas => Math.Max(1, conta.Value.ValidadeDoLinkHoras);

    private static ResultadoDaGestao<AdesaoFeitaV1> SemAssento(Empresa empresa)
        => ResultadoDaGestao<AdesaoFeitaV1>.Falhou(FalhaNaGestao.Conflito, $"A empresa {empresa.Nome} não tem assento livre. Fale com o gestor.");
}

/// <summary>
/// A empresa administrada pelo gestor (US #425): os usuários dela, o bloqueio (que libera o assento), o código de
/// adesão novo e a política de login. O <see cref="EscopoDaEmpresa"/> é aplicado AQUI, num ponto só: o usuário de
/// outra empresa é proibido, e só o gestor administra.
/// </summary>
public sealed class GestaoDaEmpresa(DevKitPageDb db, IOptions<OpcoesDeLogin> login) : IGestaoDaEmpresa
{
    public async Task<EmpresaDoGestorV1?> EmpresaAsync(EscopoDaEmpresa escopo, CancellationToken ct)
    {
        var empresa = await db.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == escopo.EmpresaId, ct);
        return empresa is null ? null : await ResumoAsync(empresa, ct);
    }

    public async Task<IReadOnlyList<UsuarioDaEmpresaV1>> UsuariosAsync(EscopoDaEmpresa escopo, CancellationToken ct)
        => (await db.Usuarios.AsNoTracking().Where(u => u.EmpresaId == escopo.EmpresaId).OrderBy(u => u.Login).ToListAsync(ct))
            .Select(Resumo)
            .ToArray();

    public async Task<ResultadoDaGestao<UsuarioDaEmpresaV1>> BloquearAsync(EscopoDaEmpresa escopo, int usuarioId, bool bloqueado, CancellationToken ct)
    {
        if (!escopo.EhGestor)
            return ResultadoDaGestao<UsuarioDaEmpresaV1>.Falhou(FalhaNaGestao.Proibida, "Só o gestor administra os usuários da empresa.");
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId, ct);
        if (usuario is null)
            return ResultadoDaGestao<UsuarioDaEmpresaV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Usuário não encontrado.");
        if (usuario.EmpresaId != escopo.EmpresaId)
            return ResultadoDaGestao<UsuarioDaEmpresaV1>.Falhou(FalhaNaGestao.Proibida, "O usuário é de outra empresa.");
        if (usuario.Id == escopo.UsuarioId && bloqueado)
            return ResultadoDaGestao<UsuarioDaEmpresaV1>.Falhou(FalhaNaGestao.Recusada, "Você não pode bloquear a si mesmo.");

        if (!bloqueado && usuario.BloqueadoPeloAdmin && usuario.Papel == Papeis.Dev)
        {
            // Desbloquear volta a ocupar um assento: sem assento livre, o desbloqueio espera um.
            var assentos = await db.Empresas.Where(e => e.Id == escopo.EmpresaId).Select(e => e.Assentos).FirstAsync(ct);
            if (await Assentos.OcupadosAsync(db, escopo.EmpresaId, ct) >= assentos)
                return ResultadoDaGestao<UsuarioDaEmpresaV1>.Falhou(FalhaNaGestao.Conflito, "Não há assento livre: bloqueie outro usuário (ou contrate mais assentos) antes de desbloquear este.");
        }

        usuario.BloqueadoPeloAdmin = bloqueado;
        if (!bloqueado)
        {
            usuario.FalhasSeguidas = 0;
            usuario.BloqueadoAteUtc = null;
        }

        await db.SaveChangesAsync(ct);
        return new(Resumo(usuario));
    }

    public async Task<CodigoDeAdesaoV1> NovoCodigoAsync(EscopoDaEmpresa escopo, CancellationToken ct)
    {
        var empresa = await db.Empresas.FirstAsync(e => e.Id == escopo.EmpresaId, ct);
        var codigo = CodigoDeAdesao.Gerar();
        while (await db.Empresas.AnyAsync(e => e.CodigoDeAdesao == codigo, ct))
            codigo = CodigoDeAdesao.Gerar();
        empresa.CodigoDeAdesao = codigo;
        await db.SaveChangesAsync(ct);
        return new CodigoDeAdesaoV1(codigo);
    }

    public async Task<EmpresaDoGestorV1> DefinirPoliticaAsync(EscopoDaEmpresa escopo, bool? loginObrigatorio, CancellationToken ct)
    {
        var empresa = await db.Empresas.FirstAsync(e => e.Id == escopo.EmpresaId, ct);
        empresa.LoginObrigatorio = loginObrigatorio;
        await db.SaveChangesAsync(ct);
        return await ResumoAsync(empresa, ct);
    }

    private async Task<EmpresaDoGestorV1> ResumoAsync(Empresa empresa, CancellationToken ct)
        => new(empresa.Id, empresa.Nome, empresa.Plano, empresa.Assentos, await Assentos.OcupadosAsync(db, empresa.Id, ct), empresa.CodigoDeAdesao,
            empresa.LoginObrigatorio, RegrasDaConta.LoginObrigatorio(empresa.LoginObrigatorio, login.Value.Obrigatorio));

    private static UsuarioDaEmpresaV1 Resumo(Usuario u)
        => new(u.Id, u.Login, u.Nome, u.Email, u.EhAdmin ? Papeis.Admin : u.Papel, u.BloqueadoPeloAdmin, DevKitPageDb.Utc(u.CriadoEmUtc));
}

/// <summary>
/// O envio de e-mail configurável (US #425): por SMTP quando <see cref="OpcoesDeEmail.Host"/> está configurado; sem
/// ele, a mensagem vai para o log — o desenvolvimento local vê o link sem um servidor de e-mail. A falha do SMTP é
/// registrada e não derruba a operação que pediu o envio (o link pode ser pedido de novo).
/// </summary>
public sealed class EnvioDeEmail(IOptions<OpcoesDeEmail> opcoes, ILogger<EnvioDeEmail> log) : IEnvioDeEmail
{
    public async Task EnviarAsync(MensagemDeEmail mensagem, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        if (string.IsNullOrWhiteSpace(cfg.Host))
        {
            log.LogWarning("E-mail não enviado (Email:Host não configurado) para {Para}: {Assunto}\n{Texto}", mensagem.Para, mensagem.Assunto, mensagem.Texto);
            return;
        }

        try
        {
            using var smtp = new SmtpClient(cfg.Host, cfg.Porta) { EnableSsl = cfg.Ssl };
            if (cfg.Usuario.Length > 0)
                smtp.Credentials = new System.Net.NetworkCredential(cfg.Usuario, cfg.Senha);
            using var carta = new MailMessage(cfg.Remetente, mensagem.Para, mensagem.Assunto, mensagem.Texto);
            await smtp.SendMailAsync(carta, ct);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            log.LogError("Falha ao enviar o e-mail '{Assunto}' ({Tipo}).", mensagem.Assunto, ex.GetType().Name);
        }
    }
}
