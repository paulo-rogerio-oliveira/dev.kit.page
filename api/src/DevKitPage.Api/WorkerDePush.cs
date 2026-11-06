using DevKitPage.Core;

namespace DevKitPage.Api;

public sealed class WorkerDePush(IServiceScopeFactory escopos, ILogger<WorkerDePush> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A inicialização/migração da base termina antes da primeira varredura.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var escopo = escopos.CreateAsyncScope();
                    await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePush>().ProcessarAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception e)
                {
                    // Exceções de HTTP/proteção podem conter endpoints ou chaves: só o tipo vai ao log.
                    log.LogWarning("A varredura de push falhou ({Tipo}); será retomada no próximo ciclo.", e.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
