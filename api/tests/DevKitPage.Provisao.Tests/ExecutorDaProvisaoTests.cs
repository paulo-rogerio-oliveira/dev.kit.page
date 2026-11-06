using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using DevKitPage.Infrastructure.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao.Tests;

/// <summary>
/// O worker de ponta a ponta sobre a base real e uma nuvem falsa: o pedido vira pronto, a falha transitória volta com a espera
/// e acerta na repetição, a definitiva para com a mensagem, a exceção inesperada vira falha com o código do log (sem o texto
/// dela), e a remoção chega a removido.
/// </summary>
public sealed class ExecutorDaProvisaoTests
{
    /// <summary>A nuvem falsa: guarda as chamadas e lança o que o teste mandar, na ordem.</summary>
    private sealed class NuvemFalsa : IProvedorDeNuvem
    {
        public Queue<Exception> Falhas { get; } = new();
        public List<string> Chamadas { get; } = [];

        public Task<RecursoNaNuvem> CriarFilaAsync(FilaNaNuvem fila, CancellationToken ct)
        {
            Chamadas.Add($"criar {fila.GrupoDeRecursos}/{fila.Conta}/{fila.Fila} em {fila.Regiao}");
            if (Falhas.TryDequeue(out var falha))
                throw falha;
            return Task.FromResult(new RecursoNaNuvem($"/subscriptions/s/{fila.Conta}", $"https://{fila.Conta}.queue.core.windows.net/{fila.Fila}"));
        }

        public Task RemoverFilaAsync(FilaNaNuvem fila, CancellationToken ct)
        {
            Chamadas.Add($"remover {fila.Conta}");
            return Falhas.TryDequeue(out var falha) ? Task.FromException(falha) : Task.CompletedTask;
        }

        /// <summary>O que acontece no meio da criação do plano (outra instância retomando a reserva, por exemplo).</summary>
        public Func<Task>? AoCriarPlano { get; set; }

        /// <summary>As falhas do passo do aplicativo, separadas das do plano.</summary>
        public Queue<Exception> FalhasDoAplicativo { get; } = new();

        public async Task CriarPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
        {
            Chamadas.Add($"plano {aplicativo.NomeDoPlano} {aplicativo.Plano} em {aplicativo.Regiao}");
            if (AoCriarPlano is { } acao)
                await acao();
        }

        public Task<RecursoNaNuvem> CriarAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
        {
            Chamadas.Add($"aplicativo {aplicativo.Tipo} {aplicativo.Nome}");
            if (FalhasDoAplicativo.TryDequeue(out var falha))
                throw falha;
            return Task.FromResult(new RecursoNaNuvem($"/subscriptions/s/{aplicativo.Nome}", $"https://{aplicativo.Nome}.azurewebsites.net"));
        }

        public Task RemoverAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
        {
            Chamadas.Add($"remover aplicativo {aplicativo.Nome}");
            return Task.CompletedTask;
        }

        public Task RemoverPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
        {
            Chamadas.Add($"remover plano {aplicativo.NomeDoPlano}");
            return Task.CompletedTask;
        }
    }

