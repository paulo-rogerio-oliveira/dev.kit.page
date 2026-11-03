using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using DevKitPage.Infrastructure.Tests;
using Microsoft.Extensions.DependencyInjection;
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

        await b.ComAsync(sp => sp.GetRequiredService<IProvisao>().RemoverAsync(ana, "ana", id, default));
        Assert.True(await executor.ProcessarUmAsync(default));

        Assert.Equal(ProvisaoV1.EstadoRemovido, (await DetalheAsync(b, ana, id)).Recurso.Estado);
        Assert.Equal($"remover {conta}", nuvem.Chamadas.Last());
    }
}
