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
/// <param name="Impasses">O cartão Impasses (US #405); nulo só numa API anterior a ele.</param>
/// <param name="Arbitro">O cartão Árbitro (US #405); nulo só numa API anterior a ele.</param>
public sealed record QualidadeResposta(
    DateOnly De, DateOnly Ate, long Turnos, long TurnosComFalha, double? TaxaDeFalha, double? DuracaoMediaDoTurnoMs,
    IReadOnlyList<CausaDeFalha> FalhasPorCausa, long Avaliacoes, double? NotaMedia, long ObjetivosCumpridos,
    long ObjetivosRecusados, double? RazaoCumpridosRecusados, double? TurnosPorObjetivoCumprido,
    ImpassesResumo? Impasses = null, ArbitroResumo? Arbitro = null);

/// <summary>Quantas vezes um recorte (a origem, o desfecho, a ação, a regra) aparece no período.</summary>
public sealed record ContagemPorRecorte(string Recorte, long Quantidade);

/// <summary>
/// Os impasses do fluxo no período (US #405): quantos, quanto tempo parados e como destravaram. As
/// médias são nulas sem evento (a tela mostra um traço).
/// </summary>
/// <param name="Detectados">Eventos <c>ImpasseDetectado</c>.</param>
/// <param name="PorOrigem">Os detectados por origem (<c>mensagem-parada</c>, <c>objetivo-parado</c>).</param>
/// <param name="MinutosParadoNaDeteccao">Por quantos minutos, em média, o fluxo estava parado quando o impasse foi detectado.</param>
/// <param name="Destravados">Eventos <c>ImpasseResolvido</c>.</param>
/// <param name="MinutosAteDestravar">Quantos minutos, em média, da detecção ao desfecho.</param>
/// <param name="ComoDestravaram">Os desfechos, do mais comum ao mais raro.</param>
public sealed record ImpassesResumo(
    long Detectados, IReadOnlyList<ContagemPorRecorte> PorOrigem, double? MinutosParadoNaDeteccao,
    long Destravados, double? MinutosAteDestravar, IReadOnlyList<ContagemPorRecorte> ComoDestravaram);

/// <summary>
/// O árbitro no período (US #405): as cobranças por regra, a taxa de correção e as escaladas ao dev.
/// </summary>
/// <param name="Acoes">Todos os eventos <c>ArbitroAgiu</c>.</param>
/// <param name="Cobrancas">As ações <c>cobrou</c>.</param>
/// <param name="CobrancasPorRegra">As cobranças pela seção da regra (vazia: cobrança sem seção).</param>
/// <param name="Corrigidas">As ações <c>corrigido</c> — a cobrança atendida.</param>
/// <param name="TaxaDeCorrecao">Corrigidas sobre cobranças; nula sem cobrança.</param>
/// <param name="EscaladasAoDev">As ações <c>escalou-ao-dev</c>: o árbitro desistiu e chamou o desenvolvedor.</param>
/// <param name="PorAcao">Todas as ações, da mais comum à mais rara.</param>
public sealed record ArbitroResumo(
    long Acoes, long Cobrancas, IReadOnlyList<ContagemPorRecorte> CobrancasPorRegra, long Corrigidas, double? TaxaDeCorrecao,
    long EscaladasAoDev, IReadOnlyList<ContagemPorRecorte> PorAcao);

/// <summary>Uma linha do log de eventos.</summary>
public sealed record EventoDoLog(
    string EventId, int MaquinaId, string Apelido, string Tipo, string SessaoId, int Quantidade, long? Valor, string Detalhe, DateTimeOffset Em);

/// <summary>Uma página de resultados.</summary>
public sealed record Pagina<T>(IReadOnlyList<T> Itens, int Total, int NumeroDaPagina, int Tamanho);

