using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Listagem sem materializar documentos, itens, acessos ou validar grafos de projetos fechados.</summary>
public interface IConsultaDeProjetos
{
    Task<IReadOnlyList<ResumoDoProjetoV1>> ListarResumosAsync(
        EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct);

    /// <summary>
    /// A pendência do pedido remoto <paramref name="acao"/> (<see cref="DisparoDeProjetoRemoto.Acoes"/>) no projeto, pela
    /// MESMA conferência da fila — situação, papel e matriz —; nula quando o pedido seria aceito.
    /// </summary>
    Task<string?> PendenciaDoDisparoAsync(EscopoDoDesign escopo, string slug, string acao, CancellationToken ct)
        => Task.FromResult<string?>("Conferência de disparo indisponível nesta versão.");
}
