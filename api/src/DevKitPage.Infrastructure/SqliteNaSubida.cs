using System.Data.Common;
using System.Globalization;
using DevKitPage.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O SQLite da API em volta das migrations da subida (API fora do ar, 16/11). Na troca de instância do App Service, o contêiner
/// novo rodava o <c>dotnet DevKitPage.Api.dll</c> e não abria a porta em 230 s (nem em 300 s), sem uma linha de log: a trava de
/// migration do EF (<see cref="TabelaDaTrava"/>, a linha que o EF 9+ grava no SQLite enquanto aplica as migrations) tinha ficado
/// com um processo que morreu com ela, e o <c>MigrateAsync</c> espera por ela para sempre.
/// <para>
/// A base de produção mora no <c>/home</c> do App Service, um compartilhamento de rede, em WAL — que não funciona em rede
/// (sqlite.org/wal.html) —, e o deploy põe o contêiner velho e o novo na mesma base: foi assim que a migration
/// <c>ConversaDaTask</c> se perdeu no deploy de 12/11 (sem a <c>FalasRemotas.Sessao</c>, as falas do chat davam 500) e a trava
/// ficou para trás. Daí o <see cref="AjustarJournalAsync"/>: no armazenamento de rede, o journal <c>delete</c>.
/// </para>
/// </summary>
public static class SqliteNaSubida
{
    /// <summary>A tabela da trava de migration que o EF cria no SQLite.</summary>
    public const string TabelaDaTrava = "__EFMigrationsLock";

    /// <summary>Os journais que a configuração pode fixar (<see cref="OpcoesDoBanco.JournalDoSqlite"/>).</summary>
    public static readonly IReadOnlySet<string> Journais =
        new HashSet<string>(["wal", "delete", "truncate", "persist"], StringComparer.OrdinalIgnoreCase);

