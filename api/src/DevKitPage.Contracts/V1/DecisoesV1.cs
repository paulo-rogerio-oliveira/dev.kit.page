namespace DevKitPage.Contracts.V1;

/// <summary>Evidências do ato exato. Campo opcional no retrato v1; sem ele não há aprovação remota.</summary>
public sealed record SnapshotDaDecisaoV1(
    string Origem, string Destino, string Sha, IReadOnlyList<NotaDaDecisaoV1> Notas,
    int TestesAprovados, int TestesFalhos, bool RegressaoConcluida, int Arquivos, int Commits,
    string Alteracoes, IReadOnlyList<CapturaDaDecisaoV1> Capturas);
public sealed record NotaDaDecisaoV1(string Avaliador, int Nota, int Minima);
public sealed record CapturaDaDecisaoV1(string Titulo, string Url);
public sealed record DecisaoPendenteV1(PendenciaNoRetratoV1 Pendencia, string SnapshotId, string? Bloqueio, RespostaDaDecisaoV1? Resposta);
public sealed record ResponderDecisaoV1(string SnapshotId, string Acao, string? Opcao = null, string? Codigo = null);
public sealed record RespostaDaDecisaoV1(long Id, string MaquinaId, string PendenciaId, string SnapshotId,
    string Acao, string? Opcao, string? Origem, string? Destino, string? Sha, DateTimeOffset Em, bool SegundoFator, string Estado);
public sealed record PrepararAutenticadorV1(string Senha);
public sealed record ConfirmarAutenticadorV1(string Codigo);
public sealed record AutenticadorPreparadoV1(string Chave, string Uri, DateTimeOffset ExpiraEm);
public sealed record EstadoDoAutenticadorV1(bool Ativo);
