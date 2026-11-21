using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Projeção portátil do mesmo grafo e ordenação do host; nunca regrava o plano aprovado.</summary>
public static class OndasEfetivasDosItens
{
    public static IReadOnlyList<ItemDoProjetoV1> Projetar(IReadOnlyList<ItemDoProjetoV1> itens)
    {
        var retirados = itens.Where(CicloDosItensV1.Retirado).Select(i => i.Numero).ToHashSet();
        var efetivos = itens.Where(i => !retirados.Contains(i.Numero))
            .Select(i => i with { Dependencias = i.Dependencias.Where(d => !retirados.Contains(d)).ToArray() }).ToArray();
        var ondas = new Dictionary<int, int>();
        var componentes = new Dictionary<int, HashSet<string>>();
        while (ondas.Count < efetivos.Length)
        {
            var proximo = efetivos.Where(i => !ondas.ContainsKey(i.Numero) && i.Dependencias.All(ondas.ContainsKey))
                .OrderBy(i => i.Numero).FirstOrDefault()
                ?? throw new ArgumentException("Grafo efetivo inválido: ciclo ou dependência ausente.");
            var onda = proximo.Dependencias.Count == 0 ? 1 : proximo.Dependencias.Max(d => ondas[d]) + 1;
            var toca = proximo.Componentes.Select(c => c.Trim()).Where(c => c.Length > 0).ToArray();
            while (componentes.TryGetValue(onda, out var ocupados) && toca.Any(ocupados.Contains)) onda++;
            if (!componentes.TryGetValue(onda, out var daOnda))
                componentes[onda] = daOnda = new(StringComparer.OrdinalIgnoreCase);
            daOnda.UnionWith(toca);
            ondas[proximo.Numero] = onda;
        }
        return efetivos.OrderBy(i => i.Numero).Select(i => i with { Onda = ondas[i.Numero] }).ToArray();
    }
}
