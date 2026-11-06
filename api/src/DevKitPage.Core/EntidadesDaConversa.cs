namespace DevKitPage.Core;

/// <summary>
/// Um comando do site para UMA máquina (a mensagem ao Orquestrador, ligar ou reiniciar), com o estado do caminho até ela. O
/// <see cref="ComandoId"/> é o id que viaja na fila; é único por usuário, e o reenvio com ele não cria outro.
/// </summary>
public sealed class ComandoRemoto
{
    public long Id { get; set; }

    /// <summary>O dono: só ele lê, e só a máquina dele recebe.</summary>
    public int UsuarioId { get; set; }

    public string ComandoId { get; set; } = string.Empty;
    public string MaquinaId { get; set; } = string.Empty;

    /// <summary>Um de <c>ConversaV1.TiposDeComando</c>.</summary>
    public string Tipo { get; set; } = string.Empty;

    public string Texto { get; set; } = string.Empty;

    /// <summary>Um de <c>ConversaV1.Dispositivos</c>.</summary>
    public string Dispositivo { get; set; } = string.Empty;

    /// <summary>Um de <c>ConversaV1.EstadosDoComando</c>.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>A fila (o recurso da provisão) por onde o comando foi.</summary>
    public long? RecursoId { get; set; }

    /// <summary>A falha da fila, já mascarada; vazia sem falha.</summary>
    public string Erro { get; set; } = string.Empty;

    public DateTime CriadoEmUtc { get; set; }

    /// <summary>O fim do TTL: sem o recebimento até aqui, o comando é <c>expirado</c> — e a fila também o descarta.</summary>
    public DateTime ExpiraEmUtc { get; set; }

    public DateTime? EntregueEmUtc { get; set; }
    public DateTime? ConcluidoEmUtc { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
}

/// <summary>
/// Uma resposta do dev.kit, recebida por HTTPS. Única por (<see cref="UsuarioId"/>, <see cref="MaquinaId"/>,
/// <see cref="RespostaId"/>): o reenvio do dev.kit não a duplica.
/// </summary>
public sealed class RespostaRemota
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public string MaquinaId { get; set; } = string.Empty;
    public string RespostaId { get; set; } = string.Empty;

    /// <summary>O <see cref="ComandoRemoto.ComandoId"/> respondido; vazio na mensagem espontânea do Orquestrador.</summary>
    public string ComandoId { get; set; } = string.Empty;

    /// <summary>Um de <c>ConversaV1.TiposDeResposta</c>.</summary>
    public string Tipo { get; set; } = string.Empty;

    public string Texto { get; set; } = string.Empty;

    /// <summary>Quando o dev.kit gerou (o relógio da máquina, informativo).</summary>
    public DateTime GeradaEmUtc { get; set; }

    public DateTime RecebidaEmUtc { get; set; }
}

/// <summary>
/// Uma fala do chat do Orquestrador no dev.kit, com quem falou e onde nasceu. Única por (<see cref="UsuarioId"/>,
/// <see cref="MaquinaId"/>, <see cref="FalaId"/>): o reenvio do dev.kit não a duplica. Não muda o estado de comando nenhum.
/// </summary>
public sealed class FalaRemota
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public string MaquinaId { get; set; } = string.Empty;
    public string FalaId { get; set; } = string.Empty;

    /// <summary>Um de <c>ConversaV1.AutoresDaFala</c>.</summary>
    public string Autor { get; set; } = string.Empty;

    /// <summary>Um de <c>ConversaV1.OrigensDaFala</c>.</summary>
    public string Origem { get; set; } = string.Empty;

    public string Texto { get; set; } = string.Empty;

    /// <summary>O <see cref="ComandoRemoto.ComandoId"/> que trouxe a fala de origem remota; vazio na do desktop.</summary>
    public string ComandoId { get; set; } = string.Empty;

    /// <summary>Quando foi dita (o relógio da máquina): a ordem da conversa.</summary>
    public DateTime DitaEmUtc { get; set; }

    public DateTime RecebidaEmUtc { get; set; }
}

/// <summary>
/// Um evento da conversa, só acrescentado: o comando que mudou de estado ou a resposta que chegou, com a foto dela em JSON.
/// O <see cref="Id"/> é o <c>id:</c> do SSE — o site retoma do último que viu, e nada se perde entre o histórico e o SSE.
/// </summary>
public sealed class EventoDaConversa
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public string MaquinaId { get; set; } = string.Empty;

    /// <summary>Um de <c>ConversaV1.TiposDeEvento</c>.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>O <c>EventoDaConversaV1</c> inteiro, em JSON (o comando ou a resposta como estavam naquele momento).</summary>
    public string Conteudo { get; set; } = string.Empty;

    public DateTime EmUtc { get; set; }
}
