using System.Collections.Concurrent;
using DevKitPage.Contracts.V1;
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
        await SimularAsync(fila.RecursoId, "a conta de armazenamento", ct);
        return new RecursoNaNuvem(
            $"/subscriptions/simulada/resourceGroups/{fila.GrupoDeRecursos}/providers/Microsoft.Storage/storageAccounts/{fila.Conta}",
            $"https://{fila.Conta}.queue.core.windows.net/{fila.Fila}");
    }

    public Task RemoverFilaAsync(FilaNaNuvem fila, CancellationToken ct) => SimularAsync(fila.RecursoId, "a remoção da conta", ct);

    public Task CriarPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct) => SimularAsync(aplicativo.RecursoId, "o plano", ct);

    /// <summary>
    /// O aplicativo simulado. O nome com <see cref="OpcoesDoWorker.SimulacaoFalhaNoAplicativo"/> falha de vez no aplicativo — com o
    /// plano já criado, é a falha PARCIAL da demonstração.
    /// </summary>
    public async Task<RecursoNaNuvem> CriarAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
    {
        await SimularAsync(aplicativo.RecursoId, "o aplicativo", ct);
        var falha = opcoes.Value.SimulacaoFalhaNoAplicativo;
        if (!string.IsNullOrWhiteSpace(falha) && aplicativo.Nome.Contains(falha.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new FalhaNaNuvem("o Azure respondeu 409, nome em uso (simulação)", transitoria: false);
        var estatico = aplicativo.Tipo == ProvisaoV1.TipoStaticWebApp;
        return new RecursoNaNuvem(
            $"/subscriptions/simulada/resourceGroups/{aplicativo.GrupoDeRecursos}/providers/Microsoft.Web/{(estatico ? "staticSites" : "sites")}/{aplicativo.Nome}",
            $"https://{aplicativo.Nome}.{(estatico ? "azurestaticapps.net" : "azurewebsites.net")}");
    }

    public Task RemoverAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct) => SimularAsync(aplicativo.RecursoId, "a remoção do aplicativo", ct);

    public Task RemoverPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct) => SimularAsync(aplicativo.RecursoId, "a remoção do plano", ct);

    private async Task SimularAsync(long recursoId, string oQue, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, cfg.SimulacaoSegundos)), ct);
        if (_falhas.AddOrUpdate(recursoId, 1, (_, n) => n + 1) <= cfg.SimulacaoFalhasTransitorias)
            throw new FalhaNaNuvem($"Simulação: o Azure respondeu 503 ao criar {oQue}.", transitoria: true);
    }
}
