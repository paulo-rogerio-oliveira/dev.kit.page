using System.Data.Common;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// (boards-estados, item 5) A marca de disparo na infraestrutura: a coluna nula que a migration e o script do Azure SQL
/// acrescentam sem tocar nos projetos gravados, a gravação que preserva o valor quando o cliente antigo não o envia e a
/// lista de resumos que a devolve sem ler o documento.
/// </summary>
public sealed class DisparoDoProjetoTests
{
    private static readonly DateTimeOffset Disparo = new(2026, 11, 27, 12, 30, 0, TimeSpan.Zero);

    private sealed class Comandos : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private static EnvioDeProjetoV1 Envio(string slug, string situacao, DateTimeOffset? disparadoEm, int? versao = null) => new(
        slug, $"Projeto {slug}", "usuario", "", ["https://github.com/acme/portal"], $"feature/dev/projeto-{slug}", false,
        "Definição", [], true, 2, "main", situacao, versao, [new(1, "Item", "", "", [], [], [], 1, 0, ProjetosV1.ItemPlanejado)],
        NivelNoBoard: "requisito", EstadosNoBoard: new(Itens: new(Finalizacao: "Review")), DisparadoEm: disparadoEm);

    [Fact]
    public void Script_do_azure_sql_acrescenta_so_a_coluna_nula_do_modelo_e_a_verificacao_o_aponta()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("DisparoDoProjeto.sql");
        var coluna = Assert.Single(BaseDeTeste.ColunasNoSqlServer("ProjetosCompartilhados"), c => c.StartsWith("[DisparadoEmUtc]", StringComparison.Ordinal));

        Assert.Equal("[DisparadoEmUtc] datetime2 NULL", coluna);
        Assert.Contains($"ADD {coluna};", script, StringComparison.Ordinal);
        Assert.Contains("IF COL_LENGTH(N'dbo.ProjetosCompartilhados', N'DisparadoEmUtc') IS NULL", script, StringComparison.Ordinal);
        Assert.Contains("THROW 50001", script, StringComparison.Ordinal);
        // A base nova (Us693.sql) já nasce com a coluna; a base existente a ganha pelo script novo, que a verificação indica.
        Assert.Contains(coluna, BaseDeTeste.ScriptDoAzureSql("Us693.sql"), StringComparison.Ordinal);
        Assert.Equal(["DisparoDoProjeto.sql"], EsquemaDoBanco.ScriptsQueTrazem("ProjetosCompartilhados", "DisparadoEmUtc"));
    }

    [Fact]
    public async Task A_migracao_acrescenta_a_coluna_e_nao_deixa_mudanca_de_modelo_pendente()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();

        var (aplicadas, pendente) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains(aplicadas, m => m.EndsWith("_DisparoDoProjeto", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    [Fact]
    public async Task Gravacao_preserva_a_marca_do_cliente_antigo_e_o_resumo_a_le_da_coluna_sem_o_documento()
    {
        await using var banco = new BaseDeTeste();
        await banco.InicializarAsync();
        await banco.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var escopo = new EscopoDoDesign(await db.Usuarios.Select(u => u.Id).SingleAsync(), null, false);
            var projetos = sp.GetRequiredService<IProjetosDaEmpresa>();
            var disparado = (await projetos.GravarAsync(escopo, Envio("disparado", ProjetosV1.Aprovado, Disparo), default)).Valor!.Projeto;
            var aguardando = (await projetos.GravarAsync(escopo, Envio("aguardando", ProjetosV1.Aprovado, null), default)).Valor!.Projeto;
            Assert.Equal(Disparo, disparado.DisparadoEm);
            Assert.Null(aguardando.DisparadoEm);

            // O cliente antigo regrava sem a marca e sem o espelho: a coluna e o documento continuam com eles.
            var antigo = Envio("disparado", ProjetosV1.Aprovado, null, disparado.Versao) with { NivelNoBoard = null, EstadosNoBoard = null };
            var regravado = (await projetos.GravarAsync(escopo, antigo, default)).Valor!.Projeto;
            Assert.Equal(disparado.Versao + 1, regravado.Versao);
            Assert.Equal(Disparo, regravado.DisparadoEm);
            Assert.Equal("requisito", regravado.NivelNoBoard);
            Assert.Equal("Review", regravado.EstadosNoBoard?.Itens?.Finalizacao);
            var linha = await db.ProjetosCompartilhados.AsNoTracking().SingleAsync(p => p.Id == disparado.Id);
            Assert.Equal(Disparo.UtcDateTime, linha.DisparadoEmUtc);
            Assert.Null((await db.ProjetosCompartilhados.AsNoTracking().SingleAsync(p => p.Id == aguardando.Id)).DisparadoEmUtc);

            var comandos = new Comandos();
            await using var leitor = new DevKitPageDb(new DbContextOptionsBuilder<DevKitPageDb>()
                .UseSqlite(db.Database.GetDbConnection()).AddInterceptors(comandos).Options);
            var resumos = await new ConsultaDeProjetos(leitor).ListarResumosAsync(escopo, null, null, default);
            Assert.Equal(Disparo, resumos.Single(r => r.Slug == "disparado").DisparadoEm);
            Assert.Null(resumos.Single(r => r.Slug == "aguardando").DisparadoEm);
            var sql = Assert.Single(comandos.Sql);
            Assert.Contains("\"DisparadoEmUtc\"", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Documento\"", sql, StringComparison.Ordinal);
            return true;
        });
    }
}
