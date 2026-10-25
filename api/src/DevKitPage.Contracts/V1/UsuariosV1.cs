namespace DevKitPage.Contracts.V1;

/// <summary>Um usuário na lista do admin (US #405). A senha nunca sai daqui — nem o hash.</summary>
/// <param name="Nome">O nome de exibição (vazio no usuário anterior à gestão).</param>
/// <param name="Papel">Um de <see cref="Papeis"/>.</param>
/// <param name="EmpresaId">A empresa do gestor (obrigatória) ou do dev (opcional); nula para o admin.</param>
/// <param name="Bloqueado">Bloqueado pelo admin: não entra, nem com a senha certa, até ser desbloqueado.</param>
/// <param name="BloqueadoAte">O bloqueio TEMPORÁRIO por falhas seguidas de login, quando está valendo.</param>
/// <param name="DeveTrocarSenha">Ainda com a senha temporária: o primeiro acesso exige a troca.</param>
/// <param name="Licenca">A licença do dev.kit do usuário (US #687); nula só numa API anterior a ela.</param>
public sealed record UsuarioResumo(
    int Id, string Login, string Nome, string Papel, int? EmpresaId, string? Empresa, bool Bloqueado,
    DateTimeOffset? BloqueadoAte, bool DeveTrocarSenha, DateTimeOffset CriadoEm, LicencaDoUsuarioV1? Licenca = null);

/// <summary>Os tipos da licença de um usuário como a gestão os mostra e o admin os escolhe (US #687).</summary>
public static class TiposDaLicencaDoUsuario
{
    /// <summary>O teste: a validade curta de quem ainda está experimentando.</summary>
    public const string Trial = "trial";

    /// <summary>A licença do próprio usuário (a compra individual, ou a extensão dada a uma pessoa).</summary>
    public const string Individual = "individual";

    /// <summary>A licença do contrato da empresa: a validade é o "contrato até" dela.</summary>
    public const string Empresa = "empresa";

    /// <summary>A conta de administração: sem prazo, e sem o que definir.</summary>
    public const string Admin = "admin";

    /// <summary>Os tipos que o admin escolhe no painel da licença.</summary>
    public static IReadOnlyList<string> Escolhiveis { get; } = [Trial, Individual, Empresa];
}

/// <summary>A situação da licença de um usuário (US #687) — a cor da coluna Licença e o filtro.</summary>
public static class SituacoesDaLicenca
{
    /// <summary>Com validade, e a mais de <see cref="DiasDoAviso"/> dias do fim.</summary>
    public const string Valida = "valida";

    /// <summary>Vence em até <see cref="DiasDoAviso"/> dias (a regra dos 7 dias da renovação).</summary>
    public const string Vencendo = "vencendo";

    /// <summary>Passou da validade: a renovação é recusada e o usuário fica "Bloqueado: licença".</summary>
    public const string Vencida = "vencida";

    /// <summary>O admin revogou: a renovação é recusada até ele definir uma validade nova.</summary>
    public const string Revogada = "revogada";

    /// <summary>Sem data-limite (a conta de administração, ou o admin tirou o prazo).</summary>
    public const string SemPrazo = "sem-prazo";

    /// <summary>Ninguém definiu validade (nem o admin, nem o contrato da empresa): a renovação segue como antes.</summary>
    public const string Automatica = "automatica";

    /// <summary>A antecedência do aviso — o ciclo da renovação do dev.kit (o <c>AvisoDaLicenca</c> do git.kit usa o mesmo).</summary>
    public const int DiasDoAviso = 7;
}

