using DevKitPage.Core;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A visibilidade dos projetos compartilhados escrita para o banco (a regra do design, <see cref="RegrasDoDesign.Visivel"/>): o
/// dono, ou quem tem acesso concedido na mesma empresa. A mesma consulta serve o projeto (<see cref="ProjetosDaEmpresa"/>) e os
/// artefatos dele (<see cref="ArtefatosDosProjetosNoBanco"/>), para os dois nunca divergirem.
/// </summary>
internal static class ConsultaDosProjetos
{
    public static IQueryable<ProjetoCompartilhado> Visiveis(this IQueryable<ProjetoCompartilhado> projetos, EscopoDoDesign escopo)
    {
        var empresa = escopo.EmpresaId;
        var eu = escopo.UsuarioId;
        return projetos.Where(p => p.DonoId == eu || (empresa != null && p.EmpresaId == empresa && p.Acessos.Any(a => a.UsuarioId == eu)));
    }
}
