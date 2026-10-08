namespace DevKitPage.Contracts.V1;

/// <summary>O login do usuário do dashboard.</summary>
public sealed record LoginRequest(string Login, string Senha);

/// <summary>O token e o que a tela precisa saber dele.</summary>
/// <param name="Token">O JWT (Bearer).</param>
/// <param name="ExpiraEm">Quando ele vence — a web volta ao login nesse momento.</param>
/// <param name="DeveTrocarSenha">O usuário só acessa a troca de senha enquanto isto for verdade.</param>
/// <param name="Papel">Um de <see cref="Papeis"/> (US #381): o gestor vê só a empresa dele.</param>
/// <param name="Empresa">O nome da empresa do gestor; nulo para o admin.</param>
public sealed record LoginResponse(
    string Token, DateTimeOffset ExpiraEm, bool DeveTrocarSenha, string Login, bool EhAdmin, string Papel = Papeis.Admin, string? Empresa = null);

/// <summary>A troca de senha (obrigatória no primeiro acesso do admin).</summary>
public sealed record TrocarSenhaRequest(string SenhaAtual, string NovaSenha);

/// <summary>Uma máquina que envia telemetria.</summary>
/// <param name="Id">O id interno — é o filtro das outras consultas.</param>
/// <param name="MaquinaId">O id anônimo que o dev.kit gerou.</param>
/// <param name="Apelido">Como a tela a mostra.</param>
public sealed record MaquinaResumo(
    int Id, string MaquinaId, string Apelido, string VersaoDevKit, DateTimeOffset RegistradaEm, DateTimeOffset? UltimoEnvioEm, long Eventos);

/// <summary>Um dia da série de quantidade.</summary>
public sealed record DiaDeUso(
    DateOnly Dia, long Sessoes, long Turnos, long Fluxos, long Ferramentas, long ComandosDelegados, long ArquivosAlterados);

/// <summary>A QUANTIDADE de uso no período (e por máquina, quando filtrada).</summary>
/// <param name="MaquinasAtivas">Máquinas distintas com algum evento no período (US #381), contadas nos totais diários.</param>
/// <param name="MaquinasRegistradas">Máquinas registradas até o fim do período (US #381), no escopo de quem consulta.</param>
public sealed record QuantidadeResposta(
    DateOnly De, DateOnly Ate, long Sessoes, long Turnos, long Fluxos, long Ferramentas, long ComandosDelegados,
    long ArquivosAlterados, IReadOnlyList<DiaDeUso> SerieDiaria, long MaquinasAtivas = 0, long MaquinasRegistradas = 0);

/// <summary>Uma causa de falha de turno (as de <c>TurnFailures</c> do dev.kit) e quantas vezes.</summary>
public sealed record CausaDeFalha(string Causa, long Quantidade);

/// <summary>
/// A QUALIDADE de uso no período. As taxas são nulas quando não há denominador (nenhum turno,
/// nenhuma nota, nenhum objetivo) — a tela mostra um traço, nunca NaN.
/// </summary>
/// <param name="TaxaDeFalha">Turnos com falha sobre turnos executados.</param>
/// <param name="NotaMedia">A média das notas dos avaliadores (0–100).</param>
/// <param name="RazaoCumpridosRecusados">Objetivos cumpridos por recusado.</param>
/// <param name="TurnosPorObjetivoCumprido">O retrabalho: quantos turnos custou cada objetivo aceito.</param>
public sealed record QualidadeResposta(
    DateOnly De, DateOnly Ate, long Turnos, long TurnosComFalha, double? TaxaDeFalha, double? DuracaoMediaDoTurnoMs,
    IReadOnlyList<CausaDeFalha> FalhasPorCausa, long Avaliacoes, double? NotaMedia, long ObjetivosCumpridos,
    long ObjetivosRecusados, double? RazaoCumpridosRecusados, double? TurnosPorObjetivoCumprido);

/// <summary>Uma linha do log de eventos.</summary>
public sealed record EventoDoLog(
    string EventId, int MaquinaId, string Apelido, string Tipo, string SessaoId, int Quantidade, long? Valor, string Detalhe, DateTimeOffset Em);

/// <summary>Uma página de resultados.</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Itens, int Total, int NumeroDaPagina, int Tamanho);

/// <summary>
/// Os estados de um grupo de exceção não classificada (US #381) — o ciclo de reação, no padrão do
/// Sentry. <see cref="Regrediu"/> só o servidor põe: o grupo resolvido voltou numa versão igual ou
/// maior que a indicada.
/// </summary>
public static class EstadosDoGrupo
{
    public const string Novo = "Novo";
    public const string Visto = "Visto";
    public const string Resolvido = "Resolvido";
    public const string Ignorado = "Ignorado";
    public const string Regrediu = "Regrediu";

