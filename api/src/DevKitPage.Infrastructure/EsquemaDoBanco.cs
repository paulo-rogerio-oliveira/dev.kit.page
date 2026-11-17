using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;

namespace DevKitPage.Infrastructure;

/// <summary>Uma tabela (ou uma coluna) do modelo que o banco não tem, e os scripts do Azure SQL que a trazem.</summary>
public sealed record FaltaNoEsquema(string Tabela, string? Coluna, IReadOnlyList<string> Scripts)
{
    public override string ToString()
        => (Coluna is null ? $"a tabela {Tabela}" : $"a coluna {Tabela}.{Coluna}") + (Scripts.Count > 0 ? $" ({string.Join(", ", Scripts)})" : string.Empty);
}

/// <summary>
/// A verificação do esquema do Azure SQL na subida (bug do painel Uso, 26/10). No SQL Server o esquema nasce do modelo
/// (<c>EnsureCreated</c>) e, numa base que já existe, cada tabela ou coluna nova vem de um script de
/// <c>api/scripts/sqlserver</c> rodado À MÃO — esquecido o <c>Us687.sql</c>, o painel de utilidade lia
/// <c>FotosDeEstado</c>, que não existia, e a tela mostrava só o "An error occurred while processing your request".
/// <para>
/// Aqui a API compara o modelo com o <c>INFORMATION_SCHEMA</c> e guarda o que falta, com o script que o traz: vai para o
/// log da subida, para o <c>/health</c> e para a mensagem do erro. Ela NÃO roda os scripts: idempotentes um a um, eles
/// não o são em sequência (o <c>Us491.sql</c> recria o índice único que o <c>Us663.sql</c> derruba), e o esquema de produção
/// continua mudando só pela mão de quem publica.
/// </para>
/// <para>
/// No SQLite a mesma comparação (API fora do ar, 16/11): as migrations fazem o esquema, mas o deploy de 12/11 perdeu a
/// <c>ConversaDaTask</c> na base em rede, e a <c>FalasRemotas.Sessao</c> ausente só aparecia no 500 das falas do chat.
/// </para>
/// </summary>
public sealed class EsquemaDoBanco
{
    private static readonly Lazy<IReadOnlyList<(string Nome, string Texto)>> Scripts = new(LerScripts);

    private bool _sqlite;

    /// <summary>O que o banco não tem do modelo; vazio quando está completo (ou ainda não foi verificado).</summary>
    public IReadOnlyList<FaltaNoEsquema> Faltas { get; private set; } = [];

    /// <summary>A frase do que falta, para o log, o <c>/health</c> e a mensagem do erro; vazia quando nada falta.</summary>
    public string Descrever()
        => Faltas.Count == 0
            ? string.Empty
            : $"O banco não tem {string.Join("; ", Faltas.Take(10))}{(Faltas.Count > 10 ? $" e mais {Faltas.Count - 10}" : string.Empty)}. "
              + (_sqlite
                  ? "No SQLite, a migration que traz isso não está em vigor: reinicie a API, que aplica as que faltam no __EFMigrationsHistory (se ele já registra a migration, tire-a de lá antes)."
                  : "Aplique os scripts indicados de api/scripts/sqlserver (são idempotentes) e reinicie a API.");

    /// <summary>Compara o modelo com o banco e registra o que falta: no Azure SQL, com o script que o traz; no SQLite, sem script.</summary>
    public async Task VerificarAsync(DevKitPageDb db, ILogger log, CancellationToken ct = default)
    {
        _sqlite = db.Database.IsSqlite();
        try
        {
            Faltas = Comparar(db.Model, await ColunasAsync(db, _sqlite, ct), comScripts: !_sqlite);
            if (Faltas.Count > 0)
                log.LogError("Esquema do banco incompleto: {Faltas}", Descrever());
        }
        catch (DbException ex)
        {
            // Não conseguir verificar não impede a subida: o erro de cada consulta segue explicado pelo tratamento da API.
            log.LogWarning(ex, "Não foi possível verificar o esquema do banco.");
        }
    }

