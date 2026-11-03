using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao;

/// <summary>
/// A varredura do worker: reserva o próximo pedido (<see cref="IMotorDeProvisao"/>), executa a operação na nuvem
/// (<see cref="IProvedorDeNuvem"/>) e grava o desfecho. Um pedido por vez, cada um num escopo (um DbContext) novo. A falha
/// da nuvem já vem classificada; a exceção inesperada vira falha DEFINITIVA com um código que acha o erro no log — sem o
/// texto dela, que pode ter o que não deve ir para a tela.
/// </summary>
public sealed class ExecutorDaProvisao(IServiceScopeFactory escopos, IOptions<OpcoesDoWorker> opcoes, ILogger<ExecutorDaProvisao> log) : BackgroundService
{
    /// <summary>O nome desta instância na reserva: a máquina, o processo e um sufixo (duas instâncias nunca se confundem).</summary>
    public string Trabalhador { get; } = NomeDaInstancia();

    private static string NomeDaInstancia()
    {
        var nome = $"{Environment.MachineName}-{Environment.ProcessId}-{Guid.NewGuid():N}";
        return nome.Length <= 100 ? nome : nome[^100..];
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Worker da provisão {Trabalhador} no ar.", Trabalhador);
        while (!ct.IsCancellationRequested)
        {
            bool processou;
            try
            {
                processou = await ProcessarUmAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A base fora do ar, por exemplo: a varredura continua, e o pedido reservado volta quando a reserva vencer.
                log.LogError(e, "A varredura da provisão falhou; tenta de novo no próximo intervalo.");
                processou = false;
            }

            if (!processou)
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, opcoes.Value.IntervaloSegundos)), ct);
        }
    }

    /// <summary>Executa UM pedido; falso quando não havia nenhum vencido.</summary>
    public async Task<bool> ProcessarUmAsync(CancellationToken ct)
    {
        using var escopo = escopos.CreateScope();
        var motor = escopo.ServiceProvider.GetRequiredService<IMotorDeProvisao>();
        var nuvem = escopo.ServiceProvider.GetRequiredService<IProvedorDeNuvem>();

        var recurso = await motor.ReservarAsync(Trabalhador, ct);
        if (recurso is null)
            return false;

        var fila = new FilaNaNuvem(recurso.Id, recurso.UsuarioId, recurso.GrupoDeRecursos, recurso.Nome, recurso.Fila, recurso.Regiao);
        var remocao = recurso.Operacao == ProvisaoV1.OperacaoRemover;
        log.LogInformation("Provisão {Recurso}: {Operacao}, tentativa {Tentativa}.", recurso.Id, recurso.Operacao, recurso.Tentativas);
        try
        {
            RecursoNaNuvem? criado = null;
            if (remocao)
                await nuvem.RemoverFilaAsync(fila, ct);
            else
                criado = await nuvem.CriarFilaAsync(fila, ct);
            await motor.ConcluirAsync(recurso.Id, Trabalhador, criado, ct);
            log.LogInformation("Provisão {Recurso}: {Operacao} concluída.", recurso.Id, recurso.Operacao);
        }
        catch (FalhaNaNuvem falha)
        {
            log.LogWarning(falha, "Provisão {Recurso}: {Operacao} falhou ({Tipo}).", recurso.Id, recurso.Operacao, falha.Transitoria ? "transitória" : "definitiva");
            await motor.FalharAsync(recurso.Id, Trabalhador, falha.Message, falha.Transitoria, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var codigo = Guid.NewGuid().ToString("N")[..8];
            log.LogError(e, "Provisão {Recurso}: falha inesperada (código {Codigo}).", recurso.Id, codigo);
            await motor.FalharAsync(recurso.Id, Trabalhador, $"Falha inesperada no worker da provisão (código {codigo} no log).", transitoria: false, ct);
        }
        return true;
    }
}
