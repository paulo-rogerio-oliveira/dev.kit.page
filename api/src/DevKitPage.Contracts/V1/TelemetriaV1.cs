namespace DevKitPage.Contracts.V1;

/// <summary>
/// O contrato v1 da ingestão — o MESMO que o dev.kit envia (git.kit,
/// <c>TelemetrySyncService</c>). Mudar um nome aqui quebra todas as máquinas já instaladas: um
/// campo novo é opcional, e uma mudança incompatível é um <c>v2</c>, com rota própria.
/// </summary>
public static class ContratoV1
{
    /// <summary>O valor de <see cref="TelemetryBatchV1.Versao"/> que esta rota aceita.</summary>
    public const string Versao = "v1";

    /// <summary>O cabeçalho com a chave da máquina — a ingestão só aceita isto, nunca o JWT de usuário.</summary>
    public const string CabecalhoDaChave = "X-Machine-Key";
}

/// <summary>Um evento de uso.</summary>
/// <param name="EventId">O id único, gerado no dev.kit: é por ele que o reenvio é descartado.</param>
/// <param name="Tipo">O tipo (<see cref="TiposDeEvento"/>); o desconhecido é ignorado, não recusado.</param>
/// <param name="SessaoId">A sessão (task) do dev.kit — um id interno, sem pasta nem nome.</param>
/// <param name="Quantidade">Quantos o evento soma.</param>
/// <param name="Valor">A medida (duração do turno em ms, nota do avaliador), quando há.</param>
/// <param name="Detalhe">O recorte (ferramenta, executável, causa da falha, CLI, versão).</param>
/// <param name="Em">Quando aconteceu (UTC).</param>
/// <param name="Trace">
/// Opcional (US #381): o trace JÁ SANITIZADO no dev.kit de uma <see cref="TiposDeEvento.ExcecaoNaoClassificada"/>
/// — o tipo e os quadros, sem caminhos, e-mails nem URLs. Até <c>8 KB</c>; o cliente antigo não manda.
/// </param>
/// <param name="Assinatura">Opcional (US #381): a impressão digital da exceção, que agrupa as ocorrências.</param>
/// <param name="Roi">
/// Opcional (US #387): a FOTO do ROI de um work item, que só vem no <see cref="TiposDeEvento.RoiCalculado"/>.
/// Um dev.kit antigo nunca o manda — campo novo opcional, sem v2.
/// </param>
/// <param name="Avaliacao">
/// Opcional (US #417): o joinha do desenvolvedor sobre um turno do agente, que só vem no
/// <see cref="TiposDeEvento.EntregaAvaliada"/>. Um dev.kit antigo não o manda — campo novo opcional, sem v2.
/// </param>
public sealed record TelemetryEventV1(
    string EventId, string Tipo, string SessaoId, int Quantidade, long? Valor, string Detalhe, DateTimeOffset Em,
    string? Trace = null, string? Assinatura = null, RoiV1? Roi = null, AvaliacaoV1? Avaliacao = null);

/// <summary>
/// A avaliação (o joinha) que o desenvolvedor deu à entrega de um turno do agente (US #417). É uma
/// por (máquina, <see cref="TelemetryEventV1.SessaoId"/>, <see cref="Turno"/>): o mesmo turno avaliado de
/// novo SUBSTITUI a avaliação anterior — vale a mais recente pelo <c>em</c> do evento —, nunca soma.
/// </summary>
/// <param name="Boa">O joinha: verdadeiro = positivo, falso = negativo.</param>
/// <param name="Motivo">O motivo, da lista fechada do <see cref="TiposDeEvento.EntregaAvaliada"/>; vazio na boa sem motivo.</param>
/// <param name="Agente">O agente (CLI) que fez o turno: claude, kiro, kimi, glm… Vazio quando não se sabe.</param>
/// <param name="Modelo">O modelo efetivo do agente no turno; vazio quando não se sabe.</param>
/// <param name="Fluxo">O id do fluxo da task; vazio na task sem fluxo.</param>
/// <param name="WorkItem">O work item vinculado à task, quando há.</param>
/// <param name="Turno">O número do turno do agente avaliado na sessão (a chave da substituição).</param>
public sealed record AvaliacaoV1(bool Boa, string Motivo, string Agente, string Modelo, string Fluxo, int? WorkItem, int Turno);

