using System.Text.Json.Serialization;

namespace DevKitPage.Contracts.V1;

/// <summary>
/// O contrato v1 da provisão (projeto controle-remoto, item 4): o usuário pede um recurso no Azure pelo site, e um trabalhador
/// SEPARADO da API, com identidade própria, o cria. A API só grava o pedido e lê o estado: ela não tem permissão nenhuma no
/// Azure. O primeiro recurso é a fila do usuário (uma Storage Queue num grupo de recursos dele); App Service e Static Web App
/// entram depois pelo mesmo caminho. O pedido é assíncrono e repetível: <see cref="Pedido"/>, <see cref="EmProvisao"/>,
/// <see cref="Pronto"/> ou <see cref="Falhou"/>, e a remoção termina em <see cref="Removido"/>.
/// </summary>
public static class ProvisaoV1
{
    /// <summary>A raiz das rotas deste contrato.</summary>
    public const string Rotas = "/api/provisao/v1";

    /// <summary>A fila de comandos do usuário: uma conta de armazenamento com uma Storage Queue.</summary>
    public const string TipoFila = "fila";

    public const string OperacaoCriar = "criar";
    public const string OperacaoRemover = "remover";

    /// <summary>Gravado, à espera do trabalhador (também entre uma tentativa e a seguinte).</summary>
    public const string Pedido = "pedido";

    /// <summary>O trabalhador está criando ou removendo agora.</summary>
    public const string EmProvisao = "em-provisao";

    /// <summary>O recurso existe e está pronto para uso.</summary>
    public const string Pronto = "pronto";

    /// <summary>As tentativas acabaram ou o Azure recusou de vez; o motivo vai em <see cref="RecursoNaNuvemV1.UltimoErro"/>.</summary>
    public const string Falhou = "falhou";

    /// <summary>O recurso foi removido do Azure. A linha fica, com o histórico, para a auditoria.</summary>
    public const string Removido = "removido";

    /// <summary>A liberação veio do admin, usuário a usuário.</summary>
    public const string OrigemUsuario = "usuario";

    /// <summary>A liberação veio do plano da assinatura (o tier, quando existir).</summary>
    public const string OrigemPlano = "plano";

    /// <summary>O autor dos eventos que o próprio usuário causa no histórico (o pedido, a remoção, o repetir).</summary>
    public const string AutorUsuario = "usuario";

    /// <summary>O prefixo do autor dos eventos do trabalhador: <c>trabalhador:&lt;nome&gt;</c>.</summary>
    public const string AutorTrabalhador = "trabalhador";

    public const int TamanhoMaximoDaRegiao = 40;
    public const int TamanhoMaximoDaChaveDoCliente = 64;
    public const int TamanhoMaximoDaMensagem = 500;
}

/// <summary>A área de provisão de quem está logado: se ela está liberada, onde se pode criar e os recursos dele.</summary>
/// <param name="Liberada">O usuário pode pedir recursos novos. Falso sem a liberação, mesmo com recursos antigos.</param>
/// <param name="Origem">De onde vem a liberação (<see cref="ProvisaoV1.OrigemUsuario"/> ou <see cref="ProvisaoV1.OrigemPlano"/>); nula sem ela.</param>
/// <param name="Regioes">As regiões do Azure aceitas no pedido.</param>
/// <param name="RegiaoPadrao">A região sugerida pela tela.</param>
/// <param name="Recursos">Os recursos dele, do mais recente ao mais antigo, inclusive os removidos (sem o histórico).</param>
public sealed record SituacaoDaProvisaoV1(
    bool Liberada, string? Origem, IReadOnlyList<string> Regioes, string RegiaoPadrao, IReadOnlyList<RecursoNaNuvemV1> Recursos);

/// <summary>O pedido da fila do usuário. Os nomes no Azure saem do servidor, nunca do corpo.</summary>
/// <param name="Regiao">Uma de <see cref="SituacaoDaProvisaoV1.Regioes"/>.</param>
/// <param name="ChaveDoCliente">Opcional: o mesmo pedido enviado de novo (duplo clique, reenvio) devolve o mesmo recurso.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PedidoDeFilaV1(string Regiao, string? ChaveDoCliente = null);

/// <summary>Um recurso do usuário no Azure e onde ele está no ciclo.</summary>
/// <param name="Id">O id do recurso nesta API: é o que as rotas de repetir e remover recebem.</param>
/// <param name="Tipo">Hoje só <see cref="ProvisaoV1.TipoFila"/>.</param>
/// <param name="Operacao">A operação corrente ou a última: <see cref="ProvisaoV1.OperacaoCriar"/> ou <see cref="ProvisaoV1.OperacaoRemover"/>.</param>
/// <param name="Estado">Um dos estados de <see cref="ProvisaoV1"/>.</param>
/// <param name="Regiao">A região do Azure.</param>
/// <param name="GrupoDeRecursos">O grupo de recursos do usuário.</param>
/// <param name="Conta">A conta de armazenamento, de nome único no Azure.</param>
/// <param name="Fila">O nome da fila dentro da conta.</param>
/// <param name="Tentativas">As falhas da operação corrente; zera quando ela termina ou é repetida.</param>
/// <param name="UltimoErro">O motivo da última falha, sem segredo nem identificador do Azure; vazio sem falha.</param>
/// <param name="PedidoEm">Quando o recurso foi pedido.</param>
/// <param name="AtualizadoEm">A última mudança de estado.</param>
/// <param name="ProntoEm">Quando ficou pronto pela última vez; nulo se nunca ficou.</param>
/// <param name="Historico">A trilha de auditoria, do mais antigo ao mais recente; só na leitura de um recurso.</param>
public sealed record RecursoNaNuvemV1(
    long Id, string Tipo, string Operacao, string Estado, string Regiao, string GrupoDeRecursos, string Conta, string Fila,
    int Tentativas, string UltimoErro, DateTimeOffset PedidoEm, DateTimeOffset AtualizadoEm, DateTimeOffset? ProntoEm,
    IReadOnlyList<EventoDaProvisaoV1>? Historico = null);

/// <summary>Uma linha da auditoria: quem moveu o recurso, para onde e por quê.</summary>
/// <param name="Em">Quando (a hora do servidor).</param>
/// <param name="Operacao">A operação daquele momento.</param>
/// <param name="Estado">O estado em que o recurso ficou.</param>
/// <param name="Autor"><see cref="ProvisaoV1.AutorUsuario"/> ou <c>trabalhador:&lt;nome&gt;</c>.</param>
/// <param name="Mensagem">O que aconteceu, sem segredo nem identificador do Azure.</param>
public sealed record EventoDaProvisaoV1(DateTimeOffset Em, string Operacao, string Estado, string Autor, string Mensagem);

/// <summary>A liberação da provisão de um usuário, como o admin a vê e a grava.</summary>
/// <param name="UsuarioId">O usuário.</param>
/// <param name="Login">O login dele.</param>
/// <param name="Liberada">Liberada agora.</param>
/// <param name="AlteradaPor">Quem liberou ou revogou por último.</param>
/// <param name="AlteradaEm">Quando.</param>
public sealed record LiberacaoDaProvisaoV1(int UsuarioId, string Login, bool Liberada, string AlteradaPor, DateTimeOffset AlteradaEm);

/// <summary>O admin libera ou revoga a provisão de um usuário. Revogar não remove o que ele já criou.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DefinicaoDaLiberacaoV1(bool Liberada);
