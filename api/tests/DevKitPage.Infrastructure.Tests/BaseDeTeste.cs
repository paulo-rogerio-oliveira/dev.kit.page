using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>Um relógio que o teste move.</summary>
public sealed class RelogioDeTeste(DateTimeOffset agora) : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = agora;

    public override DateTimeOffset GetUtcNow() => Agora;
}

/// <summary>
/// Uma base SQLite NOVA num arquivo temporário, com a composição de verdade (as migrations, a
/// semente e os serviços) — o mesmo caminho da API, sem o HTTP.
/// </summary>
public sealed class BaseDeTeste : IAsyncDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), $"devkitpage-{Guid.NewGuid():N}.db");

    public RelogioDeTeste Relogio { get; } = new(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

    public ServiceProvider Servicos { get; }

    public BaseDeTeste(Dictionary<string, string?>? configuracao = null)
    {
        var valores = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DevKitPage"] = $"Data Source={_arquivo}",
            ["Seed:AdminPassword"] = "senhaInicial2026",
            ["Telemetria:RetencaoDias"] = "30",
        };
        foreach (var (chave, valor) in configuracao ?? new())
            valores[chave] = valor;

        var cfg = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var servicos = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(cfg);
        servicos.AddSingleton<TimeProvider>(Relogio);
        servicos.AdicionarInfraestrutura(cfg);
        Servicos = servicos.BuildServiceProvider();
    }

    public async Task InicializarAsync()
    {
        using var escopo = Servicos.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<InicializadorDaBase>().InicializarAsync();
    }

    /// <summary>Executa com um escopo novo (um DbContext novo), como cada requisição da API.</summary>
    public async Task<T> ComAsync<T>(Func<IServiceProvider, Task<T>> acao)
    {
        using var escopo = Servicos.CreateScope();
        return await acao(escopo.ServiceProvider);
    }

    /// <summary>Registra uma máquina e devolve o id interno.</summary>
    public async Task<int> MaquinaAsync(string maquinaId)
    {
        await ComAsync(sp => sp.GetRequiredService<IMaquinas>().RegistrarAsync(maquinaId, "1.4.0", default));
        return await ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Maquinas
            .Where(m => m.MaquinaId == maquinaId).Select(m => m.Id).SingleAsync());
    }

    public static TelemetryEventV1 Evento(string id, string tipo, DateTimeOffset em, int quantidade = 1, long? valor = null, string detalhe = "")
        => new(id, tipo, "s1", quantidade, valor, detalhe, em);

    public async ValueTask DisposeAsync()
    {
        await Servicos.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }
}