/// <summary>
/// O ROI de um work item como o dev.kit o calculou (US #387, <c>devcli roi</c>): é uma FOTO, e não uma
/// soma — a mesma máquina manda o mesmo item várias vezes, e vale a mais recente (pelo <c>em</c> do evento).
/// </summary>
/// <param name="WorkItem">O id do work item no board.</param>
/// <param name="Tipo">O tipo do item (User Story, Bug, Task…).</param>
/// <param name="Estado">O estado no board quando a foto foi tirada.</param>
/// <param name="De">O primeiro dia com trabalho no item.</param>
/// <param name="Ate">O último dia com trabalho no item.</param>
/// <param name="TurnosDoAgente">Os turnos do agente nas tasks do item.</param>
/// <param name="Sessoes">As sessões (tasks) do dev.kit que trabalharam no item.</param>
/// <param name="Horas">As horas que o dev.kit apurou para o item.</param>
/// <param name="HorasNoBoard">As horas lançadas no board (Completed Work).</param>
/// <param name="HorasNoTimesheet">As horas no timesheet corporativo; nulo quando não há timesheet configurado.</param>
/// <param name="LeadTimeDias">Da criação ao encerramento, em dias; nulo enquanto o item não fechou.</param>
/// <param name="Aberto">O item ainda está aberto no board.</param>
/// <param name="PullRequests">As pull requests vinculadas ao item.</param>
/// <param name="PullRequestsMergeadas">Delas, as concluídas (mergeadas).</param>
public sealed record RoiV1(
    int WorkItem, string Tipo, string Estado, DateOnly De, DateOnly Ate, int TurnosDoAgente, int Sessoes,
    decimal Horas, decimal HorasNoBoard, decimal? HorasNoTimesheet, double? LeadTimeDias, bool Aberto,
    int PullRequests, int PullRequestsMergeadas);

/// <summary>Um lote de eventos de uma máquina.</summary>
public sealed record TelemetryBatchV1(string Versao, string MaquinaId, string VersaoDevKit, IReadOnlyList<TelemetryEventV1> Eventos);

/// <summary>O que a ingestão fez com o lote.</summary>
/// <param name="Recebidos">Eventos no lote.</param>
/// <param name="Novos">Gravados agora.</param>
/// <param name="Duplicados">Já recebidos antes (o reenvio) — não contam de novo.</param>
/// <param name="Ignorados">De tipo que esta versão da API não conhece.</param>
public sealed record BatchResultV1(int Recebidos, int Novos, int Duplicados, int Ignorados);

/// <summary>O pedido de registro de uma máquina: o id anônimo, sem hostname nem usuário.</summary>
/// <param name="CodigoEmpresa">
/// Opcional (US #381): o código de adesão da empresa, que o dev.kit só manda depois de o colaborador
/// ACEITAR o aviso de coleta. Nulo (o cliente antigo) não mexe no vínculo; vazio desfaz.
/// </param>
/// <param name="Colaborador">Opcional (US #381): o nome que o próprio colaborador informou, compartilhado com o gestor.</param>
public sealed record MachineRegistrationV1(
    string MaquinaId, string VersaoDevKit, string CodigoRegistro, string? CodigoEmpresa = null, string? Colaborador = null);

/// <summary>A chave da máquina — devolvida UMA vez; a API guarda só o hash.</summary>
/// <param name="Empresa">A empresa a que a máquina ficou vinculada, ou nulo (US #381).</param>
/// <param name="Adesao">O que aconteceu com o código da empresa, para o dev.kit mostrar ao colaborador.</param>
public sealed record MachineRegistrationResponseV1(string Chave, string? Empresa = null, string? Adesao = null);

