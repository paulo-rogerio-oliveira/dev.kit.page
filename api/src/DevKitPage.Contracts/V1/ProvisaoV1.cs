namespace DevKitPage.Contracts.V1;

/// <summary>
/// O que o usuário pode provisionar (<c>GET /api/provisao/permissao</c>): a resposta de TODO usuário logado — é ela que
/// decide se a ação aparece na conta. Sem a liberação, as outras rotas de <c>/api/provisao</c> respondem 403.
/// </summary>
/// <param name="Liberada">O usuário pode pedir recursos.</param>
/// <param name="Origem">De onde vem a liberação: uma de <see cref="ProvisaoV1.OrigensDaLiberacao"/>.</param>
/// <param name="Regioes">As regiões do Azure aceitas no pedido.</param>
/// <param name="RegiaoPadrao">A região sugerida.</param>
/// <param name="MaximoDeFilas">A cota de filas ativas do usuário.</param>
public sealed record PermissaoDeProvisaoV1(bool Liberada, string Origem, IReadOnlyList<string> Regioes, string RegiaoPadrao, int MaximoDeFilas);

/// <summary>O pedido da fila do usuário (<c>POST /api/provisao/filas</c>). Sem região, a padrão.</summary>
public sealed record PedidoDeFilaV1(string? Regiao);

/// <summary>
/// Um recurso do usuário no Azure, como o site o mostra. A provisão é ASSÍNCRONA: o pedido nasce em
/// <see cref="ProvisaoV1.EstadoPedido"/>, o worker o leva a <see cref="ProvisaoV1.EstadoEmProvisao"/> e termina em
/// <see cref="ProvisaoV1.EstadoPronto"/> ou <see cref="ProvisaoV1.EstadoFalhou"/>. A remoção percorre os mesmos estados com a
/// <see cref="Operacao"/> <see cref="ProvisaoV1.OperacaoRemover"/> e termina em <see cref="ProvisaoV1.EstadoRemovido"/>.
/// </summary>
/// <param name="Tipo">Um de <see cref="ProvisaoV1.Tipos"/>.</param>
/// <param name="Nome">O nome do recurso no Azure (a conta de armazenamento da fila): único no Azure inteiro.</param>
/// <param name="Fila">O nome da fila dentro da conta.</param>
/// <param name="GrupoDeRecursos">O grupo de recursos do usuário, um por usuário.</param>
/// <param name="Operacao">Uma de <see cref="ProvisaoV1.Operacoes"/>.</param>
/// <param name="Estado">Um de <see cref="ProvisaoV1.Estados"/>.</param>
/// <param name="Tentativas">Quantas vezes o worker executou a operação atual.</param>
/// <param name="Erro">A última falha, já sem segredo; vazio sem falha.</param>
/// <param name="Endpoint">O endereço público da fila, quando pronta; vazio antes.</param>
/// <param name="ProximaTentativaEm">Quando o worker tenta de novo depois de uma falha transitória.</param>
public sealed record RecursoProvisionadoV1(
    long Id, string Tipo, string Nome, string Fila, string Regiao, string GrupoDeRecursos, string Operacao, string Estado, int Tentativas,
    string Erro, string Endpoint, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm, DateTimeOffset? ProntoEm, DateTimeOffset? ProximaTentativaEm);

/// <summary>Uma linha da AUDITORIA de um recurso: quem (o login ou o worker), o quê e quando.</summary>
/// <param name="Evento">Um de <see cref="ProvisaoV1.Eventos"/>.</param>
public sealed record EventoDaProvisaoV1(DateTimeOffset Em, string Evento, string Autor, string Detalhe);

/// <summary>O recurso com a auditoria, do mais antigo ao mais novo.</summary>
public sealed record DetalheDoRecursoV1(RecursoProvisionadoV1 Recurso, IReadOnlyList<EventoDaProvisaoV1> Eventos);

/// <summary>A liberação de um recurso a um usuário pelo admin (<c>PUT /api/usuarios/{id}/recursos/{recurso}</c>).</summary>
public sealed record LiberacaoDeRecursoV1(bool Liberado);

/// <summary>
/// O contrato da provisão (<c>docs/arquitetura.md</c>, "Motor de provisão"). Campo novo é opcional; o estado novo entra
/// no fim das listas.
/// </summary>
public static class ProvisaoV1
{
    /// <summary>O recurso de permissão que libera a provisão (a chave em <c>UsuarioResumo.RecursosLiberados</c>).</summary>
    public const string RecursoProvisao = "provisao";

    /// <summary>Os recursos que o admin libera por usuário.</summary>
    public static IReadOnlyList<string> RecursosLiberaveis { get; } = [RecursoProvisao];

    public const string TipoFila = "fila";

    /// <summary>Os tipos de recurso: a fila agora; o App Service e o Static Web App na fase 4.</summary>
    public static IReadOnlyList<string> Tipos { get; } = [TipoFila];

    public const string OperacaoCriar = "criar";
    public const string OperacaoRemover = "remover";
    public static IReadOnlyList<string> Operacoes { get; } = [OperacaoCriar, OperacaoRemover];

    public const string EstadoPedido = "pedido";
    public const string EstadoEmProvisao = "em-provisao";
    public const string EstadoPronto = "pronto";
    public const string EstadoFalhou = "falhou";
    public const string EstadoRemovido = "removido";
    public static IReadOnlyList<string> Estados { get; } = [EstadoPedido, EstadoEmProvisao, EstadoPronto, EstadoFalhou, EstadoRemovido];

    /// <summary>A liberação explícita do admin.</summary>
    public const string OrigemPermissao = "permissao";

    /// <summary>A liberação pelo plano da assinatura (o tier, quando existir: <c>Provisao:PlanosQueLiberam</c>).</summary>
    public const string OrigemPlano = "plano";

    public const string OrigemNenhuma = "nenhuma";
    public static IReadOnlyList<string> OrigensDaLiberacao { get; } = [OrigemPermissao, OrigemPlano, OrigemNenhuma];

    public const string EventoPedido = "pedido";
    public const string EventoInicio = "inicio";
    public const string EventoPronto = "pronto";
    public const string EventoFalhaTransitoria = "falha-transitoria";
    public const string EventoFalha = "falha";
    public const string EventoRepeticao = "repeticao";
    public const string EventoRemocaoPedida = "remocao-pedida";
    public const string EventoRemovido = "removido";
    public const string EventoLiberado = "liberado";
    public const string EventoRetirado = "retirado";

    public static IReadOnlyList<string> Eventos { get; } =
    [
        EventoPedido, EventoInicio, EventoPronto, EventoFalhaTransitoria, EventoFalha, EventoRepeticao, EventoRemocaoPedida, EventoRemovido,
        EventoLiberado, EventoRetirado,
    ];

    /// <summary>O autor das linhas da auditoria escritas pelo worker.</summary>
    public const string AutorWorker = "worker";

    /// <summary>O nome da fila de comandos dentro da conta do usuário.</summary>
    public const string NomeDaFila = "comandos";

    /// <summary>O <c>codigo</c> do 403 de quem não tem a provisão liberada.</summary>
    public const string CodigoNaoLiberada = "provisao-nao-liberada";
}