    /// <summary>As tabelas e colunas do modelo que não estão em <paramref name="existentes"/> (a comparação ignora a caixa).</summary>
    /// <param name="comScripts">Com o script do Azure SQL que traz cada falta; o SQLite não tem script, tem migration.</param>
    public static IReadOnlyList<FaltaNoEsquema> Comparar(IModel modelo, IEnumerable<(string Tabela, string Coluna)> existentes, bool comScripts = true)
    {
        var colunas = existentes.Select(c => (c.Tabela.ToUpperInvariant(), c.Coluna.ToUpperInvariant())).ToHashSet();
        var tabelas = colunas.Select(c => c.Item1).ToHashSet();
        var faltas = new List<FaltaNoEsquema>();
        foreach (var grupo in modelo.GetEntityTypes().Where(e => e.GetTableName() is not null).GroupBy(e => e.GetTableName()!).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var tabela = grupo.Key;
            if (!tabelas.Contains(tabela.ToUpperInvariant()))
            {
                faltas.Add(new FaltaNoEsquema(tabela, null, comScripts ? ScriptsQueTrazem(tabela, null) : []));
                continue;
            }

            var loja = StoreObjectIdentifier.Table(tabela, grupo.First().GetSchema());
            foreach (var coluna in grupo.SelectMany(e => e.GetProperties()).Select(p => p.GetColumnName(loja)).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (!colunas.Contains((tabela.ToUpperInvariant(), coluna.ToUpperInvariant())))
                    faltas.Add(new FaltaNoEsquema(tabela, coluna, comScripts ? ScriptsQueTrazem(tabela, coluna) : []));
            }
        }

        return faltas;
    }

    /// <summary>
    /// Os scripts de <c>api/scripts/sqlserver</c> que trazem a tabela (o <c>CREATE TABLE</c> dela) ou a coluna (o
    /// <c>COL_LENGTH</c> que a acrescenta; senão, o <c>CREATE TABLE</c> que já a tem).
    /// </summary>
    public static IReadOnlyList<string> ScriptsQueTrazem(string tabela, string? coluna)
    {
        bool Cria(string texto) => texto.Contains($"CREATE TABLE [dbo].[{tabela}]", StringComparison.OrdinalIgnoreCase);
        if (coluna is null)
            return Scripts.Value.Where(s => Cria(s.Texto)).Select(s => s.Nome).ToArray();

        var acrescentam = Scripts.Value
            .Where(s => s.Texto.Contains($"[{tabela}]', N'{coluna}'", StringComparison.OrdinalIgnoreCase)
                        || s.Texto.Contains($"dbo.{tabela}', N'{coluna}'", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Nome)
            .ToArray();
        return acrescentam.Length > 0
            ? acrescentam
            : Scripts.Value.Where(s => Cria(s.Texto) && s.Texto.Contains($"[{coluna}]", StringComparison.OrdinalIgnoreCase)).Select(s => s.Nome).ToArray();
    }

    private static async Task<IReadOnlyList<(string Tabela, string Coluna)>> ColunasAsync(DevKitPageDb db, bool sqlite, CancellationToken ct)
    {
        var conexao = db.Database.GetDbConnection();
        var abriu = conexao.State != ConnectionState.Open;
        if (abriu)
            await conexao.OpenAsync(ct);
        try
        {
            await using var comando = conexao.CreateCommand();
            comando.CommandText = sqlite
                ? "SELECT m.name, p.name FROM sqlite_master AS m JOIN pragma_table_info(m.name) AS p WHERE m.type = 'table'"
                : "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo'";
            var colunas = new List<(string, string)>();
            await using var leitor = await comando.ExecuteReaderAsync(ct);
            while (await leitor.ReadAsync(ct))
                colunas.Add((leitor.GetString(0), leitor.GetString(1)));
            return colunas;
        }
        finally
        {
            if (abriu)
                await conexao.CloseAsync();
        }
    }

    /// <summary>Os scripts embutidos (o <c>.csproj</c> os inclui como <c>sqlserver/&lt;nome&gt;.sql</c>), pelo nome.</summary>
    private static IReadOnlyList<(string Nome, string Texto)> LerScripts()
    {
        var assembly = typeof(EsquemaDoBanco).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("sqlserver/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(n =>
            {
                using var leitor = new StreamReader(assembly.GetManifestResourceStream(n)!);
                return (n["sqlserver/".Length..], leitor.ReadToEnd());
            })
            .ToArray();
    }
}
