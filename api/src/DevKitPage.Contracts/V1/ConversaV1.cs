namespace DevKitPage.Contracts.V1;

/// <summary>
/// Um COMANDO novo do site para uma máquina (<c>POST /api/remoto/comandos</c>): a mensagem ao Orquestrador ou o pedido de
/// ligar ou reiniciar o dev.kit. O site grava no SQL e na fila do usuário; o serviço da máquina lê a fila com a SAS curta.
/// </summary>
/// <param name="Id">
/// O id do comando, escolhido pelo SITE (um GUID): o reenvio com o mesmo id devolve o comando que já existe (200), sem
/// outra mensagem na fila nem conta no limite. Vazio: a API sorteia.
/// </param>
/// <param name="MaquinaId">A máquina de destino, uma só (o comando não vai para as outras máquinas do usuário).</param>
/// <param name="Tipo">Um de <see cref="ConversaV1.TiposDeComando"/>.</param>
/// <param name="Texto">
/// A mensagem (obrigatória em <see cref="ConversaV1.ComandoMensagem"/>), o formulário em JSON nos de criação
/// (<see cref="ConversaV1.TiposDeCriacao"/>); vazia nos outros.
/// </param>
/// <param name="TtlSegundos">Quanto tempo o comando espera na fila; vazio é <c>Remoto:TtlPadraoSegundos</c>.</param>
/// <param name="Dispositivo">De onde veio: <see cref="ConversaV1.DispositivoCelular"/> ou <see cref="ConversaV1.DispositivoNavegador"/>.</param>
/// <param name="ConfirmarReinicio">O usuário confirmou reiniciar com o turno em andamento (sem isto, 409).</param>
public sealed record ComandoNovoV1(
    string? Id, string MaquinaId, string Tipo, string? Texto = null, int? TtlSegundos = null, string? Dispositivo = null,
    bool ConfirmarReinicio = false);

/// <summary>
/// Um comando como o site e o dev.kit o leem. O estado (<see cref="ConversaV1.EstadosDoComando"/>) diz onde ele está:
/// <see cref="ConversaV1.EstadoNaFila"/> até o dev.kit confirmar o recebimento, e <see cref="ConversaV1.EstadoExpirado"/>
/// quando o TTL venceu antes disso — o site nunca finge que entregou.
/// </summary>
public sealed record ComandoRemotoV1(
    string Id, string MaquinaId, string Tipo, string Texto, string Dispositivo, string Estado, DateTimeOffset CriadoEm,
    DateTimeOffset ExpiraEm, DateTimeOffset? EntregueEm, DateTimeOffset? ConcluidoEm, string Erro);

/// <summary>
/// A MENSAGEM que vai na fila do usuário (o JSON do <c>MessageText</c>, em Base64). A fila é só transporte: a mensagem lida
/// some, e o histórico fica no SQL. Ela pode chegar repetida e fora de ordem: o serviço confere o <see cref="Id"/> e confirma
/// o recebimento (<c>POST /api/remoto/comandos/{id}/recebido</c>) ANTES de executar.
/// </summary>
/// <param name="Origem">Sempre <see cref="ConversaV1.OrigemRemoto"/>: a linha do tempo do dev.kit mostra de onde veio a ordem.</param>
/// <param name="ConfirmarReinicio">
/// (campo novo, item 8) O usuário confirmou o reinício com o turno em andamento: o serviço confere o turno de novo na hora de
/// executar e, sem a confirmação, não interrompe o turno que começou depois do heartbeat que o site leu. O dev.kit antigo o
/// ignora.
/// </param>
public sealed record MensagemDaFilaV1(
    int Contrato, string Id, string MaquinaId, string Tipo, string Texto, string Origem, string Dispositivo, DateTimeOffset CriadoEm,
    DateTimeOffset ExpiraEm, bool ConfirmarReinicio = false);

/// <summary>O pedido da SAS da fila pelo serviço da máquina (<c>POST /api/remoto/fila/sas</c>).</summary>
public sealed record PedidoDeSasV1(int Contrato, string MaquinaId);

/// <summary>
/// A SAS CURTA da fila do usuário: só a fila dele e só para CONSUMIR (a permissão <c>p</c> lê e apaga mensagens; não
/// espia, não escreve, não chega a outra fila). O serviço pede outra antes de <see cref="ExpiraEm"/>.
/// </summary>
/// <param name="Endpoint">A URL da fila (<c>https://{conta}.queue.core.windows.net/comandos</c>).</param>
/// <param name="Sas">A query da SAS, sem o <c>?</c> — vai depois da URL de cada chamada.</param>
/// <param name="Permissoes">Sempre <see cref="ConversaV1.PermissaoDaSas"/>.</param>
public sealed record SasDaFilaV1(string Endpoint, string Sas, string Permissoes, DateTimeOffset ExpiraEm);