    /// <summary>O caminho completo do arquivo da base; nulo na base em memória (ou sem arquivo).</summary>
    public static string? Arquivo(string? conexao)
    {
        if (string.IsNullOrWhiteSpace(conexao))
            return null;
        var cadeia = new SqliteConnectionStringBuilder(conexao);
        var arquivo = cadeia.DataSource;
        if (string.IsNullOrWhiteSpace(arquivo) || arquivo == ":memory:" || cadeia.Mode == SqliteOpenMode.Memory
            || arquivo.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.GetFullPath(arquivo);
    }

    /// <summary>
    /// O arquivo está num compartilhamento de rede: o caminho UNC (<c>\\servidor\...</c>) ou, no App Service
    /// (<c>WEBSITE_SITE_NAME</c>), dentro do <c>HOME</c> — o <c>/home</c> de todas as instâncias é um compartilhamento do Azure Storage.
    /// </summary>
    public static bool EmArmazenamentoDeRede(string arquivo, Func<string, string?> variavel)
    {
        if (arquivo.StartsWith(@"\\", StringComparison.Ordinal))
            return true;
        if (string.IsNullOrWhiteSpace(variavel("WEBSITE_SITE_NAME")) || variavel("HOME") is not { Length: > 0 } home)
            return false;

        static string Normal(string caminho) => caminho.Replace('\\', '/').TrimEnd('/');
        return Normal(arquivo).StartsWith(Normal(home) + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Libera a trava de migration de um processo morto: a linha da <see cref="TabelaDaTrava"/> mais velha que
    /// <paramref name="abandonadaApos"/> (ou com a data ilegível). A mais nova é de outra instância aplicando as migrations agora:
    /// ela fica, e o <c>MigrateAsync</c> espera por ela.
    /// </summary>
    /// <param name="conexao">A conexão aberta com a base.</param>
    /// <returns>A data da trava liberada (<see cref="DateTimeOffset.MinValue"/> se ilegível); nulo quando não havia trava abandonada.</returns>
    public static async Task<DateTimeOffset?> LiberarTravaAbandonadaAsync(
        DbConnection conexao, DateTimeOffset agora, TimeSpan abandonadaApos, ILogger log, CancellationToken ct = default)
    {
        await using (var existe = Comando(conexao, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tabela", ("$tabela", TabelaDaTrava)))
        {
            if (Convert.ToInt64(await existe.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 0)
                return null;
        }

        object? tomadaEm;
        await using (var ler = Comando(conexao, $"SELECT \"Timestamp\" FROM \"{TabelaDaTrava}\" WHERE \"Id\" = 1"))
        await using (var leitor = await ler.ExecuteReaderAsync(ct))
        {
            if (!await leitor.ReadAsync(ct))
                return null;
            tomadaEm = leitor.IsDBNull(0) ? null : leitor.GetValue(0);
        }

        var texto = Convert.ToString(tomadaEm, CultureInfo.InvariantCulture);
        DateTimeOffset? quando = DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lida) ? lida : null;
        if (quando is { } recente && agora - recente < abandonadaApos)
        {
            log.LogWarning("A trava de migration do SQLite é de {TomadaEm:O}: outra instância aplica as migrations, e a subida espera por ela.", recente);
            return null;
        }

        // Só a linha lida: se outra instância trocou a trava nesse meio-tempo, a dela fica.
        await using var apagar = Comando(conexao, $"DELETE FROM \"{TabelaDaTrava}\" WHERE \"Id\" = 1 AND \"Timestamp\" IS $tomadaEm",
            ("$tomadaEm", tomadaEm ?? DBNull.Value));
        if (await apagar.ExecuteNonQueryAsync(ct) == 0)
            return null;

        log.LogWarning(
            "Trava de migration do SQLite abandonada desde {TomadaEm} liberada: o processo que a tomou morreu antes de terminar as migrations, e o MigrateAsync esperaria por ela para sempre.",
            texto);
        return quando ?? DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Aplica o journal configurado: no <c>auto</c>, o <c>delete</c> só quando <paramref name="emRede"/>. Com outro processo na base
    /// (o contêiner velho, no deploy) o WAL não sai: fica o aviso, e a próxima subida tenta de novo.
    /// </summary>
    /// <param name="conexao">A conexão aberta com a base — de preferência a única.</param>
    /// <returns>O journal em vigor depois do ajuste; nulo quando não havia o que ajustar ou o SQLite recusou a troca.</returns>
    public static async Task<string?> AjustarJournalAsync(DbConnection conexao, string? configurado, bool emRede, ILogger log, CancellationToken ct = default)
    {
        var valor = (configurado ?? string.Empty).Trim();
        if (valor.Length > 0 && !Journais.Contains(valor) && !string.Equals(valor, OpcoesDoBanco.JournalAutomatico, StringComparison.OrdinalIgnoreCase))
            log.LogWarning("Banco:JournalDoSqlite '{Valor}' não é auto, wal, delete, truncate nem persist: o journal do SQLite fica como está.", valor);

        var pedido = Pedido(valor, emRede);
        if (pedido is null)
            return null;
        try
        {
            var atual = await JournalAsync(conexao, "PRAGMA journal_mode", ct);
            if (string.Equals(atual, pedido, StringComparison.OrdinalIgnoreCase))
                return atual;

            var novo = await JournalAsync(conexao, $"PRAGMA journal_mode = {pedido}", ct);
            if (string.Equals(novo, pedido, StringComparison.OrdinalIgnoreCase))
                log.LogInformation("Journal do SQLite trocado de {Antes} para {Depois}{Motivo}.", atual, novo, emRede ? " (a base está num compartilhamento de rede)" : string.Empty);
            else
                log.LogWarning("O journal do SQLite segue {Atual} (o pedido é {Pedido}): outro processo usa a base, e a próxima subida tenta de novo.", novo, pedido);
            return novo;
        }
        catch (SqliteException ex)
        {
            log.LogWarning(ex, "O journal do SQLite não foi para {Pedido} (outro processo usa a base?); a API segue no atual, e a próxima subida tenta de novo.", pedido);
            return null;
        }
    }

    /// <summary>O journal a aplicar: o fixado na configuração; no <c>auto</c>, o <c>delete</c> só em rede; nulo para não mexer.</summary>
    public static string? Pedido(string? configurado, bool emRede)
    {
        var valor = (configurado ?? string.Empty).Trim();
        if (string.Equals(valor, OpcoesDoBanco.JournalAutomatico, StringComparison.OrdinalIgnoreCase))
            return emRede ? "delete" : null;
        return Journais.Contains(valor) ? valor.ToLowerInvariant() : null;
    }

    private static async Task<string?> JournalAsync(DbConnection conexao, string pragma, CancellationToken ct)
    {
        await using var comando = Comando(conexao, pragma);
        // Sair do WAL pede a base só para esta conexão; com o contêiner velho nela, o SQLite repetiria até o timeout padrão (30 s).
        comando.CommandTimeout = 5;
        return Convert.ToString(await comando.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static DbCommand Comando(DbConnection conexao, string sql, params (string Nome, object Valor)[] parametros)
    {
        var comando = conexao.CreateCommand();
        comando.CommandText = sql;
        foreach (var (nome, valor) in parametros)
        {
            var parametro = comando.CreateParameter();
            parametro.ParameterName = nome;
            parametro.Value = valor;
            comando.Parameters.Add(parametro);
        }
        return comando;
    }
}
