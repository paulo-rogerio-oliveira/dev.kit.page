using System.Text;
using DevKitPage.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao;

/// <summary>
/// O laço do trabalhador: um pedido por volta, cada um num escopo próprio (um DbContext novo); sem pedido, a pausa de
/// <see cref="OpcoesDaProvisao.IntervaloSegundos"/>. Uma volta que falha vai para o log e não derruba o processo. Mais de uma
/// réplica pode rodar ao mesmo tempo: a concessão no SQL é que decide quem pega cada pedido.
/// </summary>
public sealed class TrabalhadorDaProvisao(IServiceScopeFactory escopos, IOptions<OpcoesDaProvisao> opcoes, ILogger<TrabalhadorDaProvisao> log) : BackgroundService
{
    /// <summary>O nome desta réplica na concessão e na auditoria: a máquina (no contêiner, o id dele) e o processo.</summary>
    public static string Nome { get; } = NomeDoTrabalhador(Environment.MachineName, Environment.ProcessId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Trabalhador da provisão {Nome} no ar.", Nome);
        while (!stoppingToken.IsCancellationRequested)
        {
            bool processou;
            try
            {
                using var escopo = escopos.CreateScope();
                processou = await escopo.ServiceProvider.GetRequiredService<IMotorDeProvisao>().ProcessarProximoAsync(Nome, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "A volta do trabalhador da provisão falhou; tenta de novo depois do intervalo.");
                processou = false;
            }

            if (processou)
                continue;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, opcoes.Value.IntervaloSegundos)), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>O nome com letras, dígitos, ponto e hífen, no teto da coluna (o resto vira hífen).</summary>
    public static string NomeDoTrabalhador(string maquina, int processo)
    {
        var limpo = new StringBuilder();
        foreach (var c in (maquina ?? string.Empty).Trim())
            limpo.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' ? char.ToLowerInvariant(c) : '-');
        var raiz = limpo.Length == 0 ? "trabalhador" : limpo.ToString();
        var sufixo = "-" + processo.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return (raiz.Length + sufixo.Length <= 80 ? raiz : raiz[..(80 - sufixo.Length)]) + sufixo;
    }
}