    /// <summary>O log do executor guardado: o aviso da reserva perdida tem de estar aqui.</summary>
    private sealed class LogGuardado : ILogger<ExecutorDaProvisao>
    {
        public List<(LogLevel Nivel, string Texto)> Linhas { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Linhas.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>A base com um App Service pedido (o worker ainda não passou) e o executor sobre ela, com o log guardado.</summary>
    private static async Task<(BaseDeTeste Base, ExecutorDaProvisao Executor, NuvemFalsa Nuvem, LogGuardado Log, int Ana, long Recurso)> MontarAplicativoAsync()
    {
        var b = new BaseDeTeste(new Dictionary<string, string?> { ["Provisao:MaximoDeTentativas"] = "3", ["Provisao:ReservaMinutos"] = "10" });
        await b.InicializarAsync();
        var ana = await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var usuario = new Usuario { Login = "ana", Papel = Papeis.Dev, CriadoEmUtc = b.Relogio.Agora.UtcDateTime };
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            return usuario.Id;
        });
        var pedido = new AplicativoPedido(ProvisaoV1.TipoAppService, "api-relatorios", "brazilsouth", new PlanoDeAplicativoV1("B1", "B1 · Linux", 61.70m));
        var recurso = (await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PedirAplicativoAsync(ana, "ana", pedido, default))).Valor!.Recurso.Id;
        var nuvem = new NuvemFalsa();
        var log = new LogGuardado();
        var executor = new ExecutorDaProvisao(new EscoposComNuvem(b.Servicos, nuvem), Options.Create(new OpcoesDoWorker()), log);
        return (b, executor, nuvem, log, ana, recurso);
    }

    private static async Task<(BaseDeTeste Base, ExecutorDaProvisao Executor, NuvemFalsa Nuvem, int Ana, long Recurso)> MontarAsync()
    {
        var b = new BaseDeTeste(new Dictionary<string, string?> { ["Provisao:MaximoDeTentativas"] = "3", ["Provisao:EsperaInicialSegundos"] = "30" });
        await b.InicializarAsync();
        var nuvem = new NuvemFalsa();
        var ana = await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var usuario = new Usuario { Login = "ana", Papel = Papeis.Dev, CriadoEmUtc = b.Relogio.Agora.UtcDateTime };
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            return usuario.Id;
        });
        var recurso = (await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().PedirFilaAsync(ana, "ana", "brazilsouth", default))).Valor!.Recurso.Id;

        // O executor sobre os MESMOS serviços da base, com a nuvem falsa no lugar do ARM.
        var executor = new ExecutorDaProvisao(new EscoposComNuvem(b.Servicos, nuvem), Options.Create(new OpcoesDoWorker()), NullLogger<ExecutorDaProvisao>.Instance);
        return (b, executor, nuvem, ana, recurso);
    }

    /// <summary>Os escopos da base, com a nuvem falsa respondendo por <see cref="IProvedorDeNuvem"/>.</summary>
    private sealed class EscoposComNuvem(ServiceProvider servicos, IProvedorDeNuvem nuvem) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Escopo(servicos.CreateScope(), nuvem);

        private sealed class Escopo(IServiceScope interno, IProvedorDeNuvem nuvem) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type tipo) => tipo == typeof(IProvedorDeNuvem) ? nuvem : interno.ServiceProvider.GetService(tipo);
            public void Dispose() => interno.Dispose();
        }
    }

    private static Task<DetalheDoRecursoV1> DetalheAsync(BaseDeTeste b, int ana, long id)
        => b.ComAsync(async sp => (await sp.GetRequiredService<IProvisao>().DetalheAsync(ana, id, default))!);

    [Fact]
    public async Task O_pedido_vira_fila_pronta_com_os_nomes_do_pedido()
    {
        var (b, executor, nuvem, ana, id) = await MontarAsync();
        await using var _ = b;

        Assert.True(await executor.ProcessarUmAsync(default));
        Assert.False(await executor.ProcessarUmAsync(default));

        var detalhe = await DetalheAsync(b, ana, id);
        Assert.Equal(ProvisaoV1.EstadoPronto, detalhe.Recurso.Estado);
        Assert.Equal([$"criar rg-devkit-u{ana}/{detalhe.Recurso.Nome}/comandos em brazilsouth"], nuvem.Chamadas);
        Assert.Equal($"https://{detalhe.Recurso.Nome}.queue.core.windows.net/comandos", detalhe.Recurso.Endpoint);
    }

    [Fact]
    public async Task A_falha_transitoria_espera_e_a_repeticao_usa_os_mesmos_nomes_ate_dar_certo()
    {
        var (b, executor, nuvem, ana, id) = await MontarAsync();
        await using var _ = b;
        nuvem.Falhas.Enqueue(new FalhaNaNuvem("O Azure limitou os pedidos (TooManyRequests).", transitoria: true));

        Assert.True(await executor.ProcessarUmAsync(default));
        var esperando = await DetalheAsync(b, ana, id);
        Assert.Equal((ProvisaoV1.EstadoPedido, "O Azure limitou os pedidos (TooManyRequests)."), (esperando.Recurso.Estado, esperando.Recurso.Erro));
        Assert.False(await executor.ProcessarUmAsync(default));

        b.Relogio.Agora = b.Relogio.Agora.AddSeconds(31);
        Assert.True(await executor.ProcessarUmAsync(default));

        var pronto = await DetalheAsync(b, ana, id);
        Assert.Equal((ProvisaoV1.EstadoPronto, 2), (pronto.Recurso.Estado, pronto.Recurso.Tentativas));
        Assert.Equal(2, nuvem.Chamadas.Count);
        Assert.Single(nuvem.Chamadas.Distinct());
        Assert.Equal(
            [ProvisaoV1.EventoPedido, ProvisaoV1.EventoInicio, ProvisaoV1.EventoFalhaTransitoria, ProvisaoV1.EventoInicio, ProvisaoV1.EventoPronto],
            pronto.Eventos.Select(e => e.Evento));
    }

    [Fact]
    public async Task A_falha_definitiva_para_e_a_inesperada_vira_codigo_sem_o_texto_da_excecao()
    {
        var (b, executor, nuvem, ana, id) = await MontarAsync();
        await using var _ = b;
        nuvem.Falhas.Enqueue(new FalhaNaNuvem("A identidade do worker não tem permissão para a conta de armazenamento (AuthorizationFailed).", transitoria: false));

        await executor.ProcessarUmAsync(default);
        var falhou = await DetalheAsync(b, ana, id);
        Assert.Equal(ProvisaoV1.EstadoFalhou, falhou.Recurso.Estado);
        Assert.Contains("AuthorizationFailed", falhou.Recurso.Erro, StringComparison.Ordinal);

        await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RepetirAsync(ana, "ana", id, default));
        nuvem.Falhas.Enqueue(new InvalidOperationException("Server=tcp:x;Password=segredo-que-nao-pode-aparecer"));
        await executor.ProcessarUmAsync(default);

        var inesperada = await DetalheAsync(b, ana, id);
        Assert.Equal(ProvisaoV1.EstadoFalhou, inesperada.Recurso.Estado);
        Assert.Matches("^Falha inesperada no worker da provisão \\(código [0-9a-f]{8} no log\\)\\.$", inesperada.Recurso.Erro);
        Assert.DoesNotContain(inesperada.Eventos, e => e.Detalhe.Contains("segredo", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_remocao_pedida_chega_a_removido()
    {
        var (b, executor, nuvem, ana, id) = await MontarAsync();
        await using var _ = b;
        await executor.ProcessarUmAsync(default);
        var conta = (await DetalheAsync(b, ana, id)).Recurso.Nome;

        await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RemoverAsync(ana, "ana", id, null, default));
        Assert.True(await executor.ProcessarUmAsync(default));

        Assert.Equal(ProvisaoV1.EstadoRemovido, (await DetalheAsync(b, ana, id)).Recurso.Estado);
        Assert.Equal($"remover {conta}", nuvem.Chamadas.Last());
    }

    [Fact]
    public async Task O_app_service_cria_o_plano_e_depois_o_site_e_fica_pronto_com_o_endereco()
    {
        var (b, executor, nuvem, _, ana, id) = await MontarAplicativoAsync();
        await using var _ = b;

        Assert.True(await executor.ProcessarUmAsync(default));

        var detalhe = await DetalheAsync(b, ana, id);
        var r = detalhe.Recurso;
        Assert.Equal([$"plano {r.NomeDoPlano} B1 em brazilsouth", $"aplicativo app-service {r.Nome}"], nuvem.Chamadas);
        Assert.Equal((ProvisaoV1.EstadoPronto, $"https://{r.Nome}.azurewebsites.net", false), (r.Estado, r.Endpoint, r.Parcial));
        Assert.Equal([ProvisaoV1.EventoPedido, ProvisaoV1.EventoInicio, ProvisaoV1.EventoPasso, ProvisaoV1.EventoPronto], detalhe.Eventos.Select(e => e.Evento));
        Assert.Equal($"plano {r.NomeDoPlano} e aplicativo {r.Nome} prontos", detalhe.Eventos.Last().Detalhe);
    }

    [Fact]
    public async Task A_falha_no_site_depois_do_plano_e_parcial_e_a_repeticao_retoma_do_site()
    {
        var (b, executor, nuvem, _, ana, id) = await MontarAplicativoAsync();
        await using var _ = b;
        nuvem.FalhasDoAplicativo.Enqueue(new FalhaNaNuvem("O nome do App Service já é usado no Azure (409).", transitoria: false));

        await executor.ProcessarUmAsync(default);
        var parcial = (await DetalheAsync(b, ana, id)).Recurso;
        Assert.Equal((ProvisaoV1.EstadoFalhou, true), (parcial.Estado, parcial.Parcial));
        Assert.Equal([ProvisaoV1.PassoPlano], parcial.Passos);
        Assert.Contains("409", parcial.Erro, StringComparison.Ordinal);

        await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RepetirAsync(ana, "ana", id, default));
        nuvem.Chamadas.Clear();
        await executor.ProcessarUmAsync(default);

        Assert.Equal([$"aplicativo app-service {parcial.Nome}"], nuvem.Chamadas);
        Assert.Equal(ProvisaoV1.EstadoPronto, (await DetalheAsync(b, ana, id)).Recurso.Estado);
    }

    [Fact]
    public async Task A_remocao_do_app_service_apaga_o_site_e_depois_o_plano()
    {
        var (b, executor, nuvem, _, ana, id) = await MontarAplicativoAsync();
        await using var _ = b;
        await executor.ProcessarUmAsync(default);
        var chave = await SegundoFatorDeTeste.AtivarAsync(b, ana);
        await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RemoverAsync(ana, "ana", id, SegundoFatorDeTeste.Codigo(b, chave), default));
        nuvem.Chamadas.Clear();

        Assert.True(await executor.ProcessarUmAsync(default));

        var r = (await DetalheAsync(b, ana, id)).Recurso;
        Assert.Equal([$"remover aplicativo {r.Nome}", $"remover plano {r.NomeDoPlano}"], nuvem.Chamadas);
        Assert.Equal(ProvisaoV1.EstadoRemovido, r.Estado);
    }

    [Fact]
    public async Task O_worker_que_perde_a_reserva_no_meio_registra_no_log_e_nao_grava_o_desfecho()
    {
        var (b, executor, nuvem, log, ana, id) = await MontarAplicativoAsync();
        await using var _ = b;
        // No meio do plano a reserva vence e outra instância a retoma.
        nuvem.AoCriarPlano = async () =>
        {
            b.Relogio.Agora = b.Relogio.Agora.AddMinutes(11);
            Assert.NotNull(await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ReservarAsync("outra-instancia", default)));
        };

        Assert.True(await executor.ProcessarUmAsync(default));

        var aviso = Assert.Single(log.Linhas, l => l.Nivel == LogLevel.Warning);
        Assert.Contains($"Provisão {id}: o worker {executor.Trabalhador} perdeu a reserva ao gravar o plano criado", aviso.Texto, StringComparison.Ordinal);
        Assert.DoesNotContain(nuvem.Chamadas, c => c.StartsWith("aplicativo", StringComparison.Ordinal));
        var r = (await DetalheAsync(b, ana, id)).Recurso;
        Assert.Equal((ProvisaoV1.EstadoEmProvisao, 0), (r.Estado, r.Passos!.Count));
    }

    [Fact]
    public async Task O_desfecho_que_chega_depois_de_perder_a_reserva_tambem_vai_ao_log()
    {
        var (b, executor, nuvem, log, ana, id) = await MontarAplicativoAsync();
        await using var _ = b;
        // O plano já existia (passo gravado numa execução anterior): a reserva vence durante o site.
        await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            (await db.RecursosProvisionados.SingleAsync(x => x.Id == id)).Passos = ProvisaoV1.PassoPlano;
            return await db.SaveChangesAsync();
        });
        nuvem.FalhasDoAplicativo.Enqueue(new FalhaNaNuvem("503", transitoria: true));
        executor = new ExecutorDaProvisao(new EscoposComNuvem(b.Servicos, new NuvemQueRetomaNoSite(nuvem, b)), Options.Create(new OpcoesDoWorker()), log);

        await executor.ProcessarUmAsync(default);

        Assert.Contains(log.Linhas, l => l.Nivel == LogLevel.Warning && l.Texto.Contains("perdeu a reserva ao gravar a falha", StringComparison.Ordinal));
        Assert.Equal(string.Empty, (await DetalheAsync(b, ana, id)).Recurso.Erro);
    }

    /// <summary>A nuvem falsa em que outra instância retoma a reserva enquanto o site é criado.</summary>
    private sealed class NuvemQueRetomaNoSite(NuvemFalsa nuvem, BaseDeTeste b) : IProvedorDeNuvem
    {
        public Task<RecursoNaNuvem> CriarFilaAsync(FilaNaNuvem fila, CancellationToken ct) => nuvem.CriarFilaAsync(fila, ct);
        public Task RemoverFilaAsync(FilaNaNuvem fila, CancellationToken ct) => nuvem.RemoverFilaAsync(fila, ct);
        public Task CriarPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct) => nuvem.CriarPlanoAsync(aplicativo, ct);
        public Task RemoverAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct) => nuvem.RemoverAplicativoAsync(aplicativo, ct);
        public Task RemoverPlanoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct) => nuvem.RemoverPlanoAsync(aplicativo, ct);

        public async Task<RecursoNaNuvem> CriarAplicativoAsync(AplicativoNaNuvem aplicativo, CancellationToken ct)
        {
            b.Relogio.Agora = b.Relogio.Agora.AddMinutes(11);
            await b.ComAsync(sp => sp.GetRequiredService<IMotorDeProvisao>().ReservarAsync("outra-instancia", default));
            return await nuvem.CriarAplicativoAsync(aplicativo, ct);
        }
    }
}
