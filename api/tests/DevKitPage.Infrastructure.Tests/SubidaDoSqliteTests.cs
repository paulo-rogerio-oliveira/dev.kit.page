using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A API que não subia mais (16/11): o contêiner novo rodava o <c>dotnet DevKitPage.Api.dll</c> e, sem uma linha de log, não
/// abria a porta em 230 s (nem em 300 s) — a trava de migration do EF (<c>__EFMigrationsLock</c>) ficou com a linha de um
/// processo que morreu com ela na mão, e o <c>MigrateAsync</c> do SQLite espera por ela para sempre. A mesma base, em WAL num
/// compartilhamento de rede, tinha perdido no deploy de 12/11 a migration <c>ConversaDaTask</c> (a <c>FalasRemotas.Sessao</c>).
/// </summary>
public sealed class SubidaDoSqliteTests
{
    /// <summary>A migration anterior à <c>ConversaDaTask</c>: o ponto em que a base de produção ficou.</summary>
    private const string AntesDaConversaDaTask = "20261108060000_DispositivoDaDecisao";

    /// <summary>A trava tomada pelo EF e nunca solta: o processo que a tomou morreu antes de terminar as migrations.</summary>
    private static async Task<DateTimeOffset> TravaDeProcessoMortoAsync(BaseDeTeste b)
        => await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            await db.Database.OpenConnectionAsync();
            var antes = DateTimeOffset.UtcNow;
            // Sem o Dispose da trava: é o que o processo morto deixou.
            _ = await db.GetService<IHistoryRepository>().AcquireDatabaseLockAsync();
            await db.Database.CloseConnectionAsync();
            return antes;
        });

    private static Task<long> TravasAsync(BaseDeTeste b)
        => EscalarAsync(b, $"SELECT COUNT(*) FROM \"{SqliteNaSubida.TabelaDaTrava}\"");

    private static Task<long> EscalarAsync(BaseDeTeste b, string sql)
        => b.ComAsync(async sp =>
        {
            var conexao = sp.GetRequiredService<DevKitPageDb>().Database.GetDbConnection();
            await conexao.OpenAsync();
            try
            {
                await using var comando = conexao.CreateCommand();
                comando.CommandText = sql;
                return Convert.ToInt64(await comando.ExecuteScalarAsync());
            }
            finally
            {
                await conexao.CloseAsync();
            }
        });

    private static Task<string> JournalAsync(BaseDeTeste b)
        => b.ComAsync(async sp =>
        {
            var conexao = sp.GetRequiredService<DevKitPageDb>().Database.GetDbConnection();
            await conexao.OpenAsync();
            try
            {
                await using var comando = conexao.CreateCommand();
                comando.CommandText = "PRAGMA journal_mode";
                return (string)(await comando.ExecuteScalarAsync())!;
            }
            finally
            {
                await conexao.CloseAsync();
            }
        });

    /// <summary>A subida inteira com um teto: travada, ela falharia o teste em vez de pendurar a suíte.</summary>
    private static async Task InicializarComTetoAsync(BaseDeTeste b)
    {
        var subida = b.InicializarAsync();
        var venceu = await Task.WhenAny(subida, Task.Delay(TimeSpan.FromSeconds(60)));
        Assert.True(venceu == subida, "A subida não terminou em 60 s: a trava de migration segurou o MigrateAsync.");
        await subida;
    }

    [Fact]
    public async Task Sem_a_correcao_a_trava_abandonada_segura_o_MigrateAsync_do_EF()
    {
        // O comportamento do EF que derrubou a API: sem limite, o MigrateAsync espera a trava para sempre.
        await using var b = new BaseDeTeste();
        await b.InicializarAsync();
        await TravaDeProcessoMortoAsync(b);

        using var limite = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => b.ComAsync(async sp =>
        {
            await sp.GetRequiredService<DevKitPageDb>().Database.MigrateAsync(limite.Token);
            return 0;
        }));
    }

    [Fact]
    public async Task A_subida_libera_a_trava_de_processo_morto_e_aplica_a_migration_que_faltava()
    {
        // O estado de produção: a base sem a ConversaDaTask e a trava do EF de um processo que morreu com ela.
        await using var b = new BaseDeTeste();
        await b.ComAsync(async sp =>
        {
            await sp.GetRequiredService<DevKitPageDb>().GetService<IMigrator>().MigrateAsync(AntesDaConversaDaTask);
            return 0;
        });
        var tomada = await TravaDeProcessoMortoAsync(b);
        b.Relogio.Agora = tomada.AddMinutes(10);

        await InicializarComTetoAsync(b);

        Assert.Equal(0, await TravasAsync(b));
        Assert.Equal(1, await EscalarAsync(b, "SELECT COUNT(*) FROM pragma_table_info('FalasRemotas') WHERE name = 'Sessao'"));
        Assert.Equal(1, await EscalarAsync(b, "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20261109060000_ConversaDaTask'"));
    }

    [Fact]
    public async Task A_trava_recente_e_de_outra_instancia_migrando_e_fica()
    {
        await using var b = new BaseDeTeste();
        await b.InicializarAsync();
        var tomada = await TravaDeProcessoMortoAsync(b);

        var liberada = await b.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            await db.Database.OpenConnectionAsync();
            try
            {
                return await SqliteNaSubida.LiberarTravaAbandonadaAsync(db.Database.GetDbConnection(), tomada.AddMinutes(1), TimeSpan.FromMinutes(5), NullLogger.Instance);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        });

        Assert.Null(liberada);
        Assert.Equal(1, await TravasAsync(b));
    }

    [Fact]
    public async Task A_trava_com_data_ilegivel_tambem_sai()
    {
        await using var b = new BaseDeTeste();
        await b.InicializarAsync();
        await EscalarAsync(b, $"INSERT INTO \"{SqliteNaSubida.TabelaDaTrava}\" (\"Id\", \"Timestamp\") VALUES (1, 'ontem'); SELECT 0");

        await InicializarComTetoAsync(b);

        Assert.Equal(0, await TravasAsync(b));
    }

    [Fact]
    public async Task Sem_trava_nem_base_a_subida_cria_a_base_como_antes()
    {
        await using var b = new BaseDeTeste();

        await InicializarComTetoAsync(b);
        await InicializarComTetoAsync(b);

        // O EF cria a base nova em WAL; fora do armazenamento de rede, o auto não mexe nele.
        Assert.Equal("wal", await JournalAsync(b));
        Assert.Equal(0, await TravasAsync(b));
    }

    [Fact]
    public async Task O_journal_configurado_troca_o_wal_da_base()
    {
        await using var b = new BaseDeTeste(new() { ["Banco:JournalDoSqlite"] = "delete" });

        await InicializarComTetoAsync(b);

        Assert.Equal("delete", await JournalAsync(b));
    }

    [Theory]
    [InlineData("auto", true, "delete")]
    [InlineData("auto", false, null)]
    [InlineData("AUTO", true, "delete")]
    [InlineData("wal", true, "wal")]
    [InlineData("Delete", false, "delete")]
    [InlineData("", true, null)]
    [InlineData("memory", true, null)]
    public void O_auto_so_troca_o_journal_em_rede(string configurado, bool emRede, string? esperado)
    {
        Assert.Equal(esperado, SqliteNaSubida.Pedido(configurado, emRede));
    }

    [Theory]
    [InlineData("/home/dados/devkitpage.db", "devkit", "/home", true)]
    [InlineData("/home/dados/devkitpage.db", null, "/home", false)]
    [InlineData("/tmp/devkitpage.db", "devkit", "/home", false)]
    [InlineData("/homedados/devkitpage.db", "devkit", "/home", false)]
    [InlineData(@"D:\home\dados\devkitpage.db", "devkit", @"D:\home", true)]
    [InlineData(@"\\servidor\pasta\devkitpage.db", null, null, true)]
    public void O_home_do_App_Service_e_o_caminho_UNC_sao_armazenamento_de_rede(string arquivo, string? site, string? home, bool emRede)
    {
        var variaveis = new Dictionary<string, string?> { ["WEBSITE_SITE_NAME"] = site, ["HOME"] = home };

        Assert.Equal(emRede, SqliteNaSubida.EmArmazenamentoDeRede(arquivo, nome => variaveis.GetValueOrDefault(nome)));
    }

    [Fact]
    public async Task A_base_sqlite_em_dia_nao_tem_falta_no_esquema()
    {
        await using var b = new BaseDeTeste();
        await InicializarComTetoAsync(b);

        Assert.Empty(b.Servicos.GetRequiredService<EsquemaDoBanco>().Faltas);
    }

    [Fact]
    public async Task A_coluna_que_a_migration_perdeu_fica_dita_no_esquema_do_sqlite()
    {
        // A ConversaDaTask registrada sem a coluna: o que restaria se só o efeito dela se perdesse.
        await using var b = new BaseDeTeste();
        await b.InicializarAsync();
        await EscalarAsync(b, "DROP INDEX \"IX_FalasRemotas_UsuarioId_MaquinaId_Sessao_DitaEmUtc\"; ALTER TABLE \"FalasRemotas\" DROP COLUMN \"Sessao\"; SELECT 0");
        SqliteConnection.ClearAllPools();

        await InicializarComTetoAsync(b);

        var esquema = b.Servicos.GetRequiredService<EsquemaDoBanco>();
        var falta = Assert.Single(esquema.Faltas);
        Assert.Equal("a coluna FalasRemotas.Sessao", falta.ToString());
        Assert.Contains("__EFMigrationsHistory", esquema.Descrever(), StringComparison.Ordinal);
        Assert.DoesNotContain("sqlserver", esquema.Descrever(), StringComparison.Ordinal);
    }
}
