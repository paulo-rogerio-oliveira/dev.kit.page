using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Identidade comum de Projetos e Design, inclusive para contas sem empresa.</summary>
public static class RegrasDeCompartilhamento
{
    public static string NivelEfetivo(EscopoDoDesign escopo, string? pedido)
    {
        if (escopo.EmpresaId is null) return NiveisDeConvencao.Usuario;
        var nivel = (pedido ?? "").Trim().ToLowerInvariant();
        return nivel.Length == 0 ? NiveisDeConvencao.Empresa : nivel;
    }

    public static string EspacoDe(EscopoDoDesign escopo)
        => escopo.EmpresaId is { } empresa ? $"empresa:{empresa}" : $"usuario:{escopo.UsuarioId}";
}
