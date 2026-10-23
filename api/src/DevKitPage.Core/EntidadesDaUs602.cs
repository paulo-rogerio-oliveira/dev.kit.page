namespace DevKitPage.Core;

/// <summary>
/// Um RELATO do usuário do dev.kit (US #602): defeito, sugestão ou dúvida. Ligado a quem relatou e à empresa dele (as
/// claims do token, nunca o corpo) — é a empresa que decide quem administra: o admin geral vê todos, o gestor só os da
/// empresa dele. O texto e o contexto são gravados JÁ mascarados (<see cref="RegrasDosRelatos.Mascarar"/>).
/// </summary>
public sealed class Relato
{
    public long Id { get; set; }

    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    /// <summary>A empresa de quem relatou; nula para quem não tem empresa (o admin, o usuário avulso).</summary>
    public int? EmpresaId { get; set; }

    public Empresa? Empresa { get; set; }

    /// <summary>Uma de <c>CategoriasDeRelato</c>.</summary>
    public string Categoria { get; set; } = string.Empty;

    public string Texto { get; set; } = string.Empty;

    /// <summary>O contexto consentido e mascarado; vazio sem consentimento.</summary>
    public string Contexto { get; set; } = string.Empty;

    /// <summary>A captura (PNG ou JPEG), com o teto do contrato; nula sem captura.</summary>
    public byte[]? Captura { get; set; }

    public string VersaoDevKit { get; set; } = string.Empty;

    /// <summary>O id que o dev.kit gerou ao enfileirar: único por usuário, torna o reenvio da fila inofensivo.</summary>
    public string ChaveDoCliente { get; set; } = string.Empty;

    /// <summary>Um de <c>EstadosDeRelato</c>.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>A resposta de quem administra; vazia enquanto não houver.</summary>
    public string Resposta { get; set; } = string.Empty;

    public int? RespondidoPorId { get; set; }
    public Usuario? RespondidoPor { get; set; }
    public DateTime? RespondidoEmUtc { get; set; }

    public DateTime CriadoEmUtc { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
}
