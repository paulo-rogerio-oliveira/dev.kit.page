using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>O vínculo de acesso compartilhado por design e projetos em ondas.</summary>
public interface IProjetoComAcessos
{
    long Id { get; }
    int? EmpresaId { get; }
    int DonoId { get; set; }
    DateTime AtualizadoEmUtc { get; set; }
    string NomeDoAcesso { get; }
    List<AcessoAoProjeto> Acessos { get; }
}

public interface IAcessosAosProjetos
{
    Task<ResultadoDaGestao<AcessosDoProjetoV1>> ListarAsync(EscopoDoDesign escopo, string recurso, long projeto, CancellationToken ct);
    Task<ResultadoDaGestao<AcessosDoProjetoV1>> ConcederAsync(EscopoDoDesign escopo, string recurso, long projeto, ConcessaoDeAcessoV1 pedido, CancellationToken ct);
    Task<ResultadoDaGestao<AcessosDoProjetoV1>> AlterarAsync(EscopoDoDesign escopo, string recurso, long projeto, int usuario, string? papel, CancellationToken ct);
    Task<ResultadoDaGestao<AcessosDoProjetoV1>> RemoverAsync(EscopoDoDesign escopo, string recurso, long projeto, int usuario, CancellationToken ct);
    Task<ResultadoDaGestao<AcessosDoProjetoV1>> TransferirAsync(EscopoDoDesign escopo, string recurso, long projeto, TransferenciaDeDonoV1 pedido, CancellationToken ct);
}

public static class RegrasDeAcesso
{
    public static string Papel(EscopoDoDesign escopo, IProjetoComAcessos projeto)
        => RegrasDoDesign.PapelNoProjeto(escopo, projeto.DonoId, projeto.EmpresaId,
            projeto.Acessos.FirstOrDefault(a => a.UsuarioId == escopo.UsuarioId)?.Papel);

    public static bool PodeEditar(string papel) => papel is AcessoV1.PapelDono or AcessoV1.PapelEditor;
}