/// <summary>
/// Os HARNESSES — o CLI do provedor cognitivo que roda o turno — que o painel filtra no log e nas exceções. É o recorte
/// do <see cref="TiposDeEvento.TurnoExecutado"/> e o fim do tipo da falha de turno sem causa (<c>Falha de turno do
/// &lt;cli&gt;</c>). O <see cref="Codex"/> entrou como provedor na versão 5 do dev.kit; o filtro fica vazio enquanto
/// nenhuma máquina o mandar.
/// </summary>
public static class Harnesses
{
    public const string Claude = "claude";

    /// <summary>O Kiro CLI (o filtro aceita também <c>kiro-cli</c>).</summary>
    public const string Kiro = "kiro";

    public const string Glm = "glm";
    public const string Codex = "codex";

    /// <summary>Os harnesses que o filtro aceita, na ordem da tela.</summary>
    public static IReadOnlyList<string> Conhecidos { get; } = [Claude, Kiro, Glm, Codex];
}

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

/// <summary>
/// O PAINEL DE UTILIDADE (US #687, <c>GET /api/dashboard/utilidade</c>): o que está travando o trabalho agora, o que o uso
/// entregou no período, os turnos de cada dia pelo desfecho e onde as tasks travam. As medidas sem denominador são nulas
/// (a tela mostra um traço).
/// </summary>
/// <param name="Adocao">A faixa Adoção (os usuários ativos ao lado das máquinas ativas); nula só numa API anterior a ela.</param>
public sealed record UtilidadeResposta(
    DateOnly De, DateOnly Ate, SaudeAgoraV1 Saude, ResultadoDoUsoV1 Resultado, IReadOnlyList<TurnosDoDiaV1> TurnosPorDesfecho,
    IReadOnlyList<CausaDeTravamentoV1> OndeTravam, AdocaoV1? Adocao = null);

/// <summary>
/// A ADOÇÃO no período: quem usou o dev.kit, nos filtros do painel (o período, a máquina e o harness). A máquina ativa é a que
/// tem total no período (com o harness, a que rodou turno naquele CLI); o usuário ativo é o dono de uma licença emitida para
/// uma máquina ativa e válida no período — é o login que liga a máquina anônima à pessoa.
/// </summary>
/// <param name="UsuariosAtivos">Usuários distintos com uso no período.</param>
/// <param name="UsuariosCadastrados">Os usuários do dev.kit (papel dev) cadastrados até o fim do período, no escopo.</param>
/// <param name="MaquinasAtivas">Máquinas distintas com uso no período.</param>
/// <param name="MaquinasRegistradas">Máquinas registradas até o fim do período, no escopo.</param>
/// <param name="MaquinasSemLogin">As máquinas ativas sem licença de usuário no período (o uso anônimo).</param>
public sealed record AdocaoV1(long UsuariosAtivos, long UsuariosCadastrados, long MaquinasAtivas, long MaquinasRegistradas, long MaquinasSemLogin);

/// <summary>
/// A SAÚDE DE AGORA — não depende do período: a última foto de cada task, work item e máquina (a máquina do filtro, quando
/// há). Cada cartão tem a ação dele na tela.
/// </summary>
/// <param name="TasksParadas">As tasks "em execução" sem processo e sem atividade.</param>
/// <param name="Perguntas">As perguntas abertas esperando o desenvolvedor.</param>
/// <param name="Fechamentos">Os fechamentos de entrega cujo último desfecho foi a falha (os itens são os work items).</param>
/// <param name="Maquinas">As máquinas cuja última amostra (das últimas 24 h) está com o disco ou a memória no limite.</param>
public sealed record SaudeAgoraV1(CartaoDeSaudeV1 TasksParadas, CartaoDeSaudeV1 Perguntas, CartaoDeSaudeV1 Fechamentos, CartaoDeSaudeV1 Maquinas);

/// <summary>Um cartão do "Saúde agora".</summary>
/// <param name="Quantidade">Quantos casos há agora.</param>
/// <param name="Novos24h">Quantos deles começaram nas últimas 24 h.</param>
/// <param name="MaisAntigo">Desde quando está o caso mais antigo; nulo sem caso.</param>
/// <param name="Itens">O que identificar no cartão: os work items (<c>#669</c>) do fechamento, a amostra da máquina…</param>
public sealed record CartaoDeSaudeV1(int Quantidade, int Novos24h, DateTimeOffset? MaisAntigo, IReadOnlyList<string> Itens);

