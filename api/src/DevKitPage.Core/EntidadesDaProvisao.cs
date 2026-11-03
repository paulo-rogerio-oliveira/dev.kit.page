namespace DevKitPage.Core;

/// <summary>
/// Um recurso liberado pelo admin a um usuário (a provisão, hoje). Único por (<see cref="UsuarioId"/>, <see cref="Recurso"/>):
/// retirar apaga a linha, e a auditoria (<see cref="EventoDaProvisao"/>) guarda quem liberou e quem retirou.
/// </summary>
public sealed class PermissaoDeUsuario
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }

    /// <summary>Um de <c>ProvisaoV1.RecursosLiberaveis</c>.</summary>
    public string Recurso { get; set; } = string.Empty;

    /// <summary>O login do admin que liberou.</summary>
    public string ConcedidaPor { get; set; } = string.Empty;

    public DateTime ConcedidaEmUtc { get; set; }
}

/// <summary>
/// Um recurso do usuário no Azure e o PEDIDO que o cria ou remove — a mesma linha, porque o pedido é repetível: o worker
/// executa de novo a mesma operação, com os mesmos nomes, até ela dar certo ou falhar de vez. Nunca é apagado (o recurso
/// removido fica como <c>removido</c>, com a auditoria): é o rastro do que custa dinheiro.
/// </summary>
public sealed class RecursoProvisionado
{
    public long Id { get; set; }

    /// <summary>O dono: só ele lê, repete e remove.</summary>
    public int UsuarioId { get; set; }

    /// <summary>Um de <c>ProvisaoV1.Tipos</c>.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>O nome no Azure (a conta de armazenamento): ÚNICO aqui, e escolhido para ser único no Azure.</summary>
    public string Nome { get; set; } = string.Empty;

    public string Fila { get; set; } = string.Empty;
    public string Regiao { get; set; } = string.Empty;
    public string GrupoDeRecursos { get; set; } = string.Empty;

    /// <summary>Uma de <c>ProvisaoV1.Operacoes</c>: o que o worker faz no próximo pedido.</summary>
    public string Operacao { get; set; } = string.Empty;

    /// <summary>Um de <c>ProvisaoV1.Estados</c>.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>As execuções da operação atual (zera quando o usuário repete ou pede a remoção).</summary>
    public int Tentativas { get; set; }

    public string Erro { get; set; } = string.Empty;

    /// <summary>O id do recurso no Azure (<c>/subscriptions/.../storageAccounts/...</c>), quando criado.</summary>
    public string IdNoAzure { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    /// <summary>O pedido em <c>pedido</c> só volta ao worker a partir daqui (a espera da falha transitória).</summary>
    public DateTime? ProximaTentativaEmUtc { get; set; }

    /// <summary>Qual worker executa (o nome da instância): só ele conclui ou falha a execução.</summary>
    public string ReservadoPor { get; set; } = string.Empty;

    /// <summary>A reserva vence aqui: o worker que caiu no meio não prende o pedido para sempre.</summary>
    public DateTime? ReservadoAteUtc { get; set; }

    /// <summary>Sobe a cada gravação: dois workers não reservam o mesmo pedido (token de concorrência).</summary>
    public int Versao { get; set; }

    public DateTime CriadoEmUtc { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
    public DateTime? ProntoEmUtc { get; set; }
    public DateTime? RemovidoEmUtc { get; set; }
}

/// <summary>
/// Uma linha da auditoria da provisão: o pedido, cada execução do worker, a falha, a repetição, a remoção — e a liberação ou
/// retirada da permissão pelo admin (sem recurso). Só acrescenta.
/// </summary>
public sealed class EventoDaProvisao
{
    public long Id { get; set; }

    /// <summary>O recurso; nulo nos eventos da permissão.</summary>
    public long? RecursoId { get; set; }

    /// <summary>O usuário dono do recurso (ou o que recebeu a permissão).</summary>
    public int UsuarioId { get; set; }

    /// <summary>Um de <c>ProvisaoV1.Eventos</c>.</summary>
    public string Evento { get; set; } = string.Empty;

    /// <summary>O login de quem fez, ou <c>worker</c>.</summary>
    public string Autor { get; set; } = string.Empty;

    public string Detalhe { get; set; } = string.Empty;
    public DateTime EmUtc { get; set; }
}
