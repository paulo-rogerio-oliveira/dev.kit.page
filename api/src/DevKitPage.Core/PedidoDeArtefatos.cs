namespace DevKitPage.Core;

/// <summary>Snapshot do pedido, inclusive resultados que ainda não publicaram ou não possuem produtor.</summary>
public sealed class PedidoDeArtefatos
{
    public string Id { get; set; } = "";
    public long ProjetoId { get; set; }
    public ProjetoCompartilhado? Projeto { get; set; }
    public string Conteudo { get; set; } = "";
    public long Revisao { get; set; }
}