/// <summary>O RESULTADO do uso no período (e a variação contra o período anterior de mesmo tamanho).</summary>
/// <param name="Aprovadas">As entregas aprovadas pelo desenvolvedor (a revisão humana <c>aprovada</c> ou <c>commit</c>).</param>
/// <param name="Integradas">Os fechamentos de entrega que terminaram bem (<see cref="TiposDeEvento.FechamentoDaEntrega"/> <c>ok</c>).</param>
/// <param name="AprovadasNaSemana">As aprovadas nos últimos 7 dias do período.</param>
/// <param name="PercentualNaPrimeira">As sessões aprovadas sem nenhuma devolução antes, sobre as aprovadas (0–100).</param>
/// <param name="VariacaoNaPrimeira">A diferença, em pontos percentuais, para o período anterior.</param>
/// <param name="NotaMedia">A nota média dos avaliadores no período.</param>
/// <param name="RodadasAteANota">As avaliações por sessão que teve o objetivo cumprido.</param>
/// <param name="MedianaAteAprovacaoMinutos">A mediana, em minutos, do começo da sessão à aprovação.</param>
/// <param name="VariacaoDaMedianaMinutos">A diferença da mediana para o período anterior.</param>
/// <param name="PercentualEsperandoDev">Do tempo até a aprovação, quanto foi esperando o dev (a entrega pronta até a decisão), 0–100.</param>
/// <param name="TurnosPorEntrega">Os turnos do período sobre as entregas aprovadas.</param>
public sealed record ResultadoDoUsoV1(
    long Aprovadas, long Integradas, long AprovadasNaSemana, double? PercentualNaPrimeira, double? VariacaoNaPrimeira, double? NotaMedia,
    double? RodadasAteANota, double? MedianaAteAprovacaoMinutos, double? VariacaoDaMedianaMinutos, double? PercentualEsperandoDev,
    double? TurnosPorEntrega, long Turnos, long ComandosDelegados, long ArquivosAlterados);

/// <summary>Os turnos de um dia pelo desfecho — as quatro camadas da coluna.</summary>
public sealed record TurnosDoDiaV1(DateOnly Dia, long Trabalhou, long EsperouDev, long LimiteDeUso, long Falhou);

/// <summary>Uma causa de "onde as tasks travam": a retrospectiva do Orquestrador, com o selo da sugestão ou da US.</summary>
/// <param name="Causa">O título curto da sugestão de impacto.</param>
/// <param name="Vezes">Quantas sugestões o Orquestrador registrou para a causa no período.</param>
/// <param name="Selo">O texto do selo (<c>sugestão #3</c>, <c>corrigido #568</c>, <c>#681</c>); vazio sem selo.</param>
/// <param name="TipoDoSelo">Um de <see cref="TiposDeSelo"/>.</param>
public sealed record CausaDeTravamentoV1(string Causa, long Vezes, string Selo, string TipoDoSelo);

/// <summary>Os tipos do selo de uma causa — a cor dele na tela.</summary>
public static class TiposDeSelo
{
    /// <summary>A sugestão está pendente: alguém precisa decidir.</summary>
    public const string Sugestao = "sugestao";

    /// <summary>A sugestão foi promovida: a causa tem correção.</summary>
    public const string Corrigido = "corrigido";

    /// <summary>A causa aponta a US (a origem da sugestão), sem decisão ainda.</summary>
    public const string Us = "us";

    public const string Nenhum = "";
}

/// <summary>O painel de ROI por work item (US #387): as fotos mais recentes primeiro, e os totais.</summary>
/// <param name="Itens">No máximo as <c>ConsultasDoPainel.RoiMaximosNaLista</c> mais recentes.</param>
public sealed record RoiResposta(DateOnly De, DateOnly Ate, IReadOnlyList<RoiDoWorkItem> Itens, RoiTotais Totais);
