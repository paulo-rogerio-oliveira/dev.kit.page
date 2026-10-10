namespace DevKitPage.Contracts.V1;

/// <summary>
/// Uma avaliação (o joinha) de um turno do agente (US #417), como a ingestão a guardou: a MAIS RECENTE de
/// cada (máquina, sessão, turno). É o item de <c>GET /api/dashboard/feedback</c>, numa
/// <see cref="Pagina{T}"/> — a mesma paginação do log de eventos.
/// </summary>
/// <param name="Em">O <c>em</c> do evento que trouxe a avaliação (UTC).</param>
/// <param name="Maquina">O id interno da máquina (o do filtro <c>maquina</c>).</param>
/// <param name="SessaoId">A sessão (task) do dev.kit — um id interno, sem pasta nem nome.</param>
/// <param name="Turno">O número do turno avaliado na sessão.</param>
/// <param name="Boa">O joinha: verdadeiro = positivo.</param>
/// <param name="Motivo">O motivo, da lista fechada; vazio na boa sem motivo.</param>
/// <param name="Agente">O agente (CLI); vazio quando o dev.kit não o mandou.</param>
/// <param name="Modelo">O modelo; vazio quando o dev.kit não o mandou.</param>
/// <param name="Fluxo">O fluxo; vazio na task sem fluxo.</param>
/// <param name="WorkItem">O work item da task, quando há.</param>
public sealed record FeedbackV1(
    DateTimeOffset Em, int Maquina, string SessaoId, int Turno, bool Boa, string Motivo, string Agente, string Modelo, string Fluxo, int? WorkItem);

/// <summary>
/// As métricas do feedback no período e no escopo (US #417) — o que <c>GET /api/dashboard/feedback/metricas</c>
/// devolve. Base vazia é tudo zero com as listas vazias, nunca erro.
/// </summary>
/// <param name="De">O primeiro dia do período (inclusive).</param>
/// <param name="Ate">O último dia do período (inclusive).</param>
/// <param name="Total">As avaliações (uma por turno) no período.</param>
/// <param name="Positivos">Delas, as boas.</param>
/// <param name="Negativos">Delas, as ruins.</param>
/// <param name="PorFluxo">Por fluxo, do mais avaliado ao menos; o vazio vem como <see cref="ChavesDoFeedback.SemFluxo"/>.</param>
/// <param name="PorAgente">Por agente, do mais avaliado ao menos; o vazio vem como <see cref="ChavesDoFeedback.SemAgente"/>.</param>
/// <param name="PorDia">Por dia (UTC) do <c>em</c>, em ordem; só os dias com avaliação.</param>
public sealed record MetricasDeFeedbackV1(
    DateOnly De, DateOnly Ate, int Total, int Positivos, int Negativos,
    IReadOnlyList<FeedbackPorChaveV1> PorFluxo, IReadOnlyList<FeedbackPorChaveV1> PorAgente, IReadOnlyList<FeedbackPorDiaV1> PorDia);

/// <summary>O joinha agregado por um recorte (o fluxo ou o agente).</summary>
public sealed record FeedbackPorChaveV1(string Chave, int Total, int Positivos, int Negativos);

/// <summary>O joinha agregado num dia.</summary>
public sealed record FeedbackPorDiaV1(DateOnly Dia, int Positivos, int Negativos);

/// <summary>
/// As chaves das métricas quando o recorte veio vazio (a task sem fluxo, o dev.kit que não soube o agente):
/// elas agrupam as avaliações sem o recorte, em vez de uma chave em branco na tela.
/// </summary>
public static class ChavesDoFeedback
{
    public const string SemFluxo = "(sem fluxo)";
    public const string SemAgente = "(sem agente)";
}
