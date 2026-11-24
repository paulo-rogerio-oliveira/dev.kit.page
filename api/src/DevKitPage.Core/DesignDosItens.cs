using System.Text.Json;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

public static class DesignDosItens
{
    public static string Validar(DesignDoItemV1? design)
    {
        if (design is null) return "";
        if (design.Revisao < 1 || design.Propostas is null || design.Propostas.Count > ProjetosV1.MaximoDeAnexos)
            return "Informe a revisão explícita e a lista de propostas do planejamento (até 50).";
        if (!design.AlteraTela && design.Propostas.Count > 0) return "Propostas visuais exigem AlteraTela=true.";
        foreach (var referencia in design.Propostas)
            if (referencia is null || !Id(referencia.Projeto) || !Id(referencia.Tela) || referencia.Versao < 1
                || referencia.Hash is null || !Regex.IsMatch(referencia.Hash, "\\A[0-9a-fA-F]{64}\\z", RegexOptions.CultureInvariant))
                return "Referência de proposta inválida: informe projeto/tela/versão/hash SHA-256.";
        return design.Propostas.Select(p => (p.Projeto, p.Tela)).Distinct().Count() != design.Propostas.Count
            ? "Fixe somente uma versão por projeto/tela em cada revisão." : "";
    }

    public static string Mudanca(DesignDoItemV1? anterior, DesignDoItemV1? atual)
    {
        if (anterior is null || atual is null || JsonSerializer.Serialize(anterior) == JsonSerializer.Serialize(atual)) return "";
        if (atual.Revisao <= anterior.Revisao) return "Alterar propostas exige nova revisão explícita e nova aprovação do plano.";
        foreach (var proposta in atual.Propostas)
        {
            var antiga = anterior.Propostas.FirstOrDefault(p => p.Projeto == proposta.Projeto && p.Tela == proposta.Tela);
            if (antiga is not null && antiga != proposta && proposta.Versao <= antiga.Versao)
                return "A proposta fixada não pode ser sobrescrita: crie uma nova versão.";
        }
        return "";
    }

    private static bool Id(string? valor) => valor is not null
        && Regex.IsMatch(valor, "\\A[a-z0-9][a-z0-9_-]{0,199}\\z", RegexOptions.CultureInvariant);
}
