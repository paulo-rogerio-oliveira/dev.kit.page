namespace DevKitPage.Contracts.V1;

/// <summary>Evidências do ato exato. Campo opcional no retrato v1; sem ele não há aprovação remota.</summary>
public sealed record SnapshotDaDecisaoV1(
    string Origem, string Destino, string Sha, IReadOnlyList<NotaDaDecisaoV1> Notas,
    int TestesAprovados, int TestesFalhos, bool RegressaoConcluida, int Arquivos, int Commits,
    string Alteracoes, IReadOnlyList<CapturaDaDecisaoV1> Capturas);
public sealed record NotaDaDecisaoV1(string Avaliador, int Nota, int Minima);
public sealed record CapturaDaDecisaoV1(string Titulo, string Url);
/// <param name="Dispensada">
/// (campo novo, correções do board remoto) O dev fechou o card no site (<see cref="DecisoesV1.AcaoDispensar"/>): só a lista com
/// <c>?dispensadas=true</c> a traz. A task na máquina não muda.
/// </param>
public sealed record DecisaoPendenteV1(PendenciaNoRetratoV1 Pendencia, string SnapshotId, string? Bloqueio, RespostaDaDecisaoV1? Resposta, bool Dispensada = false);

/// <param name="SnapshotId">O snapshot revisado; dispensável só no <see cref="DecisoesV1.AcaoDispensar"/> e no <see cref="DecisoesV1.AcaoRestaurar"/>.</param>
/// <param name="Codigo">
/// (semana 7) OPCIONAL E IGNORADO: o aprovar e o autorizar valem com o login confirmado, sem o segundo fator. O campo fica no
/// V1 para o cliente antigo, que ainda o manda: o código recebido não é validado nem recusado (regra #2).
/// </param>
/// <param name="Dispositivo">
/// (controle-v4, item 3; campo novo e opcional, regra #2) De onde o dev decidiu: <see cref="ConversaV1.DispositivoCelular"/> ou
/// <see cref="ConversaV1.DispositivoNavegador"/>. Sem ele, a decisão vale como antes e a resposta vai sem o dispositivo.
/// </param>
public sealed record ResponderDecisaoV1(string SnapshotId, string Acao, string? Opcao = null, string? Codigo = null, string? Dispositivo = null);

/// <summary>A dispensa de uma pendência no site: ela some do painel (ou volta, na restauração). Não é decisão nem auditoria da task.</summary>
public sealed record DispensaDaPendenciaV1(string MaquinaId, string PendenciaId, bool Dispensada, DateTimeOffset Em);

/// <summary>As ações do site sobre uma pendência remota.</summary>
public static class DecisoesV1
{
    public const string AcaoResponder = "responder";
    public const string AcaoAprovar = "aprovar";
    public const string AcaoAjuste = "ajuste";

    /// <summary>(correções do board remoto) Fecha o card no site, por usuário, máquina e pendência. Só no site: a task não muda.</summary>
    public const string AcaoDispensar = "dispensar";

    /// <summary>Traz de volta o card dispensado.</summary>
    public const string AcaoRestaurar = "restaurar";

    /// <summary>
    /// (ajuste 9) A AUTORIZAÇÃO da pendência <see cref="RemotoV1.PendenciaAutorizacao"/>: com o login confirmado, como o aprovar
    /// (sem segundo fator desde a semana 7), e vale só para o escopo revisado (o id do snapshot inclui o escopo).
    /// </summary>
    public const string AcaoAutorizar = "autorizar";

    /// <summary>As ações aceitas no <c>POST .../decisoes/{pendencia}</c> (a nova entra no fim).</summary>
    public static IReadOnlyList<string> Acoes { get; } = [AcaoResponder, AcaoAprovar, AcaoAjuste, AcaoDispensar, AcaoRestaurar, AcaoAutorizar];

    public const string AtoConcluirProjeto = "concluir-projeto";
    public const string AtoIntegrarItem = "integrar-item";
    public const string AtoArquivarTasks = "arquivar-tasks";
    public const string AtoExecutarComando = "executar-comando";

    /// <summary>Os atos de uma autorização (o escopo do que o site autoriza).</summary>
    public static IReadOnlyList<string> AtosDaAutorizacao { get; } = [AtoConcluirProjeto, AtoIntegrarItem, AtoArquivarTasks, AtoExecutarComando];
}

/// <summary>
/// (ajuste 9) O ESCOPO EXATO de uma autorização: o ato (<see cref="DecisoesV1.AtosDaAutorizacao"/>), o alvo (o projeto, a task),
/// o SHA quando o ato é sobre um commit, os itens listados e o comando exato.
/// </summary>
public sealed record EscopoDaAutorizacaoV1(string Ato, string Alvo, string? Sha = null, IReadOnlyList<string>? Itens = null, string? Comando = null);
/// <param name="SegundoFator">
/// (semana 7) Só auditoria, e o cliente o ignora: verdadeiro só na decisão gravada quando o aprovar exigia o TOTP; a decisão
/// nova grava falso (não houve segundo fator: o login confirmado basta). Fica no V1 para o cliente antigo (regra #2).
/// </param>
/// <param name="Dispositivo">
/// (controle-v4, item 3; campo novo e opcional, regra #2) O dispositivo da decisão (<c>celular</c> ou <c>navegador</c>), para a
/// auditoria da máquina; nulo na decisão gravada antes do campo ou mandada sem ele. Fica no fim: o dev.kit antigo o ignora.
/// </param>
public sealed record RespostaDaDecisaoV1(long Id, string MaquinaId, string PendenciaId, string SnapshotId,
    string Acao, string? Opcao, string? Origem, string? Destino, string? Sha, DateTimeOffset Em, bool SegundoFator, string Estado,
    string? Dispositivo = null);
public sealed record PrepararAutenticadorV1(string Senha);
public sealed record ConfirmarAutenticadorV1(string Codigo);
public sealed record AutenticadorPreparadoV1(string Chave, string Uri, DateTimeOffset ExpiraEm);
public sealed record EstadoDoAutenticadorV1(bool Ativo);
