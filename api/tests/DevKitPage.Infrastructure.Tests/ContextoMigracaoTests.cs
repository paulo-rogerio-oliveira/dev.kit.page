using System.Text.RegularExpressions;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A migração das convenções (US #491) para o contexto (US #556) SEM PERDA: numa base SQLite real parada na última
/// migration da #491, com convenções de empresa, de time e de usuário semeadas, a migration nova as leva para a tabela
/// Contextos (time → equipe, origem usuário, versão 1, mesmos valores e autores) e torna aprovado quem já gravou a de um
/// time. E o script do Azure SQL: as colunas do modelo e cada passo protegido para rodar de novo sem efeito.
/// </summary>
public sealed class ContextoMigracaoTests : IAsyncLifetime
{
    private const string UltimaDaUs491 = "20261015050210_Us491ConvencaoDoUsuario";
    private readonly BaseDeTeste _base = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _base.DisposeAsync();

    [Fact]
    public async Task As_convencoes_semeadas_antes_viram_contexto_com_os_mesmos_valores_e_autores()
    {
        var (empresa, ana, bia) = await _base.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(UltimaDaUs491);

            var nova = new Empresa { Nome = "Empresa A", Plano = "Empresarial", Assentos = 5, CodigoDeAdesao = "COD-A", CriadaEmUtc = DateTime.UtcNow };
            db.Empresas.Add(nova);
            await db.SaveChangesAsync();
            var anaNova = new Usuario { Login = "ana", Nome = "Ana", Papel = "dev", EmpresaId = nova.Id };
            var biaNova = new Usuario { Login = "bia", Nome = "Bia", Papel = "dev", EmpresaId = nova.Id };
            db.Usuarios.AddRange(anaNova, biaNova);
            await db.SaveChangesAsync();
            return (nova.Id, anaNova.Id, biaNova.Id);
        });

        await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Database.ExecuteSqlRawAsync(
            "INSERT INTO ConvencoesDaEmpresa (EmpresaId, Nivel, Time, UsuarioId, Chave, Valor, AutorId, AtualizadoEmUtc) VALUES " +
            "({0}, 'empresa', '', 0, 'branch.padrao', 'feature/{{equipe}}/{{id}}', {1}, '2026-10-15 10:00:00'), " +
            "({0}, 'time', 'vendas', 0, 'branch.bug', 'bug/{{id}}', {1}, '2026-10-15 11:00:00'), " +
            "({0}, 'time', 'vendas', 0, 'branch.hotfix', 'hotfix/{{id}}', {1}, '2026-10-15 12:00:00'), " +
            "({0}, 'usuario', '', {2}, 'branch.padrao', 'bia/{{id}}', {2}, '2026-10-15 13:00:00')",
            empresa, ana, bia));

        await _base.InicializarAsync(); // aplica a migration nova, como a subida da API

        var contextos = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Contextos.AsNoTracking().OrderBy(c => c.Id).ToListAsync());
        Assert.Equal(
            new[]
            {
                ("empresa", "", 0, "branch.padrao", "feature/{equipe}/{id}", ana),
                ("equipe", "vendas", 0, "branch.bug", "bug/{id}", ana),
                ("equipe", "vendas", 0, "branch.hotfix", "hotfix/{id}", ana),
                ("usuario", "", bia, "branch.padrao", "bia/{id}", bia),
            },
            contextos.Select(c => (c.Nivel, c.Equipe, c.UsuarioId, c.Chave, c.Valor, c.AutorId)));
        Assert.All(contextos, c => Assert.Equal(("usuario", 1), (c.Origem, c.Versao)));

        // Quem gravou a convenção do time participa dele, aprovado — uma vez, por mais convenções que tenha gravado.
        var participantes = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().ParticipantesDeEquipe.AsNoTracking().ToListAsync());
        var participante = Assert.Single(participantes);
        Assert.Equal((empresa, ana, "vendas", "aprovado"), (participante.EmpresaId, participante.UsuarioId, participante.Equipe, participante.Situacao));
    }

    [Fact]
    public async Task O_indice_unico_novo_deixa_a_instrucao_e_a_sugestao_da_mesma_chave_e_recusa_a_repetida()
    {
        await _base.InicializarAsync();
        var (empresa, autor) = await _base.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var nova = new Empresa { Nome = "Empresa A", Plano = "Empresarial", Assentos = 5, CodigoDeAdesao = "COD-A", CriadaEmUtc = DateTime.UtcNow };
            db.Empresas.Add(nova);
            await db.SaveChangesAsync();
            return (nova.Id, await db.Usuarios.Select(u => u.Id).FirstAsync());
        });

        ItemDeContexto Item(string origem) => new()
        {
            EmpresaId = empresa, Nivel = "empresa", Chave = "contexto.stack", Valor = ".NET", Origem = origem, AutorId = autor, AtualizadoEmUtc = DateTime.UtcNow,
        };

        await _base.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            db.Contextos.AddRange(Item("usuario"), Item("agente"));
            return await db.SaveChangesAsync();
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => _base.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            db.Contextos.Add(Item("usuario"));
            return await db.SaveChangesAsync();
        }));
    }

    [Theory]
    [InlineData("Contextos")]
    [InlineData("ParticipantesDeEquipe")]
    public void Script_do_azure_sql_cria_cada_tabela_com_as_colunas_do_modelo(string tabela)
    {
        var script = BaseDeTeste.ScriptDoAzureSql("ContextoNoLugarDasConvencoes.sql");

        Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
            Assert.Contains(coluna, script);
    }

    /// <summary>
    /// Idempotente: cada lote que muda o esquema (renomear, criar, alterar, remover) só roda sob um <c>IF</c> que confere o
    /// estado — rodar o script duas vezes não faz nada na segunda. A tabela é RENOMEADA, nunca apagada.
    /// </summary>
    [Fact]
    public void Script_do_azure_sql_protege_cada_passo_e_nao_apaga_a_tabela()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("ContextoNoLugarDasConvencoes.sql");
        var lotes = Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline)
            .Select(l => string.Join('\n', l.Split('\n').Where(linha => !linha.TrimStart().StartsWith("--", StringComparison.Ordinal))).Trim())
            .Where(l => l.Length > 0)
            .ToList();

        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sp_rename N'[dbo].[ConvencoesDaEmpresa]', N'Contextos'", script);
        foreach (var lote in lotes.Where(l => Regex.IsMatch(l, @"\b(CREATE|ALTER|DROP|sp_rename)\b")))
            Assert.True(lote.StartsWith("IF ", StringComparison.Ordinal), $"Lote sem proteção:\n{lote}");

        // Os dois lotes de dados também se repetem sem efeito: o UPDATE só acha 'time' uma vez, o INSERT confere o que já existe.
        Assert.Contains("WHERE [Nivel] = N'time'", script);
        Assert.Contains("NOT EXISTS (SELECT 1 FROM [dbo].[ParticipantesDeEquipe] p", script);
    }
}
