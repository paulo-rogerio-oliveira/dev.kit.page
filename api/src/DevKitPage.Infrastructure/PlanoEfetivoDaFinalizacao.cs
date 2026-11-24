using System.Text.Json;
using DevKit.Matrix;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

public static class PlanoEfetivoDaFinalizacao
{
    public static async Task<EnvioDeProjetoV1> ConferirAsync(DevKitPageDb db, long projetoId, EnvioDeProjetoV1 projeto, CancellationToken ct)
    {
        if (projeto.PoliticaDeFinalizacao?.Politica != "finalizacao-unica") return projeto;
        var linhas = await db.Set<RevisaoMatrixNoBanco>().AsNoTracking().Where(m => m.ProjetoId == projetoId && m.Tipo == "matriz").ToArrayAsync(ct);
        var matrizes = linhas.GroupBy(m => (m.Aplicacao, m.DocumentoId))
            .Select(g => JsonSerializer.Deserialize<DocumentoMatrix>(g.OrderBy(m => m.Versao).Last().Documento, RegrasMatrix.Json)!.Matriz!).ToArray();
        var transferencias = matrizes.SelectMany(m => m.Transferencias ?? []).Where(t => t.Revogacao is null).ToArray();
        FonteDaTransferenciaMatrix.Conferir(projeto, transferencias);
        if (transferencias.Any(t => matrizes.Any(m => m.Celulas.Any(c => c.Responsaveis.Any(r => r.Item == t.Item)))))
            throw new ArgumentException("Transferência parcial: ainda há obrigação atribuída ao item em outra matriz do projeto.");
        var transferidos = transferencias.Select(t => t.Item).ToHashSet();
        return projeto with { Itens = projeto.Itens.Where(i => !transferidos.Contains(i.Numero)).ToArray() };
    }
}