/// <summary>O recebimento de um comando pela máquina (<c>POST /api/remoto/comandos/{id}/recebido</c>).</summary>
public sealed record RecebimentoV1(int Contrato, string MaquinaId);

/// <summary>
/// A resposta do recebimento: <see cref="Executar"/> verdadeiro SÓ no primeiro recebimento dentro do TTL. O repetido
/// (<see cref="Repetido"/>) e o vencido (<see cref="ConversaV1.EstadoExpirado"/>) não se executam.
/// </summary>
public sealed record RecebimentoAceitoV1(string Id, string Estado, bool Executar, bool Repetido);

/// <summary>
/// Uma RESPOSTA do dev.kit (<c>POST /api/remoto/respostas</c>), por HTTPS — já sanitizada (<c>TraceSanitizado</c>); a API
/// recusa (422) a que traz token ou dado de cliente.
/// </summary>
/// <param name="Id">O id da resposta, do dev.kit: o reenvio com o mesmo id não grava outra (idempotente).</param>
/// <param name="ComandoId">O comando respondido; vazio é a mensagem espontânea do Orquestrador.</param>
/// <param name="Tipo">Um de <see cref="ConversaV1.TiposDeResposta"/>.</param>
public sealed record RespostaDoDevKitV1(
    int Contrato, string MaquinaId, string Id, string? ComandoId, string Tipo, string? Texto, DateTimeOffset GeradaEm);

/// <summary>Uma resposta guardada, como o site a lê.</summary>
public sealed record RespostaRemotaV1(
    string Id, string MaquinaId, string? ComandoId, string Tipo, string Texto, DateTimeOffset GeradaEm, DateTimeOffset RecebidaEm);

/// <summary>A resposta do envio do dev.kit: <see cref="Repetida"/> é o reenvio de uma já gravada.</summary>
public sealed record RespostaRecebidaV1(string Id, bool Repetida, DateTimeOffset RecebidaEm);

/// <summary>
/// Uma FALA do chat do Orquestrador no dev.kit (<c>POST /api/remoto/conversa/falas</c>), para o chat do Board mostrar a mesma
/// conversa: quem falou (<see cref="Autor"/>) e onde a fala nasceu (<see cref="Origem"/>). Só o dev.kit que leu
/// <see cref="RemotoV1.RecursoFalasDoDesktop"/> nos <c>recursos</c> do heartbeat a envia; o antigo não, e nada muda para ele.
/// Já sanitizada (<c>TraceSanitizado</c>): a API recusa (422) a que traz token ou dado de cliente.
/// </summary>
/// <param name="Id">O id da fala, do dev.kit: o reenvio com o mesmo id e o mesmo conteúdo não grava outra (idempotente).</param>
/// <param name="Autor">Um de <see cref="ConversaV1.AutoresDaFala"/>.</param>
/// <param name="Origem">Um de <see cref="ConversaV1.OrigensDaFala"/>.</param>
/// <param name="DitaEm">Quando foi dita, pelo relógio da máquina: é a ordem da conversa no histórico.</param>
/// <param name="ComandoId">
/// O comando do site que trouxe a fala; obrigatório na de origem <see cref="ConversaV1.OrigemRemoto"/>, para o site juntar a fala
/// ao comando e não mostrá-la duas vezes.
/// </param>
public sealed record FalaDoDevKitV1(
    int Contrato, string MaquinaId, string Id, string Autor, string Origem, string? Texto, DateTimeOffset DitaEm, string? ComandoId = null);

/// <summary>Uma fala guardada, como o site a lê.</summary>
public sealed record FalaRemotaV1(
    string Id, string MaquinaId, string Autor, string Origem, string Texto, string? ComandoId, DateTimeOffset DitaEm, DateTimeOffset RecebidaEm);

/// <summary>A resposta do envio da fala: <see cref="Repetida"/> é o reenvio de uma já gravada.</summary>
public sealed record FalaRecebidaV1(string Id, bool Repetida, DateTimeOffset RecebidaEm);

