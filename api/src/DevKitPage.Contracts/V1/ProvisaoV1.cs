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
/// <param name="Aplicativos">
/// Os sites e APIs que o usuário pode pedir (<see cref="ProvisaoV1.TipoAppService"/> e <see cref="ProvisaoV1.TipoStaticWebApp"/>):
/// a cota, as regiões e os planos de cada tipo. Vazio numa API anterior a eles (item 12).
/// </param>
/// <param name="LimiteDeCusto">O teto do custo estimado do mês dos aplicativos, em reais.</param>
/// <param name="AlertaDeCustoPercentual">A partir de quantos por cento do teto o site avisa (e a auditoria registra).</param>
public sealed record PermissaoDeProvisaoV1(
    bool Liberada, string Origem, IReadOnlyList<string> Regioes, string RegiaoPadrao, int MaximoDeFilas,
    IReadOnlyList<TipoDeAplicativoV1>? Aplicativos = null, decimal LimiteDeCusto = 0, int AlertaDeCustoPercentual = 0);

/// <summary>Um tipo de aplicativo que o usuário pode pedir: quantos ativos cabem, onde e em que plano.</summary>
/// <param name="Tipo">Um de <see cref="ProvisaoV1.TiposDeAplicativo"/>.</param>
/// <param name="Maximo">A cota de recursos ATIVOS deste tipo por usuário.</param>
public sealed record TipoDeAplicativoV1(string Tipo, int Maximo, IReadOnlyList<string> Regioes, string RegiaoPadrao, IReadOnlyList<PlanoDeAplicativoV1> Planos);

/// <summary>Um plano (o SKU do Azure) com o custo ESTIMADO do mês, em reais — o que entra na conta do alerta de custo.</summary>
/// <param name="Id">O SKU como o Azure o chama (<c>F1</c>, <c>B1</c>, <c>Free</c>, <c>Standard</c>).</param>
/// <param name="Rotulo">Como o site o mostra (<c>B1 · Linux</c>).</param>
public sealed record PlanoDeAplicativoV1(string Id, string Rotulo, decimal CustoMensal);

/// <summary>
/// O pedido de um aplicativo (<c>POST /api/provisao/aplicativos</c>). O <c>Nome</c> é o do usuário (3 a 20
/// caracteres: minúsculas, dígitos e hífen); o nome no Azure ganha o prefixo do tipo, o usuário e um sufixo único.
/// </summary>
public sealed record PedidoDeAplicativoV1(string? Tipo, string? Nome, string? Regiao, string? Plano);

/// <summary>A remoção que pede o segundo fator (<c>POST /api/provisao/recursos/{id}/remover</c>): o código do aplicativo autenticador.</summary>
public sealed record RemocaoComSegundoFatorV1(string? Codigo);

/// <summary>
/// O uso da cota e do custo do usuário (<c>GET /api/provisao/cota</c>): é o que a seção mostra nos cartões e o que a API
/// confere a cada pedido. <see cref="Alerta"/> a partir do percentual; <see cref="Bloqueado"/> com o custo no teto.
/// </summary>
public sealed record CotaDaProvisaoV1(
    IReadOnlyList<UsoDoTipoV1> Tipos, decimal CustoDoMes, decimal LimiteDeCusto, int AlertaDeCustoPercentual, bool Alerta, bool Bloqueado);

