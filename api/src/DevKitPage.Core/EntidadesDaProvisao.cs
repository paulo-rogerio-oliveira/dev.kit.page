namespace DevKitPage.Core;

/// <summary>
/// A liberação da provisão de um usuário pelo admin (projeto controle-remoto, item 4). Uma linha por usuário: revogar desliga
/// <see cref="Liberada"/> e guarda quem e quando, em vez de apagar. A outra origem possível é o plano da assinatura
/// (<see cref="OpcoesDaProvisao.PlanosQueLiberam"/>), que não precisa de linha aqui.
/// </summary>
public sealed class LiberacaoDeProvisao
{
    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    public bool Liberada { get; set; }

    /// <summary>O login de quem liberou ou revogou por último.</summary>
    public string AlteradaPor { get; set; } = string.Empty;

    public DateTime AlteradaEmUtc { get; set; }
}

/// <summary>
/// Um recurso do usuário no Azure e o pedido que o move: a API grava, o trabalhador da provisão executa. Um por (usuário, tipo,
/// geração): a geração sobe a cada recurso novo do mesmo tipo depois de um removido, e o índice único dela é o que impede dois
/// pedidos simultâneos de criarem duas filas. Os nomes no Azure saem da geração, então repetir a operação acerta o MESMO recurso.
/// </summary>
public sealed class RecursoNaNuvem
{
    public long Id { get; set; }

    /// <summary>O dono: o usuário do token. Ninguém mais lê nem move o recurso.</summary>
    public int UsuarioId { get; set; }

    public Usuario? Usuario { get; set; }

    /// <summary>Hoje só <c>ProvisaoV1.TipoFila</c>.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>1 no primeiro recurso do tipo; o próximo depois de um removido é 2, e assim por diante.</summary>
    public int Geracao { get; set; }

    /// <summary><c>criar</c> ou <c>remover</c>.</summary>
    public string Operacao { get; set; } = string.Empty;

    /// <summary>Um dos estados de <c>ProvisaoV1</c>.</summary>
    public string Estado { get; set; } = string.Empty;

    public string Regiao { get; set; } = string.Empty;
    public string GrupoDeRecursos { get; set; } = string.Empty;

    /// <summary>A conta de armazenamento, de nome único no Azure (<see cref="RegrasDaProvisao.NomeDaConta"/>).</summary>
    public string Conta { get; set; } = string.Empty;

    public string Fila { get; set; } = string.Empty;

    /// <summary>As falhas da operação corrente: no <see cref="OpcoesDaProvisao.MaximoDeTentativas"/>, o recurso vai a falhou.</summary>
    public int Tentativas { get; set; }

    /// <summary>A partir de quando o trabalhador pega o pedido: a espera entre uma falha e a tentativa seguinte.</summary>
    public DateTime ProximaTentativaEmUtc { get; set; }

    /// <summary>Até quando o trabalhador que o pegou o tem. Vencida, outro trabalhador retoma: o que caiu no meio não trava o pedido.</summary>
    public DateTime? ConcessaoAteUtc { get; set; }

    /// <summary>O trabalhador que tem a concessão; só ele grava o desfecho dela.</summary>
    public string Trabalhador { get; set; } = string.Empty;

    /// <summary>O motivo da última falha, já sem segredo nem identificador (<see cref="RegrasDaProvisao.MensagemSegura"/>).</summary>
    public string UltimoErro { get; set; } = string.Empty;

    /// <summary>A chave do pedido no cliente: o reenvio do mesmo pedido devolve este recurso.</summary>
    public string ChaveDoCliente { get; set; } = string.Empty;

    public DateTime PedidoEmUtc { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
    public DateTime? ProntoEmUtc { get; set; }

    public List<EventoDaProvisao> Eventos { get; set; } = [];
}

/// <summary>A auditoria de um recurso: cada mudança de estado, com o autor e o motivo. Não é expurgada com o recurso removido.</summary>
public sealed class EventoDaProvisao
{
    public long Id { get; set; }

    public long RecursoId { get; set; }

    public RecursoNaNuvem? Recurso { get; set; }

    public DateTime EmUtc { get; set; }
    public string Operacao { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;

    /// <summary><c>usuario</c> ou <c>trabalhador:&lt;nome&gt;</c>.</summary>
    public string Autor { get; set; } = string.Empty;

    public string Mensagem { get; set; } = string.Empty;
}
