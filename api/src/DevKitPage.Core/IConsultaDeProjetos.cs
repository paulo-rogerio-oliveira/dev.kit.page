using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Listagem sem materializar documentos, itens, acessos ou validar grafos de projetos fechados.</summary>
public interface IConsultaDeProjetos
{
    Task<IReadOnlyList<ResumoDoProjetoV1>> ListarResumosAsync(
        EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct);
}
