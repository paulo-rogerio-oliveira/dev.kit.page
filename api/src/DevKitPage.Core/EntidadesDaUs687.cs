namespace DevKitPage.Core;

/// <summary>
/// Uma linha do histórico da licença de um usuário (US #687): o que o admin fez (definir a validade, tirar o prazo,
/// revogar) e o MOTIVO, obrigatório — é a trilha de quem estendeu a licença de quem, e por quê. As renovações do dev.kit
/// não ficam aqui: saem das <see cref="LicencaEmitida"/>, que já são a trilha delas.
/// </summary>
public sealed class HistoricoDeLicenca
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    /// <summary>Uma de <c>AcoesNaLicenca</c>.</summary>
    public string Acao { get; set; } = string.Empty;

    /// <summary>A frase do histórico ("validade definida até 27/11/2026 · individual").</summary>
    public string Descricao { get; set; } = string.Empty;

    public string Motivo { get; set; } = string.Empty;

    /// <summary>A validade depois da ação; nula sem prazo e na revogação.</summary>
    public DateTime? ValidaAteUtc { get; set; }

    public string Autor { get; set; } = string.Empty;
    public DateTime EmUtc { get; set; }
}

/// <summary>
/// A FOTO do último estado de alguma coisa numa máquina (US #687), no padrão do <see cref="RoiDeWorkItem"/>: uma linha por
/// (<see cref="MaquinaId"/>, <see cref="Tipo"/>, <see cref="Chave"/>), substituída na ingestão só por um evento MAIS NOVO
/// (<see cref="EmUtc"/>). É o que o "Saúde agora" do painel lê — a situação de cada task (chave = a sessão), a pergunta
/// aberta de cada task, o fechamento de cada work item e a última amostra de recursos da máquina (chave vazia). Não é
/// expurgada com os brutos: é o estado de agora, e não um histórico.
/// </summary>
public sealed class FotoDeEstado
{
    public long Id { get; set; }
    public int MaquinaId { get; set; }
    public Maquina? Maquina { get; set; }

    /// <summary>O tipo do evento que trouxe a foto (<c>TiposDeEvento</c>).</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>De quem é a foto: a sessão, o work item, ou vazio (a máquina).</summary>
    public string Chave { get; set; } = string.Empty;

    /// <summary>O recorte do evento (a situação, o desfecho, as medidas da amostra).</summary>
    public string Detalhe { get; set; } = string.Empty;

    public long? Valor { get; set; }

    /// <summary>O <c>em</c> do evento: é por ele que "o mais recente vence".</summary>
    public DateTime EmUtc { get; set; }

    public string EventId { get; set; } = string.Empty;
}

/// <summary>
/// Uma linha do HISTÓRICO de um item editável do catálogo (US #687): o rascunho salvo, descartado, publicado ou retirado,
/// com quem e quando. O conteúdo de cada versão publicada não fica aqui — está no zip da release.
/// </summary>
public sealed class HistoricoDeItemDoCatalogo
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public ItemDoCatalogoEditavel? Item { get; set; }

    /// <summary>Uma de <c>AcoesNoItem</c>.</summary>
    public string Acao { get; set; } = string.Empty;

    /// <summary>A versão do item que a ação gerou (na publicação) ou a de partida; 0 quando não se aplica.</summary>
    public int Versao { get; set; }

    public string Autor { get; set; } = string.Empty;
    public DateTime EmUtc { get; set; }
}

/// <summary>
/// Um arquivo ANEXO a um item do catálogo (US #687): o material de apoio de quem edita (o exemplo, a captura, o log).
/// Fica no site: não entra no zip da release, e a máquina nunca o recebe.
/// </summary>
public sealed class AnexoDeItemDoCatalogo
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public ItemDoCatalogoEditavel? Item { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string TipoDeConteudo { get; set; } = string.Empty;
    public long TamanhoBytes { get; set; }
    public byte[] Conteudo { get; set; } = [];
    public string Autor { get; set; } = string.Empty;
    public DateTime EmUtc { get; set; }
}