/// <summary>
/// Os tipos de evento que o dev.kit envia (o <c>TelemetryEventKind</c> do git.kit, pelo nome).
/// Quantidade de uso: os quatro da US #252 mais a sessão; qualidade: turnos, falhas, notas e objetivos.
/// </summary>
public static class TiposDeEvento
{
    public const string ArquivoAlterado = "ArquivoAlterado";
    public const string FluxoExecutado = "FluxoExecutado";
    public const string FerramentaAcionada = "FerramentaAcionada";
    public const string ComandoDelegado = "ComandoDelegado";
    public const string TurnoExecutado = "TurnoExecutado";
    public const string TurnoFalhou = "TurnoFalhou";
    public const string ObjetivoAvaliado = "ObjetivoAvaliado";
    public const string ObjetivoCumprido = "ObjetivoCumprido";
    public const string ObjetivoRecusado = "ObjetivoRecusado";
    public const string SessaoIniciada = "SessaoIniciada";

    /// <summary>
    /// Uma exceção que nenhum detector classificou (US #381): o turno que falhou sem causa em
    /// <c>TurnFailures.Padrao</c> ou a exceção não tratada do app, do serviço ou do devcli. Leva o
    /// <see cref="TelemetryEventV1.Trace"/> e a <see cref="TelemetryEventV1.Assinatura"/>.
    /// </summary>
    public const string ExcecaoNaoClassificada = "ExcecaoNaoClassificada";

    /// <summary>
    /// O ROI de um work item calculado no dev.kit (US #387): o <see cref="TelemetryEventV1.Detalhe"/> é o id
    /// do item, o <see cref="TelemetryEventV1.Valor"/> os turnos do agente, e o <see cref="TelemetryEventV1.Roi"/>
    /// a foto completa. No total diário ele conta "quantos ROIs foram calculados"; a foto vai para a tabela própria.
    /// </summary>
    public const string RoiCalculado = "RoiCalculado";

    // ----- US #399: a avaliação humana, a revisão, o custo e as falhas sem ninguém na frente -----
    // Gravados desde já nos totais diários (por tipo e detalhe), para o histórico acumular; os
    // indicadores do painel ficam para a fase 2. Nenhum leva campo novo: só Valor e Detalhe.

    /// <summary>
    /// O desenvolvedor avaliou a entrega: <see cref="TelemetryEventV1.Detalhe"/> = o motivo (lista fechada), valor 1 = boa, 0 = ruim.
    /// Desde a US #417 leva a <see cref="TelemetryEventV1.Avaliacao"/> (agente, modelo, fluxo, work item e turno),
    /// que a ingestão grava na tabela própria do feedback; sem ela o evento só conta no total diário.
    /// </summary>
    public const string EntregaAvaliada = "EntregaAvaliada";

    /// <summary>A entrega ficou pronta (o começo do tempo de revisão): detalhe = a origem (<c>objetivo</c>, <c>resumo</c>, <c>turno</c>).</summary>
    public const string EntregaPronta = "EntregaPronta";

    /// <summary>A decisão do desenvolvedor: detalhe = <c>aprovada</c>, <c>commit</c> ou <c>devolvida</c>; valor = ms desde a entrega pronta.</summary>
    public const string RevisaoHumana = "RevisaoHumana";

    /// <summary>Os tokens de um turno: detalhe = o CLI; valor = entrada + saída.</summary>
    public const string TokensConsumidos = "TokensConsumidos";

    /// <summary>O custo equivalente de API de um turno: detalhe = o CLI; valor em micro-dólares.</summary>
    public const string CustoEstimado = "CustoEstimado";

    /// <summary>A troca automática de agente no limite de uso: detalhe = <c>de→para</c>.</summary>
    public const string AgenteTrocado = "AgenteTrocado";

    /// <summary>Um comando negado ao agente: detalhe = a classe (<c>proibido</c> ou <c>nao-aprovado</c>).</summary>
    public const string ComandoNegado = "ComandoNegado";

