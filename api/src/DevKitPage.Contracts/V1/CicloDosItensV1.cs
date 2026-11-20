namespace DevKitPage.Contracts.V1;

public sealed record ReservaDeEscritaDoItemV1(string Maquina, string Instancia, string Sessao, string Tentativa);
public sealed record ParadaDeEscritaDoItemV1(string Pedido, ReservaDeEscritaDoItemV1 Reserva, long Recibo, DateTimeOffset Em);
public sealed record ReservasDoCicloDoItemV1(ReservaDeEscritaDoItemV1[] Reservas, ParadaDeEscritaDoItemV1[] Paradas);

/// <summary>Proveniência da importação; não substitui a autoria nem o recibo local original.</summary>
public sealed record ImportacaoDaDecisaoV1(string AutorAutenticado, DateTimeOffset Em, string HashDoEnvio);

/// <summary>Recibo portátil; identidades de processos e caminhos da máquina permanecem no host.</summary>
public sealed record DecisaoHumanaDoItemV1(string Pedido, string Acao, string Autor, string Confirmacao,
    string Motivo, DateTimeOffset Em, string VersaoEsperada, string EstadoAnterior, string Snapshot,
    bool ParadaPendente = false, int Geracao = 1);

public sealed record PedidoDeDecisaoDoItemV1(string Pedido, string Acao, int VersaoEsperada,
    bool Aplicar = false, bool Confirmar = false, string Motivo = "", string Confirmacao = "");

public sealed record ImpactoDaDecisaoDoItemV1(bool Simulacao, ProjetoV1 Projeto, int Item,
    IReadOnlyList<int> Dependentes, IReadOnlyList<ItemDoProjetoV1> GrafoEfetivo, string Consequencia);

public static class CicloDosItensV1
{
    public const string Recurso = "ciclo-humano-item-v1";
    public const string Cabecalho = "X-DevKit-Ciclo-Item";
    public const string Concluir = "concluir-manualmente";
    public const string Cancelar = "cancelar-item";
    public const string Consequencia = "Retira somente o trabalho restante. Commits e integrações anteriores permanecem na procedência e no fechamento; "
        + "nenhuma integração é criada ou revertida. Fontes, tasks, tentativas e entregas ficam preservados. Reservas ativas dependem de parada confirmada pelo host.";

    public static bool Retirado(ItemDoProjetoV1 item)
        => item.DecisaoHumana is not null || item.Situacao is ProjetosV1.ItemConcluidoManualmente or ProjetosV1.ItemCancelado;

    public static bool ReservaPendente(ItemDoProjetoV1 item)
    {
        if (item.DecisaoHumana is not { } d) return false;
        if (item.ReservasDeEscrita is not { Reservas.Length: > 0 } r) return d.ParadaPendente;
        return r.Reservas.Any(alvo => !r.Paradas.Any(p => p.Pedido == d.Pedido && p.Reserva == alvo));
    }
}
