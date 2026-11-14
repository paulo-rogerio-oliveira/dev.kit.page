namespace DevKitPage.Contracts.V1;

/// <summary>
/// O HEARTBEAT de uma máquina do dev.kit (controle remoto, fase 1): quem envia é o serviço do Windows (<c>GitKit.Scheduler</c>)
/// ou o próprio app, com o JWT do usuário. É o que diz ao site se a máquina está ligada, se o app está aberto e em que
/// estado está o agente — mesmo com o app fechado, porque o serviço continua de pé.
/// </summary>
/// <param name="Contrato">A versão do contrato (<see cref="RemotoV1.Contrato"/>); outra é 400.</param>
/// <param name="MaquinaId">O id anônimo da máquina (o mesmo da telemetria), nunca o hostname.</param>
/// <param name="Nome">O apelido que o dev deu à máquina no dev.kit (o site mostra para ele escolher).</param>
/// <param name="Origem">Quem enviou: <see cref="RemotoV1.OrigemServico"/> ou <see cref="RemotoV1.OrigemApp"/>.</param>
/// <param name="AppAberto">O <c>GitKit.exe</c> está rodando nesta máquina.</param>
/// <param name="EstadoDoAgente">Um de <see cref="RemotoV1.EstadosDoAgente"/>; vazio é <see cref="RemotoV1.AgenteParado"/>.</param>
/// <param name="TurnoEmAndamento">Há turno de agente rodando: o site avisa antes de reiniciar o dev.kit.</param>
/// <param name="EnviadoEm">O relógio da máquina no envio (informativo: a API conta o tempo pelo próprio relógio).</param>
public sealed record HeartbeatV1(
    int Contrato, string MaquinaId, string? Nome, string? VersaoDevKit, string? Origem, bool AppAberto,
    string? EstadoDoAgente, bool TurnoEmAndamento, DateTimeOffset? EnviadoEm = null);

/// <summary>
/// O RETRATO do estado de uma máquina: os projetos, os fluxos em execução e as pendências, já sanitizados no dev.kit
/// (<c>TraceSanitizado</c>). É uma FOTO inteira, não um delta: cada envio substitui o anterior, e o mais antigo que chegar
/// depois (pelo <see cref="GeradoEm"/>) não volta a foto para trás. A API recusa (422) o retrato com token ou dado de
/// cliente, dizendo o campo e o tipo — nunca o valor.
/// </summary>
public sealed record RetratoV1(
    int Contrato, string MaquinaId, DateTimeOffset GeradoEm, IReadOnlyList<ProjetoNoRetratoV1>? Projetos,
    IReadOnlyList<FluxoNoRetratoV1>? Fluxos, IReadOnlyList<PendenciaNoRetratoV1>? Pendencias);

/// <summary>Um projeto do Orquestrador no retrato: a situação (<see cref="ProjetosV1.Situacoes"/>), a onda corrente e os itens.</summary>
public sealed record ProjetoNoRetratoV1(string Slug, string Nome, string Situacao, int OndaAtual, IReadOnlyList<ItemNoRetratoV1>? Itens);

/// <summary>Um item do plano no retrato: a situação é uma de <see cref="ProjetosV1.SituacoesDoItem"/>.</summary>
public sealed record ItemNoRetratoV1(int Numero, string Titulo, string Situacao, int Onda);

/// <summary>
/// Um fluxo em execução (o "Executando" do Board): o id é o da execução no dev.kit — opaco, sem caminho —, e o projeto e
/// o item, quando a execução é de um item de projeto.
/// </summary>
/// <param name="Conversavel">
/// (campo novo, semana 7) A task desta execução aceita a CONVERSA pelo site (<see cref="RemotoV1.RecursoConversaDaTask"/>):
/// o dev.kit publica as falas dela com a <see cref="FalaDoDevKitV1.Sessao"/> = <see cref="Id"/> e entrega a mensagem com esse
/// destino. Ausente (o dev.kit antigo), o site não abre o chat da task.
/// </param>
public sealed record FluxoNoRetratoV1(
    string Id, string Nome, string Etapa, string Situacao, string? Projeto = null, int? Item = null, string? Agente = null,
    DateTimeOffset? DesdeEm = null, bool? Conversavel = null);