    /// <summary>Os estados que quem reage escolhe (o <see cref="Regrediu"/> fica de fora).</summary>
    public static IReadOnlySet<string> Escolhiveis { get; } = new HashSet<string>(StringComparer.Ordinal) { Novo, Visto, Resolvido, Ignorado };
}

/// <summary>Um grupo de exceção não classificada: as ocorrências da mesma assinatura.</summary>
/// <param name="Id">O id interno — o das rotas de detalhe, estado e exportação.</param>
/// <param name="Assinatura">A impressão digital que o dev.kit calculou (tipo e quadros de código, sem linha).</param>
/// <param name="Tipo">O tipo da exceção (a primeira linha do trace).</param>
/// <param name="Estado">Um de <see cref="EstadosDoGrupo"/>.</param>
/// <param name="ResolvidoNaVersao">A versão em que se espera que pare — a base da regressão.</param>
/// <param name="Ocorrencias">Ocorrências no período, no escopo de quem consulta.</param>
/// <param name="Maquinas">Máquinas distintas afetadas no período.</param>
public sealed record GrupoDeErroResumo(
    long Id, string Assinatura, string Tipo, string Estado, string? ResolvidoNaVersao, long Ocorrencias, int Maquinas,
    string PrimeiraVersao, string UltimaVersao, DateTimeOffset PrimeiroVistoEm, DateTimeOffset UltimoVistoEm);

/// <summary>Uma ocorrência guardada de um grupo (as últimas de cada um), com o trace dela.</summary>
public sealed record OcorrenciaDeErroResumo(string EventId, string Apelido, string VersaoDevKit, string Trace, DateTimeOffset Em);

/// <summary>Quantas ocorrências num dia — a linha do tempo do grupo.</summary>
public sealed record OcorrenciasNoDia(DateOnly Dia, long Quantidade);

/// <summary>
/// O detalhe de um grupo — é também o que a exportação em JSON devolve (o dado para a reação):
/// o trace mais recente, as ocorrências guardadas, a linha do tempo, as versões e as máquinas.
/// </summary>
public sealed record GrupoDeErroDetalhe(
    GrupoDeErroResumo Grupo, string Trace, IReadOnlyList<OcorrenciaDeErroResumo> Ocorrencias,
    IReadOnlyList<OcorrenciasNoDia> PorDia, IReadOnlyList<string> Versoes, IReadOnlyList<string> Maquinas);

/// <summary>A reação a um grupo: o estado novo e, ao resolver, a versão da correção.</summary>
public sealed record AlterarEstadoDoGrupo(string Estado, string? Versao);

/// <summary>
/// O ROI de um work item numa máquina (US #387): a foto MAIS RECENTE que o dev.kit dela mandou. O mesmo
/// item calculado em duas máquinas são duas linhas — cada uma é o trabalho daquela máquina.
/// </summary>
/// <param name="MaquinaId">O id interno da máquina (o do filtro).</param>
/// <param name="Apelido">O apelido da máquina.</param>
/// <param name="Colaborador">O nome que o colaborador informou no dev.kit (vazio na máquina anônima).</param>
/// <param name="HorasPorTurno">Horas sobre turnos do agente; nulo sem turno (a tela mostra um traço).</param>
/// <param name="LeadTimeDias">Da criação ao encerramento; nulo enquanto o item está aberto.</param>
/// <param name="AtualizadoEm">O <c>em</c> do evento que trouxe a foto — quando o dev.kit calculou.</param>
public sealed record RoiDoWorkItem(
    int MaquinaId, string Apelido, string Colaborador, int WorkItem, string Tipo, string Estado, DateOnly De, DateOnly Ate,
    int TurnosDoAgente, int Sessoes, decimal Horas, decimal HorasNoBoard, decimal? HorasNoTimesheet, decimal? HorasPorTurno,
    double? LeadTimeDias, bool Aberto, int PullRequests, int PullRequestsMergeadas, DateTimeOffset AtualizadoEm);

/// <summary>Os totais do ROI no período e no escopo (de TODAS as fotos, e não só das devolvidas na lista).</summary>
/// <param name="Itens">Fotos (máquina, work item) no período.</param>
/// <param name="LeadTimeMedioDias">A média do lead time dos itens ENCERRADOS; nula sem nenhum.</param>
public sealed record RoiTotais(
    int Itens, long Turnos, decimal Horas, double? LeadTimeMedioDias, long PullRequests, long PullRequestsMergeadas);

/// <summary>O painel de ROI por work item (US #387): as fotos mais recentes primeiro, e os totais.</summary>
/// <param name="Itens">No máximo as <c>ConsultasDoPainel.RoiMaximosNaLista</c> mais recentes.</param>
public sealed record RoiResposta(DateOnly De, DateOnly Ate, IReadOnlyList<RoiDoWorkItem> Itens, RoiTotais Totais);
