namespace DevKitPage.Contracts.V1;

/// <summary>Um usuário na lista do admin (US #405). A senha nunca sai daqui — nem o hash.</summary>
/// <param name="Nome">O nome de exibição (vazio no usuário anterior à gestão).</param>
/// <param name="Papel">Um de <see cref="Papeis"/>.</param>
/// <param name="EmpresaId">A empresa do gestor (obrigatória) ou do dev (opcional); nula para o admin.</param>
/// <param name="Bloqueado">Bloqueado pelo admin: não entra, nem com a senha certa, até ser desbloqueado.</param>
/// <param name="BloqueadoAte">O bloqueio TEMPORÁRIO por falhas seguidas de login, quando está valendo.</param>
/// <param name="DeveTrocarSenha">Ainda com a senha temporária: o primeiro acesso exige a troca.</param>
public sealed record UsuarioResumo(
    int Id, string Login, string Nome, string Papel, int? EmpresaId, string? Empresa, bool Bloqueado,
    DateTimeOffset? BloqueadoAte, bool DeveTrocarSenha, DateTimeOffset CriadoEm);

/// <summary>O usuário novo: a senha não vem daqui — a API gera uma temporária.</summary>
public sealed record UsuarioNovo(string Login, string Nome, string Papel, int? EmpresaId);

/// <summary>A edição do usuário pelo admin: o login não muda (é a identidade da trilha de auditoria).</summary>
public sealed record UsuarioEditado(string Nome, string Papel, int? EmpresaId);

/// <summary>Bloquear (<c>true</c>) ou desbloquear (<c>false</c>) o usuário.</summary>
public sealed record BloqueioDoUsuario(bool Bloqueado);

/// <summary>
/// O usuário com a senha TEMPORÁRIA — a resposta da criação e da redefinição, e só delas: a senha é
/// devolvida UMA vez (o admin a repassa), e o primeiro acesso exige a troca.
/// </summary>
public sealed record UsuarioComSenha(UsuarioResumo Usuario, string SenhaTemporaria);

/// <summary>
/// A política de login do app (US #405): com <see cref="LoginObrigatorio"/>, o dev.kit pede o login antes
/// de abrir. Lida em <c>GET /api/auth/politica</c> (que não depende do GitHub) e em <c>GET /api/versoes/ultima</c>.
/// </summary>
public sealed record PoliticaDeLoginV1(bool LoginObrigatorio);
