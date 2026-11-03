using System.Collections.Concurrent;
using DevKitPage.Core;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao;

/// <summary>
/// O provedor de DESENVOLVIMENTO e da demonstração (<c>Provisao:Provedor=simulado</c>): não sai da máquina, demora
/// <see cref="OpcoesDoWorker.SimulacaoSegundos"/> e dá <see cref="OpcoesDoWorker.SimulacaoFalhasTransitorias"/> falhas transitórias
/// por recurso antes de acertar — o caminho inteiro do motor (reserva, espera, repetição, pronto) sem criar nada pago. O
/// worker se recusa a subir com ele em Production.
/// </summary>
public sealed class ProvedorSimulado(IOptions<OpcoesDoWorker> opcoes) : IProvedorDeNuvem
{
    private readonly ConcurrentDictionary<long, int> _falhas = new();

    public async Task<RecursoNaNuvem> CriarFilaAsync(FilaNaNuvem fila, CancellationToken ct)
    {
        await SimularAsync(fila, "a conta de armazenamento", ct);
        return new RecursoNaNuvem(
            $"/subscriptions/simulada/resourceGroups/{fila.GrupoDeRecursos}/providers/Microsoft.Storage/storageAccounts/{fila.Conta}",
            $"https://{fila.Conta}.queue.core.windows.net/{fila.Fila}");
    }

    public Task RemoverFilaAsync(FilaNaNuvem fila, CancellationToken ct) => SimularAsync(fila, "a remoção da conta", ct);

    private async Task SimularAsync(FilaNaNuvem fila, string oQue, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, cfg.SimulacaoSegundos)), ct);
        if (_falhas.AddOrUpdate(fila.RecursoId, 1, (_, n) => n + 1) <= cfg.SimulacaoFalhasTransitorias)
            throw new FalhaNaNuvem($"Simulação: o Azure respondeu 503 ao criar {oQue}.", transitoria: true);
    }
}
