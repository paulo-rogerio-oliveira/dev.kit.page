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

    /// <summary>A chave atual de cada máquina registrada aqui — o registro de novo a apresenta, como o dev.kit.</summary>
    public Dictionary<string, string> Chaves { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Registra uma máquina (com a adesão à empresa, quando há código) e devolve o id interno. A que já
    /// foi registrada aqui se registra de novo com a chave atual.
    /// </summary>
    public async Task<int> MaquinaAsync(string maquinaId, string? codigoEmpresa = null, string? colaborador = null)
    {
        // (o registro devolve nulo quando recusado: a máquina existe e a chave não confere)
        var registro = await ComAsync(sp => sp.GetRequiredService<IMaquinas>()
            .RegistrarAsync(maquinaId, "1.4.0", codigoEmpresa, colaborador, Chaves.GetValueOrDefault(maquinaId), peloAdmin: false, default));
        Chaves[maquinaId] = registro?.Chave ?? throw new InvalidOperationException($"O registro de {maquinaId} foi recusado.");
        return await ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Maquinas
            .Where(m => m.MaquinaId == maquinaId).Select(m => m.Id).SingleAsync());
    }

    public static TelemetryEventV1 Evento(string id, string tipo, DateTimeOffset em, int quantidade = 1, long? valor = null, string detalhe = "")
        => new(id, tipo, "s1", quantidade, valor, detalhe, em);

    /// <summary>Uma exceção não classificada (US #381), com o trace e a assinatura que o dev.kit manda.</summary>
    public static TelemetryEventV1 Excecao(string id, DateTimeOffset em, string assinatura, string trace)
        => new(id, TiposDeEvento.ExcecaoNaoClassificada, "s1", 1, null, assinatura, em, trace, assinatura);

    /// <summary>
    /// As colunas de uma tabela no <c>CREATE TABLE</c> que o EnsureCreated faria no SQL Server, a partir
    /// do MESMO modelo — o que cada script de <c>api/scripts/sqlserver</c> tem de repetir.
    /// </summary>
    public static IReadOnlyList<string> ColunasNoSqlServer(string tabela)
    {
        var opcoes = new DbContextOptionsBuilder<DevKitPageDb>().UseSqlServer("Server=.;Database=modelo").Options;
        using var db = new DevKitPageDb(opcoes);
        var doModelo = db.Database.GenerateCreateScript();
        var bloco = doModelo[doModelo.IndexOf($"CREATE TABLE [{tabela}]", StringComparison.Ordinal)..];
        bloco = bloco[..bloco.IndexOf(");", StringComparison.Ordinal)];
        return bloco.Split('\n').Select(l => l.Trim().TrimEnd(',')).Where(l => l.StartsWith('[')).ToList();
    }

    /// <summary>O texto de um script versionado, achado subindo da pasta do teste até a raiz da API.</summary>
    public static string ScriptDoAzureSql(string nome)
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var arquivo = Path.Combine(pasta.FullName, "scripts", "sqlserver", nome);
            if (File.Exists(arquivo))
                return File.ReadAllText(arquivo);
        }

        throw new FileNotFoundException($"api/scripts/sqlserver/{nome} não encontrado.");
    }

    public async ValueTask DisposeAsync()
    {
        await Servicos.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }
}