/// <summary>
/// Uma PENDÊNCIA do dev (o que espera decisão). A pergunta leva os MESMOS campos do <c>[GITKIT:PERGUNTA]</c> —
/// <see cref="Pergunta"/>, <see cref="Opcoes"/> e <see cref="JaTentei"/> —, que as decisões remotas (fase 3 da proposta) mostram
/// como botões. O <see cref="Id"/> é estável: é por ele que a resposta volta.
/// </summary>
/// <param name="Escopo">
/// (campo novo, correções do board remoto, ajuste 9) O escopo exato da AUTORIZAÇÃO (tipo <see cref="RemotoV1.PendenciaAutorizacao"/>).
/// Ausente nas outras pendências — e fora do JSON, para o id do snapshot (o resumo da pendência serializada) não mudar.
/// </param>
public sealed record PendenciaNoRetratoV1(
    string Id, string Tipo, string Titulo, string? Pergunta = null, IReadOnlyList<string>? Opcoes = null, string? JaTentei = null,
    string? Projeto = null, int? Item = null, string? Fluxo = null, DateTimeOffset? CriadaEm = null,
    SnapshotDaDecisaoV1? Snapshot = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    EscopoDaAutorizacaoV1? Escopo = null);

/// <summary>
/// A resposta do heartbeat: quando a API o recebeu, de quanto em quanto tempo o próximo é esperado e os
/// <see cref="Recursos"/> que esta API aceita (<see cref="RemotoV1.RecursosDaApi"/>, campo novo). O dev.kit confere o recurso
/// antes de usar a rota dele: a API antiga não manda a lista, e ele não envia.
/// </summary>
public sealed record HeartbeatRecebidoV1(string MaquinaId, DateTimeOffset RecebidoEm, int IntervaloSegundos, IReadOnlyList<string>? Recursos = null);

/// <summary>A resposta do retrato. <see cref="Aplicado"/> falso é o retrato mais antigo que o gravado: aceito, mas descartado.</summary>
public sealed record RetratoRecebidoV1(string MaquinaId, DateTimeOffset RecebidoEm, bool Aplicado);

/// <summary>
/// Uma máquina do usuário, como o site a lista. <see cref="Conectada"/> é o heartbeat recebido há no máximo
/// <see cref="RemotoV1.SegundosAteDesconectar"/> segundos — pelo relógio da API. <see cref="Vinculada"/> (campo novo, item 9 do
/// remote-control-v3) é o serviço da máquina com o client credentials ativo: o site oferece o "Desvincular".
/// </summary>
public sealed record MaquinaRemotaV1(
    string MaquinaId, string Nome, string VersaoDevKit, string Origem, bool AppAberto, string EstadoDoAgente,
    bool TurnoEmAndamento, bool Conectada, DateTimeOffset? UltimoHeartbeatEm, DateTimeOffset? RetratoGeradoEm,
    bool Vinculada = false);

/// <summary>O retrato mais recente de uma máquina do usuário, com a máquina.</summary>
public sealed record RetratoDaMaquinaV1(MaquinaRemotaV1 Maquina, RetratoV1 Retrato, DateTimeOffset RecebidoEm);

/// <summary>Um campo recusado do envio: o caminho (<c>pendencias[0].titulo</c>) e o tipo do dado (<see cref="RemotoV1.TiposSensiveis"/>).</summary>
public sealed record CampoSensivelV1(string Campo, string Tipo);

/// <summary>
/// O contrato v1 do controle remoto (<c>docs/contrato-remoto-v1.md</c>). Campo novo é opcional e não muda a versão;
/// mudança incompatível é o contrato 2.
/// </summary>
public static class RemotoV1
{
    /// <summary>A versão do contrato que esta API aceita.</summary>
    public const int Contrato = 1;

    /// <summary>De quanto em quanto tempo o serviço envia o heartbeat.</summary>
    public const int IntervaloDoHeartbeatSegundos = 60;

    /// <summary>Sem heartbeat por mais que isto, a máquina aparece desconectada (três heartbeats perdidos).</summary>
    public const int SegundosAteDesconectar = 180;

    /// <summary>O maior corpo do heartbeat.</summary>
    public const int TamanhoMaximoDoHeartbeat = 4 * 1024;

    /// <summary>O maior corpo do retrato.</summary>
    public const int TamanhoMaximoDoRetrato = 256 * 1024;

    public const int TamanhoMaximoDoId = 64;
    public const int TamanhoMaximoDoNome = 200;
    public const int TamanhoMaximoDaVersao = 50;
    public const int TamanhoMaximoDoTexto = 2000;
    public const int MaximoDeProjetos = 50;
    public const int MaximoDeItens = 100;
    public const int MaximoDeFluxos = 100;
    public const int MaximoDePendencias = 100;
    public const int MaximoDeOpcoes = 10;

    public const string OrigemServico = "servico";
    public const string OrigemApp = "app";

    /// <summary>Quem envia o heartbeat.</summary>
    public static IReadOnlyList<string> Origens { get; } = [OrigemServico, OrigemApp];

    public const string AgenteAtivo = "ativo";
    public const string AgentePausado = "pausado";
    public const string AgenteEmLimite = "limite";
    public const string AgenteParado = "parado";

