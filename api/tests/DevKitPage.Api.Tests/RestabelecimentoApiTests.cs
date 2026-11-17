using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevKitPage.Api.Tests;

/// <summary>A coluna perdida do incidente, pela pipeline HTTP real e somente na base temporária da fixture.</summary>
public sealed class RestabelecimentoApiTests
{
    private const string Maquina = "maquina-restabelecimento";
    private const string Rota = "/api/remoto/conversa/falas";

    [Fact]
    public async Task Banco_inacessivel_da_503_Unhealthy_e_volta_a_Healthy_quando_a_conexao_e_restaurada()
    {
        using var api = new ApiDeTeste();
        using var cliente = api.CreateClient();
        Assert.Equal("Healthy", await cliente.GetStringAsync("/health"));
        var configuracao = api.Services.GetRequiredService<IConfiguration>();
        const string chave = "ConnectionStrings:DevKitPage";
        var original = configuracao[chave];
        try
        {
            // ReadWrite exige arquivo existente: falha real de abertura, sem criar outra base nem simular o check.
            configuracao[chave] = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(Path.GetTempPath(), $"devkitpage-ausente-{Guid.NewGuid():N}.db"),
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString();
            var resposta = await cliente.GetAsync("/health");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, resposta.StatusCode);
            Assert.Equal("Unhealthy", await resposta.Content.ReadAsStringAsync());
        }
        finally
        {
            configuracao[chave] = original;
        }
        Assert.Equal("Healthy", await cliente.GetStringAsync("/health"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Esquema_incompleto_da_500_correlacionado_e_a_migration_restabelece_a_conversa(bool administrador)
    {
        using var registros = new RegistroDeFalhas();
        using var api = new ApiDeTeste();
        using var admin = await api.AdminAsync();
        api.Services.GetRequiredService<ILoggerFactory>().AddProvider(registros);
        using var cliente = administrador ? api.CreateClient() : (await api.UsuarioAsync(admin, "dev-recuperacao", Papeis.Dev)).Cliente;
        if (administrador)
            cliente.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;

        var heartbeat = new HeartbeatV1(RemotoV1.Contrato, Maquina, "Máquina de teste", "177",
            RemotoV1.OrigemServico, true, RemotoV1.AgenteAtivo, false);
        (await cliente.PostAsJsonAsync("/api/remoto/heartbeat", heartbeat)).EnsureSuccessStatusCode();
        var anterior = Fala("fala-anterior");
        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync(Rota, anterior)).StatusCode);
        Assert.Equal("Healthy", await cliente.GetStringAsync("/health"));

        // Mesmo estado lógico documentado no incidente: histórico registra a migration, mas falta sua coluna.
        // Estes comandos atingem exclusivamente o arquivo aleatório criado por ApiDeTeste, nunca a API remota.
        await ComBancoAsync(api, async (db, esquema, log) =>
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP INDEX "IX_FalasRemotas_UsuarioId_MaquinaId_Sessao_DitaEmUtc";
                ALTER TABLE "FalasRemotas" DROP COLUMN "Sessao";
                """);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            await esquema.VerificarAsync(db, log);
        });
        Assert.Equal("Degraded", await cliente.GetStringAsync("/health"));

        using var anonimo = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync(Rota, Fala("sem-login"))).StatusCode);
        var nova = Fala("fala-reenvio");
        var falhou = await cliente.PostAsJsonAsync(Rota, nova);
        Assert.Equal(HttpStatusCode.InternalServerError, falhou.StatusCode);
        Assert.Equal("application/problem+json", falhou.Content.Headers.ContentType?.MediaType);
        using var problema = JsonDocument.Parse(await falhou.Content.ReadAsStringAsync());
        Assert.Equal(500, problema.RootElement.GetProperty("status").GetInt32());
        var codigo = problema.RootElement.GetProperty("codigo").GetString();
        var detalhe = problema.RootElement.GetProperty("detail").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(codigo));
        Assert.Contains(codigo!, detalhe, StringComparison.Ordinal);
        Assert.Contains($"POST {Rota}", detalhe, StringComparison.Ordinal);
        Assert.Contains(registros.Mensagens, mensagem => mensagem.Contains($"POST {Rota}", StringComparison.Ordinal)
            && mensagem.Contains(codigo!, StringComparison.Ordinal));
        if (administrador)
            Assert.Contains("FalasRemotas.Sessao", detalhe, StringComparison.Ordinal);
        else
        {
            Assert.DoesNotContain("FalasRemotas", detalhe, StringComparison.Ordinal);
            Assert.DoesNotContain("SQLite", detalhe, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("__EFMigrationsHistory", detalhe, StringComparison.Ordinal);
        }

        // Ensaio de recuperação para frente, com as migrations reais; não executar Down nem recriar a base.
        // Remover esta entrada só é válido para esta fixture, cuja ausência de coluna/índice acabamos de provar.
        await ComBancoAsync(api, async (db, _, _) =>
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261109060000_ConversaDaTask'"));
        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            await using var escopo = api.Services.CreateAsyncScope();
            await escopo.ServiceProvider.GetRequiredService<InicializadorDaBase>().InicializarAsync();
        }
        Assert.Equal("Healthy", await cliente.GetStringAsync("/health"));
        Assert.Equal(HttpStatusCode.Created, (await cliente.PostAsJsonAsync(Rota, nova)).StatusCode);
        var repetida = await cliente.PostAsJsonAsync(Rota, nova);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.True((await repetida.Content.ReadFromJsonAsync<FalaRecebidaV1>())!.Repetida);
        var historico = (await cliente.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!;
        Assert.Equal(new[] { anterior.Id, nova.Id }, historico.Falas.Select(fala => fala.Id));
        Assert.Empty(api.Fila.Enviadas);
    }

    private static FalaDoDevKitV1 Fala(string id)
        => new(RemotoV1.Contrato, Maquina, id, ConversaV1.AutorDev, ConversaV1.OrigemDesktop,
            "Mensagem de teste de recuperação.", DateTimeOffset.UtcNow);

    private static async Task ComBancoAsync(ApiDeTeste api, Func<DevKitPageDb, EsquemaDoBanco, ILogger, Task> executar)
    {
        await using var escopo = api.Services.CreateAsyncScope();
        var sp = escopo.ServiceProvider;
        await executar(sp.GetRequiredService<DevKitPageDb>(), sp.GetRequiredService<EsquemaDoBanco>(),
            sp.GetRequiredService<ILogger<RestabelecimentoApiTests>>());
    }

    private sealed class RegistroDeFalhas : ILoggerProvider
    {
        public ConcurrentQueue<string> Mensagens { get; } = new();

        public ILogger CreateLogger(string categoria) => new Registrador(categoria, Mensagens);
        public void Dispose() { }

        private sealed class Registrador(string categoria, ConcurrentQueue<string> mensagens) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel) && categoria == typeof(ErroUtilDaApi).FullName)
                    mensagens.Enqueue(formatter(state, exception));
            }
        }
    }
}
