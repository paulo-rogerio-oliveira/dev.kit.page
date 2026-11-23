using System.Text.Json;
using DevKit.Matrix;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

public static class FonteDaTransferenciaMatrix
{
    public static ItemDaTransferenciaMatrix Retratar(EnvioDeProjetoV1 projeto, ItemDoProjetoV1 item) => new(item.Numero,
        item.Titulo, item.Descricao, item.Fluxo, item.Repositorios.ToArray(), item.Componentes.ToArray(), item.Dependencias.ToArray(),
        item.Onda, item.WorkItemId, item.Situacao, item.Branch.Length > 0 ? item.Branch : $"{projeto.Branch}-item-{item.Numero}",
        item.Agente ?? "", item.Modelo ?? "", item.Tipo, item.Prioridade, item.Horas,
        item.Design is null ? "" : JsonSerializer.Serialize(item.Design));

    public static void Conferir(EnvioDeProjetoV1 projeto, IEnumerable<TransferenciaDeValidacaoMatrix> transferencias)
    {
        var itens = projeto.Itens.Select(i => Retratar(projeto, i)).ToArray();
        foreach (var t in transferencias) RegrasDaTransferenciaMatrix.ConferirFonte(t, itens);
    }
}
