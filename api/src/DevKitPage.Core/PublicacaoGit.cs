using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

public interface IPublicacaoGit
{
    Task<ResultadoDaGestao<ConferenciaGitV1>> ExecutarAsync(PublicacaoGitV1 pedido, bool aplicar, string autor, CancellationToken ct);
    Task<ReciboPublicacaoGitV1?> ReciboAsync(string sha, CancellationToken ct);
}

/// <summary>Uma origem Git so pode produzir uma release. A versao tambem e unica.</summary>
public sealed class ReciboDoCatalogoGit
{
    public string Sha { get; set; } = string.Empty;
    public long VersaoId { get; set; }
    public string Documento { get; set; } = string.Empty;
}
