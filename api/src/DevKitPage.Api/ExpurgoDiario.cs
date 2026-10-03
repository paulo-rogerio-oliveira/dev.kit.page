using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// O expurgo diário dos eventos brutos além da retenção (<c>Telemetria:RetencaoDias</c>). Os totais
/// diários já foram consolidados na ingestão, então o histórico do dashboard não muda. Uma falha vai
/// para o log e a próxima volta tenta de novo.
/// </summary>
public sealed class ExpurgoDiario(IServiceScopeFactory escopos, ILogger<ExpurgoDiario> log) : BackgroundService
{
    /// <summary>A espera antes da primeira volta: a subida não divide o banco com o expurgo.</summary>
    public static readonly TimeSpan EsperaInicial = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(EsperaInicial, stoppingToken);
            using var relogio = new PeriodicTimer(TimeSpan.FromHours(24));
            do
            {
                try
                {
                    using var escopo = escopos.CreateScope();
                    var apagados = await escopo.ServiceProvider.GetRequiredService<IExpurgoDeEventos>().ExpurgarAsync(stoppingToken);
                    log.LogInformation("Expurgo: {Apagados} evento(s) além da retenção removido(s).", apagados);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogError(ex, "Expurgo dos eventos falhou; tenta de novo na próxima volta.");
                }
            }
            while (await relogio.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // A API está parando.
        }
    }
}