/// <summary>
/// O histórico da conversa com uma máquina (<c>GET /api/remoto/maquinas/{id}/conversa</c>), do mais antigo ao mais novo, e o
/// <see cref="UltimoEvento"/>: o site abre o SSE com ele no <c>Last-Event-ID</c> e não perde nada entre as duas chamadas. As
/// <see cref="Falas"/> (campo novo) são a conversa do chat do dev.kit, pela ordem em que foram ditas.
/// </summary>
public sealed record ConversaDaMaquinaV1(
    IReadOnlyList<ComandoRemotoV1> Comandos, IReadOnlyList<RespostaRemotaV1> Respostas, long UltimoEvento, IReadOnlyList<FalaRemotaV1> Falas);

/// <summary>
/// Um evento do SSE da conversa (<c>GET /api/remoto/conversa/eventos</c>): o comando que mudou de estado, a resposta que
/// chegou ou a fala do dev.kit (<see cref="Fala"/>, campo novo). O <see cref="Id"/> cresce: é o <c>id:</c> do SSE, e o
/// <c>Last-Event-ID</c> retoma dali.
/// </summary>
/// <param name="Tipo">Um de <see cref="ConversaV1.TiposDeEvento"/>.</param>
public sealed record EventoDaConversaV1(
    long Id, string Tipo, string MaquinaId, ComandoRemotoV1? Comando, RespostaRemotaV1? Resposta, DateTimeOffset Em, FalaRemotaV1? Fala = null);

/// <summary>
/// A conversa e os comandos remotos (fase 2 do controle remoto), no MESMO contrato v1 do <see cref="RemotoV1"/>
/// (<c>docs/contrato-remoto-v1.md</c>, seção 4): campo novo é opcional; o estado novo entra no fim das listas.
/// </summary>
public static class ConversaV1
{
    public const string ComandoMensagem = "mensagem";
    public const string ComandoLigar = "ligar";
    public const string ComandoReiniciar = "reiniciar";

    /// <summary>
    /// O HANDSHAKE do chat remoto (campo novo, contrato seção 5): o site pede o catálogo da máquina, e o dev.kit responde com o
    /// <c>PUT /api/remoto/catalogo</c> e o <c>concluido</c>. Não abre turno do agente, então não conta no limite por hora; o
    /// dev.kit antigo o recusa (<c>recusado</c>), e o site segue com o último catálogo e o texto livre.
    /// </summary>
    public const string ComandoCatalogo = "catalogo";

    /// <summary>
    /// (remote-control-v3, item 4) CRIAR um projeto do Orquestrador pelo site: o <c>texto</c> é o JSON de
    /// <see cref="NovoProjetoRemotoV1"/>, e o dev.kit o executa pelo mesmo caminho do <c>devcli projeto --criar</c>. O projeto
    /// nasce em rascunho: o site não aprova nem dispara. O dev.kit antigo o recusa (<c>recusado</c>), com o motivo.
    /// </summary>
    public const string ComandoProjeto = "projeto";

    /// <summary>
    /// (remote-control-v3, item 4) CRIAR (ou abrir) o projeto de design de um repositório pelo site: o <c>texto</c> é o JSON de
    /// <see cref="NovoDesignRemotoV1"/>, executado pelo mesmo caminho do <c>devcli design --repo</c>. Não grava nem envia versão.
    /// </summary>
    public const string ComandoDesign = "design";

    /// <summary>
    /// O que o site manda: a mensagem ao Orquestrador, ao serviço ligar ou reiniciar o dev.kit, o pedido do catálogo e a criação
    /// de projeto e de projeto de design (o novo entra no fim).
    /// </summary>
    public static IReadOnlyList<string> TiposDeComando { get; } =
        [ComandoMensagem, ComandoLigar, ComandoReiniciar, ComandoCatalogo, ComandoProjeto, ComandoDesign];

    /// <summary>Os comandos de CRIAÇÃO: o <c>texto</c> é o formulário em JSON, conferido pela API antes da fila.</summary>
    public static IReadOnlyList<string> TiposDeCriacao { get; } = [ComandoProjeto, ComandoDesign];

    public const string EstadoPendente = "pendente";
    public const string EstadoNaFila = "na-fila";
    public const string EstadoEntregue = "entregue";
    public const string EstadoRespondido = "respondido";
    public const string EstadoConcluido = "concluido";
    public const string EstadoRecusado = "recusado";
    public const string EstadoExpirado = "expirado";
    public const string EstadoFalhou = "falhou";