    /// <summary>O estado do agente na máquina: o site diz por que a mensagem ainda não foi tratada.</summary>
    public static IReadOnlyList<string> EstadosDoAgente { get; } = [AgenteAtivo, AgentePausado, AgenteEmLimite, AgenteParado];

    public const string PendenciaPergunta = "pergunta";
    public const string PendenciaAprovacao = "aprovacao";
    public const string PendenciaFalha = "falha";

    /// <summary>(ajuste 9) A confirmação que o Orquestrador pediria ao dev, como cartão com o escopo exato e o login confirmado.</summary>
    public const string PendenciaAutorizacao = "autorizacao";

    /// <summary>Os tipos de pendência (o novo entra no fim; o dev.kit só o publica com <see cref="RecursoDecisoesAutorizar"/>).</summary>
    public static IReadOnlyList<string> TiposDePendencia { get; } = [PendenciaPergunta, PendenciaAprovacao, PendenciaFalha, PendenciaAutorizacao];

    /// <summary>(ajuste 9) A API aceita as pendências de autorização e a ação "autorizar" com o login confirmado.</summary>
    public const string RecursoDecisoesAutorizar = "decisoes-autorizar";

    public const string SensivelSegredo = "segredo";
    public const string SensivelEmail = "email";
    public const string SensivelCpf = "cpf";
    public const string SensivelCnpj = "cnpj";
    public const string SensivelTelefone = "telefone";

    /// <summary>Os tipos de dado que o envio não pode levar.</summary>
    public static IReadOnlyList<string> TiposSensiveis { get; } = [SensivelSegredo, SensivelEmail, SensivelCpf, SensivelCnpj, SensivelTelefone];

    /// <summary>A API grava as falas do chat do dev.kit (<c>POST /api/remoto/conversa/falas</c>, contrato seção 4.2).</summary>
    public const string RecursoFalasDoDesktop = "falas-do-desktop";

    /// <summary>
    /// O sinal de suporte da resposta do heartbeat: o que esta API aceita além do contrato da fase 1. Recurso novo entra no fim;
    /// o dev.kit só usa a rota do recurso que viu aqui.
    /// </summary>
    public static IReadOnlyList<string> RecursosDaApi { get; } =
        [RecursoFalasDoDesktop, "decisoes-com-snapshot", RecursoCatalogo, RecursoDecisoesAutorizar, "matrix-v1", "matrix-avaliacao-v1", RecursoConversaDaTask];

    /// <summary>
    /// (semana 7) A API guarda as falas POR TASK (<see cref="FalaDoDevKitV1.Sessao"/>), as devolve na conversa da task
    /// (<c>GET /api/remoto/maquinas/{maquina}/sessoes/{sessao}/conversa</c>) e leva a mensagem com destino
    /// (<see cref="ComandoNovoV1.Sessao"/>) na fila. Só com ele o dev.kit publica as falas da task e a marca de conversável.
    /// </summary>
    public const string RecursoConversaDaTask = "conversa-da-task";

    /// <summary>
    /// A API aceita o comando <see cref="ConversaV1.ComandoCatalogo"/> e guarda o catálogo da máquina
    /// (<c>PUT /api/remoto/catalogo</c>, contrato seção 5): o handshake dos atalhos do chat remoto.
    /// </summary>
    public const string RecursoCatalogo = "catalogo-remoto";

    /// <summary>O maior corpo do envio do catálogo.</summary>
    public const int TamanhoMaximoDoCatalogo = 256 * 1024;

    /// <summary>O máximo de entradas em cada lista do catálogo (o dev.kit corta as excedentes).</summary>
    public const int MaximoNoCatalogo = 200;

    public const string ParametroTexto = "texto";
    public const string ParametroLista = "lista";
    public const string ParametroSimNao = "sim-nao";

    /// <summary>Os tipos de parâmetro de um fluxo do catálogo (<see cref="ParametroDoFluxoV1.Tipo"/>): o campo que o chat mostra.</summary>
    public static IReadOnlyList<string> TiposDeParametro { get; } = [ParametroTexto, ParametroLista, ParametroSimNao];

    /// <summary>Quantos parâmetros um fluxo do catálogo declara, no máximo.</summary>
    public const int MaximoDeParametrosDoFluxo = 30;

    /// <summary>O <c>codigo</c> do 400 de contrato que esta API não conhece (o ProblemDetails leva <c>contratoAceito</c>).</summary>
    public const string CodigoContratoDesconhecido = "contrato-desconhecido";

    /// <summary>O <c>codigo</c> do 422 do envio com dado sensível (o ProblemDetails leva <c>campos</c>).</summary>
    public const string CodigoDadoSensivel = "dado-sensivel";
}