/// <summary>Quantos recursos ATIVOS do tipo o usuário tem, e quantos cabem.</summary>
public sealed record UsoDoTipoV1(string Tipo, int Ativos, int Maximo);

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
/// <param name="Rotulo">O nome que o USUÁRIO deu ao aplicativo (<c>api-pedidos</c>); vazio na fila.</param>
/// <param name="Plano">O plano do aplicativo (um <see cref="PlanoDeAplicativoV1.Id"/>); vazio na fila.</param>
/// <param name="NomeDoPlano">O plano do App Service no Azure (<c>asp-...</c>), criado ANTES do aplicativo; vazio nos outros tipos.</param>
/// <param name="CustoMensal">O custo estimado do mês do plano, em reais (zero na fila e nos planos gratuitos).</param>
/// <param name="Passos">Os passos da criação já feitos no Azure (<see cref="ProvisaoV1.PassoPlano"/>): a repetição retoma do seguinte.</param>
/// <param name="Parcial">
/// A criação falhou DEPOIS de um passo feito: algo ficou no Azure (o plano) e o resto não. Repetir retoma do que faltou;
/// remover apaga também o que ficou.
/// </param>
public sealed record RecursoProvisionadoV1(
    long Id, string Tipo, string Nome, string Fila, string Regiao, string GrupoDeRecursos, string Operacao, string Estado, int Tentativas,
    string Erro, string Endpoint, DateTimeOffset CriadoEm, DateTimeOffset AtualizadoEm, DateTimeOffset? ProntoEm, DateTimeOffset? ProximaTentativaEm,
    string Rotulo = "", string Plano = "", string NomeDoPlano = "", decimal CustoMensal = 0, IReadOnlyList<string>? Passos = null, bool Parcial = false);

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

    /// <summary>O site ou a API num App Service Linux, com o plano dele (fase 4, item 12).</summary>
    public const string TipoAppService = "app-service";

    /// <summary>O site estático num Static Web App (fase 4, item 12).</summary>
    public const string TipoStaticWebApp = "static-web-app";

    /// <summary>Os tipos de recurso: a fila (item 4) e os aplicativos da fase 4.</summary>
    public static IReadOnlyList<string> Tipos { get; } = [TipoFila, TipoAppService, TipoStaticWebApp];

    /// <summary>Os tipos pedidos em <c>POST /aplicativos</c>: têm cota por tipo e custo, e a remoção pede o segundo fator.</summary>
    public static IReadOnlyList<string> TiposDeAplicativo { get; } = [TipoAppService, TipoStaticWebApp];

    /// <summary>O passo do App Service que cria o plano (o <c>serverfarm</c>), antes do aplicativo.</summary>
    public const string PassoPlano = "plano";

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

    /// <summary>Um passo da criação feito no Azure (o plano do App Service): é o que a falha parcial deixa para trás.</summary>
    public const string EventoPasso = "passo";

    /// <summary>O pedido levou o custo estimado do mês ao alerta (<see cref="PermissaoDeProvisaoV1.AlertaDeCustoPercentual"/>).</summary>
    public const string EventoAlertaDeCusto = "alerta-de-custo";

    public static IReadOnlyList<string> Eventos { get; } =
    [
        EventoPedido, EventoInicio, EventoPronto, EventoFalhaTransitoria, EventoFalha, EventoRepeticao, EventoRemocaoPedida, EventoRemovido,
        EventoLiberado, EventoRetirado, EventoPasso, EventoAlertaDeCusto,
    ];

    /// <summary>O autor das linhas da auditoria escritas pelo worker.</summary>
    public const string AutorWorker = "worker";

    /// <summary>O nome da fila de comandos dentro da conta do usuário.</summary>
    public const string NomeDaFila = "comandos";

    /// <summary>O <c>codigo</c> do 403 de quem não tem a provisão liberada.</summary>
    public const string CodigoNaoLiberada = "provisao-nao-liberada";

    /// <summary>O <c>codigo</c> do 409 do aplicativo pedido com a cota do tipo cheia.</summary>
    public const string CodigoCotaCheia = "cota-cheia";

    /// <summary>O <c>codigo</c> do 409 do pedido que passaria do teto do custo do mês.</summary>
    public const string CodigoCustoNoLimite = "custo-no-limite";

    /// <summary>O <c>codigo</c> do 403 da remoção de aplicativo sem o segundo fator (ou com o código errado).</summary>
    public const string CodigoSegundoFator = "segundo-fator";
}
