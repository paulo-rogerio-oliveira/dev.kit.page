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
public sealed record ResponderDecisaoV1(string SnapshotId, string Acao, string? Opcao = null, string? Codigo = null);

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

    /// <summary>As ações aceitas no <c>POST .../decisoes/{pendencia}</c> (a nova entra no fim).</summary>
    public static IReadOnlyList<string> Acoes { get; } = [AcaoResponder, AcaoAprovar, AcaoAjuste, AcaoDispensar, AcaoRestaurar];
}
public sealed record RespostaDaDecisaoV1(long Id, string MaquinaId, string PendenciaId, string SnapshotId,
    string Acao, string? Opcao, string? Origem, string? Destino, string? Sha, DateTimeOffset Em, bool SegundoFator, string Estado);
public sealed record PrepararAutenticadorV1(string Senha);
public sealed record ConfirmarAutenticadorV1(string Codigo);
public sealed record AutenticadorPreparadoV1(string Chave, string Uri, DateTimeOffset ExpiraEm);
public sealed record EstadoDoAutenticadorV1(bool Ativo);
