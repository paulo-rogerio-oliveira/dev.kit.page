using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

public sealed record GravacaoDoProjeto(int Status, ProjetoV1? Projeto, string Mensagem = "");

public interface IProjetosCompartilhados
{
    Task<IReadOnlyList<ProjetoV1>> ListarAsync(EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct);
    Task<ProjetoV1?> ObterAsync(EscopoDoDesign escopo, long id, CancellationToken ct);
    Task<GravacaoDoProjeto> GravarAsync(EscopoDoDesign escopo, long? id, EnvioDeProjetoV1 envio, CancellationToken ct);
}