/// <summary>A licença do dev.kit de um usuário na gestão do admin (US #687).</summary>
/// <param name="Tipo">Um de <see cref="TiposDaLicencaDoUsuario"/>.</param>
/// <param name="Situacao">Uma de <see cref="SituacoesDaLicenca"/>.</param>
/// <param name="ValidaAte">A validade que a renovação respeita; nula sem prazo, revogada ou automática.</param>
/// <param name="DiasRestantes">Os dias até a validade, por data (vence amanhã = 1); negativo depois de vencer.</param>
/// <param name="Inicio">O começo da licença: o cadastro do usuário.</param>
/// <param name="DefinidaPeloAdmin">A validade é a que o admin definiu para ESTE usuário (e não a do contrato da empresa).</param>
/// <param name="UltimoUso">A última renovação do dev.kit deste usuário (a trilha das licenças emitidas); nula sem nenhuma.</param>
public sealed record LicencaDoUsuarioV1(
    string Tipo, string Situacao, DateTimeOffset? ValidaAte, int? DiasRestantes, DateTimeOffset Inicio, bool DefinidaPeloAdmin, DateTimeOffset? UltimoUso);

/// <summary>Como o admin define a validade (US #687).</summary>
public static class ModosDaValidade
{
    /// <summary>Soma <see cref="DefinicaoDeLicencaV1.Dias"/> à validade atual (ou a hoje, quando ela já passou ou não há).</summary>
    public const string Somar = "somar";

    /// <summary>Até a data <see cref="DefinicaoDeLicencaV1.Ate"/> (inclusive: vale até o fim do dia).</summary>
    public const string AteData = "ate-data";

    /// <summary>Sem prazo: a renovação segue sem data-limite.</summary>
    public const string SemPrazo = "sem-prazo";

    public static IReadOnlyList<string> Todos { get; } = [Somar, AteData, SemPrazo];
}

/// <summary>A validade que o admin define (o Aplicar validade, e a prévia dele).</summary>
/// <param name="Modo">Um de <see cref="ModosDaValidade"/>.</param>
/// <param name="Dias">Os dias a somar (no <see cref="ModosDaValidade.Somar"/>).</param>
/// <param name="Ate">A data-limite (no <see cref="ModosDaValidade.AteData"/>).</param>
/// <param name="Tipo">Um de <see cref="TiposDaLicencaDoUsuario.Escolhiveis"/>; nulo mantém o atual.</param>
/// <param name="Motivo">O porquê — obrigatório ao aplicar: vai para o histórico.</param>
public sealed record DefinicaoDeLicencaV1(string Modo, int? Dias = null, DateOnly? Ate = null, string? Tipo = null, string? Motivo = null);

/// <summary>A prévia da validade nova, antes de aplicar ("Nova validade: 27/11/2026 (33 dias a partir de hoje)").</summary>
public sealed record PreviaDeLicencaV1(DateTimeOffset? ValidaAte, bool SemPrazo, int? DiasAPartirDeHoje, string Tipo, string Mensagem);

/// <summary>A revogação da licença: o motivo vai para o histórico.</summary>
public sealed record RevogacaoDeLicencaV1(string? Motivo);

/// <summary>Uma linha do histórico da licença: a ação do admin (com o motivo) ou a renovação do dev.kit.</summary>
public sealed record HistoricoDeLicencaV1(DateTimeOffset Em, string Acao, string Descricao, string Motivo, string Autor);

/// <summary>As ações do histórico da licença.</summary>
public static class AcoesNaLicenca
{
    public const string Definida = "definida";
    public const string SemPrazo = "sem-prazo";
    public const string Revogada = "revogada";

    /// <summary>A renovação do dev.kit (a linha vem das licenças emitidas, e não de uma ação do admin).</summary>
    public const string Renovada = "renovada";

    /// <summary>O cadastro do usuário: o começo da licença.</summary>
    public const string Criada = "criada";
}

/// <summary>O painel "Licença de &lt;usuário&gt;": a licença de agora e o histórico, do mais novo ao mais antigo.</summary>
public sealed record DetalheDaLicencaV1(int UsuarioId, string Login, string Nome, string Papel, string? Empresa, LicencaDoUsuarioV1 Licenca, IReadOnlyList<HistoricoDeLicencaV1> Historico);

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
