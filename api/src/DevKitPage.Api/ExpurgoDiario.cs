using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// O expurgo diário do que passou da retenção: os eventos brutos e as ocorrências de exceção, com o
/// trace delas (<c>Telemetria:RetencaoDias</c> — o grupo e o estado da reação ficam), e os pedidos de demonstração (<c>Demonstracoes:RetencaoDias</c>, LGPD). Os totais diários já foram
/// consolidados na ingestão, então o histórico do dashboard não muda. Cada expurgo tem o seu
/// tratamento: uma falha vai para o log, não impede o outro, e a próxima volta tenta de novo.
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
                await ExpurgarAsync<IExpurgoDeEventos>("evento(s)", (e, ct) => e.ExpurgarAsync(ct), stoppingToken);
                await ExpurgarAsync<IExpurgoDeOcorrencias>("ocorrência(s) de exceção", (e, ct) => e.ExpurgarAsync(ct), stoppingToken);
                await ExpurgarAsync<IExpurgoDePedidos>("pedido(s) de demonstração", (e, ct) => e.ExpurgarAsync(ct), stoppingToken);
            }
            while (await relogio.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // A API está parando.
        }
    }

    /// <summary>Uma volta de um expurgo, num escopo próprio (um DbContext novo).</summary>
    public async Task ExpurgarAsync<TExpurgo>(string oQue, Func<TExpurgo, CancellationToken, Task<int>> expurgar, CancellationToken ct)
        where TExpurgo : notnull
    {
        try
        {
            using var escopo = escopos.CreateScope();
            var apagados = await expurgar(escopo.ServiceProvider.GetRequiredService<TExpurgo>(), ct);
            log.LogInformation("Expurgo: {Apagados} {OQue} além da retenção removido(s).", apagados, oQue);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Expurgo de {OQue} falhou; tenta de novo na próxima volta.", oQue);
        }
    }
}
