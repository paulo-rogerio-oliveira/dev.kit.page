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

        log.LogInformation("Provisão {Recurso}: {Tipo}, {Operacao}, tentativa {Tentativa}.", recurso.Id, recurso.Tipo, recurso.Operacao, recurso.Tentativas);
        try
        {
            var (seguiu, criado) = recurso.Tipo == ProvisaoV1.TipoFila
                ? (true, await FilaAsync(nuvem, recurso, ct))
                : await AplicativoAsync(motor, nuvem, recurso, ct);
            if (!seguiu)
                return true;
            if (await motor.ConcluirAsync(recurso.Id, Trabalhador, criado, ct))
                log.LogInformation("Provisão {Recurso}: {Operacao} concluída.", recurso.Id, recurso.Operacao);
            else
                ReservaPerdida(recurso, "concluir");
        }
        catch (FalhaNaNuvem falha)
        {
            log.LogWarning(falha, "Provisão {Recurso}: {Operacao} falhou ({Tipo}).", recurso.Id, recurso.Operacao, falha.Transitoria ? "transitória" : "definitiva");
            if (!await motor.FalharAsync(recurso.Id, Trabalhador, falha.Message, falha.Transitoria, ct))
                ReservaPerdida(recurso, "gravar a falha");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var codigo = Guid.NewGuid().ToString("N")[..8];
            log.LogError(e, "Provisão {Recurso}: falha inesperada (código {Codigo}).", recurso.Id, codigo);
            if (!await motor.FalharAsync(recurso.Id, Trabalhador, $"Falha inesperada no worker da provisão (código {codigo} no log).", transitoria: false, ct))
                ReservaPerdida(recurso, "gravar a falha");
        }
        return true;
    }

    private static async Task<RecursoNaNuvem?> FilaAsync(IProvedorDeNuvem nuvem, RecursoProvisionado recurso, CancellationToken ct)
    {
        var fila = new FilaNaNuvem(recurso.Id, recurso.UsuarioId, recurso.GrupoDeRecursos, recurso.Nome, recurso.Fila, recurso.Regiao);
        if (recurso.Operacao == ProvisaoV1.OperacaoRemover)
        {
            await nuvem.RemoverFilaAsync(fila, ct);
            return null;
        }
        return await nuvem.CriarFilaAsync(fila, ct);
    }

    /// <summary>
    /// O aplicativo em PASSOS. O App Service cria o plano e grava o passo antes do site: a falha no site deixa a falha PARCIAL
    /// (o plano lá, o site não), e a repetição pula o plano. A remoção apaga o aplicativo e, depois, o plano. Falso no
    /// <c>Seguiu</c> quando a reserva se perdeu no meio: o worker para e não grava o desfecho.
    /// </summary>
    private async Task<(bool Seguiu, RecursoNaNuvem? Criado)> AplicativoAsync(IMotorDeProvisao motor, IProvedorDeNuvem nuvem, RecursoProvisionado recurso, CancellationToken ct)
    {
        var aplicativo = new AplicativoNaNuvem(recurso.Id, recurso.UsuarioId, recurso.Tipo, recurso.GrupoDeRecursos, recurso.Nome, recurso.NomeDoPlano,
            recurso.Regiao, recurso.Plano);
        var servico = recurso.Tipo == ProvisaoV1.TipoAppService;
        if (recurso.Operacao == ProvisaoV1.OperacaoRemover)
        {
            await nuvem.RemoverAplicativoAsync(aplicativo, ct);
            if (servico)
                await nuvem.RemoverPlanoAsync(aplicativo, ct);
            return (true, null);
        }

        if (servico && !RegrasDaProvisao.Passos(recurso.Passos).Contains(ProvisaoV1.PassoPlano))
        {
            await nuvem.CriarPlanoAsync(aplicativo, ct);
            if (!await motor.RegistrarPassoAsync(recurso.Id, Trabalhador, ProvisaoV1.PassoPlano, ct))
            {
                ReservaPerdida(recurso, "gravar o plano criado");
                return (false, null);
            }
        }
        return (true, await nuvem.CriarAplicativoAsync(aplicativo, ct));
    }

    /// <summary>
    /// A reserva passou a outro (venceu e outra instância a retomou, ou o usuário mudou o pedido): o desfecho desta execução
    /// NÃO é gravado — fica no log, para achar o trabalho feito no Azure que não chegou à base.
    /// </summary>
    private void ReservaPerdida(RecursoProvisionado recurso, string acao)
        => log.LogWarning("Provisão {Recurso}: o worker {Trabalhador} perdeu a reserva ao {Acao} ({Operacao}, tentativa {Tentativa}); o desfecho desta execução não foi gravado.",
            recurso.Id, Trabalhador, acao, recurso.Operacao, recurso.Tentativas);
}