    // ----- US #405 (#411): o impasse do fluxo e o árbitro que reage a ele -----
    // Wire v1 sem campo novo: o recorte vai no Detalhe e a medida no Valor. O painel de qualidade os
    // agrega nos cartões Impasses e Árbitro (CalculoDeQualidade).

    /// <summary>
    /// O fluxo parou à espera de algo que não chega: <see cref="TelemetryEventV1.Detalhe"/> = a origem
    /// (<see cref="OrigensDoImpasse"/>); <see cref="TelemetryEventV1.Valor"/> = os minutos parado até a detecção.
    /// </summary>
    public const string ImpasseDetectado = "ImpasseDetectado";

    /// <summary>
    /// O impasse destravou: detalhe = como (<c>nota</c>, <c>mensagem-entregue</c>, <c>objetivo-cumprido</c>,
    /// <c>dev-falou</c>, <c>agente-pediu-avaliacao</c>, <c>cancelado</c>, <c>arbitro-reagiu</c>); valor = os
    /// minutos entre a detecção e o desfecho.
    /// </summary>
    public const string ImpasseResolvido = "ImpasseResolvido";

    /// <summary>
    /// O árbitro agiu: detalhe = <c>&lt;acao&gt;</c> ou <c>&lt;acao&gt;|&lt;seção da regra&gt;</c> (a ação em
    /// <see cref="AcoesDoArbitro"/>; a seção é o TÍTULO da seção do CLAUDE.md, nunca texto de conversa);
    /// valor = a rodada (ou o número da reação).
    /// </summary>
    public const string ArbitroAgiu = "ArbitroAgiu";

    /// <summary>Os tipos que esta versão da API grava.</summary>
    public static IReadOnlySet<string> Conhecidos { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ArquivoAlterado, FluxoExecutado, FerramentaAcionada, ComandoDelegado, TurnoExecutado,
        TurnoFalhou, ObjetivoAvaliado, ObjetivoCumprido, ObjetivoRecusado, SessaoIniciada, ExcecaoNaoClassificada,
        RoiCalculado,
        EntregaAvaliada, EntregaPronta, RevisaoHumana, TokensConsumidos, CustoEstimado, AgenteTrocado, ComandoNegado,
        ImpasseDetectado, ImpasseResolvido, ArbitroAgiu,
    };
}

/// <summary>As origens de um <see cref="TiposDeEvento.ImpasseDetectado"/> (o recorte do evento).</summary>
public static class OrigensDoImpasse
{
    /// <summary>Uma mensagem entre tasks ficou sem resposta.</summary>
    public const string MensagemParada = "mensagem-parada";

    /// <summary>O objetivo do fluxo não andou (nenhuma nota, nenhum envio).</summary>
    public const string ObjetivoParado = "objetivo-parado";
}

/// <summary>
/// As ações de um <see cref="TiposDeEvento.ArbitroAgiu"/> — a parte do detalhe antes do primeiro <c>|</c>.
/// O painel conta as cobranças por regra, a taxa de correção (<see cref="Corrigido"/> sobre
/// <see cref="Cobrou"/>) e as escaladas ao desenvolvedor.
/// </summary>
public static class AcoesDoArbitro
{
    /// <summary>O separador entre a ação e a seção da regra no detalhe.</summary>
    public const char Separador = '|';

    public const string Cobrou = "cobrou";
    public const string EscalouAoDev = "escalou-ao-dev";
    public const string Aprovou = "aprovou";
    public const string Corrigido = "corrigido";
    public const string TetoDeRodadas = "teto-de-rodadas";
    public const string TetoDeCusto = "teto-de-custo";
    public const string FalhaDeComunicacao = "falha-de-comunicacao";
    public const string ReencaminhouResposta = "reencaminhou-resposta";
    public const string PediuAvaliacao = "pediu-avaliacao";
    public const string TurnoNoDono = "turno-no-dono";
    public const string TurnoNoLeitor = "turno-no-leitor";
}