    /// <summary>
    /// O caminho do comando: <c>pendente</c> (no SQL, entrando na fila) → <c>na-fila</c> → <c>entregue</c> (o dev.kit
    /// confirmou) → <c>respondido</c>, <c>concluido</c> ou <c>recusado</c>. O TTL vencido antes da entrega é <c>expirado</c>; a
    /// fila fora do ar, <c>falhou</c> (o reenvio com o mesmo id tenta de novo).
    /// </summary>
    public static IReadOnlyList<string> EstadosDoComando { get; } =
        [EstadoPendente, EstadoNaFila, EstadoEntregue, EstadoRespondido, EstadoConcluido, EstadoRecusado, EstadoExpirado, EstadoFalhou];

    public const string RespostaMensagem = "mensagem";
    public const string RespostaConcluido = "concluido";
    public const string RespostaRecusado = "recusado";

    /// <summary>
    /// O que o dev.kit manda: a mensagem do Orquestrador (o comando vai a <c>respondido</c>), o comando executado
    /// (<c>concluido</c>, como o ligar) e o que ele não executa (<c>recusado</c>, com o motivo: o chat não aprova nada).
    /// </summary>
    public static IReadOnlyList<string> TiposDeResposta { get; } = [RespostaMensagem, RespostaConcluido, RespostaRecusado];

    public const string EventoComando = "comando";
    public const string EventoResposta = "resposta";
    public const string EventoFala = "fala";

    /// <summary>Os eventos do SSE; o novo entra no fim (o site que não o conhece o ignora).</summary>
    public static IReadOnlyList<string> TiposDeEvento { get; } = [EventoComando, EventoResposta, EventoFala];

    public const string AutorOrquestrador = "orquestrador";
    public const string AutorDev = "dev";

    /// <summary>Quem falou no chat do dev.kit: o Orquestrador ou o desenvolvedor.</summary>
    public static IReadOnlyList<string> AutoresDaFala { get; } = [AutorOrquestrador, AutorDev];

    /// <summary>A fala escrita no chat do próprio dev.kit, na máquina.</summary>
    public const string OrigemDesktop = "desktop";

    /// <summary>Onde a fala nasceu: no dev.kit (<see cref="OrigemDesktop"/>) ou no site (<see cref="OrigemRemoto"/>, por um comando).</summary>
    public static IReadOnlyList<string> OrigensDaFala { get; } = [OrigemDesktop, OrigemRemoto];

    public const string DispositivoNavegador = "navegador";
    public const string DispositivoCelular = "celular";

    /// <summary>De onde o site mandou; vazio é <see cref="DispositivoNavegador"/>.</summary>
    public static IReadOnlyList<string> Dispositivos { get; } = [DispositivoNavegador, DispositivoCelular];

    /// <summary>A origem marcada na mensagem da fila: o dev.kit mostra que a ordem veio do site.</summary>
    public const string OrigemRemoto = "remoto";

    /// <summary>A única permissão da SAS do serviço: <c>p</c> (process) — ler e apagar mensagens.</summary>
    public const string PermissaoDaSas = "p";

    public const int TamanhoMaximoDoTexto = 4000;

    /// <summary>O maior corpo dos envios da conversa (o comando, o recebimento, a resposta e o pedido da SAS).</summary>
    public const int TamanhoMaximoDoEnvio = 32 * 1024;

    /// <summary>Quantos comandos e respostas o histórico devolve (os mais recentes).</summary>
    public const int MaximoDoHistorico = 200;

    /// <summary>O <c>codigo</c> do 409 de quem ainda não tem a fila pronta (a ação "Fila do controle remoto" da conta).</summary>
    public const string CodigoSemFila = "sem-fila";

    /// <summary>O <c>codigo</c> do 409 do reinício sem confirmação com o turno em andamento (o "reinício seguro").</summary>
    public const string CodigoTurnoEmAndamento = "turno-em-andamento";

    /// <summary>O <c>codigo</c> do 409 do recebimento ou da resposta de uma máquina que não é a do comando.</summary>
    public const string CodigoOutraMaquina = "outra-maquina";

    /// <summary>O <c>codigo</c> do 429 do limite de envios por usuário (cada mensagem pode abrir um turno do agente).</summary>
    public const string CodigoLimiteDeEnvio = "limite-de-envio";

    /// <summary>O <c>codigo</c> do 503 quando a fila não aceitou a mensagem: o comando fica <c>falhou</c>.</summary>
    public const string CodigoFilaIndisponivel = "fila-indisponivel";

    /// <summary>O <c>codigo</c> do 409 do reenvio com o mesmo id e outro conteúdo.</summary>
    public const string CodigoIdEmUso = "id-em-uso";
}
