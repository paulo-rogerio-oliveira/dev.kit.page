using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

public sealed class ProjetoAcessoTests
{
    [Fact]
    public async Task Migracao_preserva_o_dono_atual_e_os_acessos_do_design_na_base_compartilhada()
    {
        await using var baseDeTeste = new BaseDeTeste();
        var dono = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            await db.GetService<IMigrator>().MigrateAsync("20261105060000_DesignAcesso");
            var u = new Usuario { Login = "dono", Papel = "dev", CriadoEmUtc = DateTime.UtcNow };
            db.Usuarios.Add(u);
            await db.SaveChangesAsync();
            var id = u.Id;
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO ProjetosCompartilhados
                (Id, Espaco, Nivel, Time, UsuarioId, Slug, Nome, Repositorios, Branch, SemanaConvertida, Definicao, Anexos,
                 Autonomia, Paralelismo, DestinoDaPr, Situacao, Versao, AutorId, AtualizadoPorId, CriadoEmUtc, AtualizadoEmUtc)
                VALUES (1, {"usuario:" + id}, 'usuario', '', {id}, 'legado', 'Legado', '', 'feature/dev/projeto-legado',
                    0, '', '', 1, 2, 'main', 'rascunho', 1, {id}, {id}, '2026-11-01', '2026-11-01')
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO ProjetosDeDesign (Id, Espaco, Nivel, Time, UsuarioId, Repositorio, Chave, DonoId, CriadoEmUtc, AtualizadoEmUtc)
                VALUES (1, {"usuario:" + id}, 'usuario', '', {id}, 'app', 'app', {id}, '2026-11-01', '2026-11-01')
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO AcessosAoDesign (ProjetoId, UsuarioId, Papel, ConcedidoPorId, ConcedidoEmUtc)
                VALUES (1, {id}, 'editor', {id}, '2026-11-01')
                """);
            await db.GetService<IMigrator>().MigrateAsync();
            return id;
        });
        await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var p = await db.ProjetosCompartilhados.SingleAsync();
            Assert.Equal(dono, p.DonoId);
            Assert.Equal(1, p.Versao);
            var a = await db.AcessosAosProjetos.SingleAsync();
            Assert.Equal(1L, a.ProjetoId);
            Assert.Equal((dono, "editor"), (a.UsuarioId, a.Papel));
            Assert.Null(a.ProjetoEmOndasId);
            Assert.False(db.Database.HasPendingModelChanges());
            var acessos = await sp.GetRequiredService<IAcessosAosProjetos>().ListarAsync(new(dono, null, false), AcessoV1.RecursoProjeto, p.Id, default);
            Assert.Equal(dono, acessos.Valor!.Dono!.Id);
            return 0;
        });
    }

    [Fact]
    public void Script_sql_server_tem_as_colunas_e_preserva_os_dados_existentes()
    {
        var sql = BaseDeTeste.ScriptDoAzureSql("AcessoAosProjetos.sql");
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("AcessosAosProjetos"))
            Assert.Contains(coluna, sql);
        Assert.Contains("[DonoId] int NOT NULL", sql);
        Assert.Contains("ELSE [AutorId] END", sql);
        Assert.Contains("FROM [dbo].[AcessosAoDesign]", sql);
        Assert.Contains("WHERE [ProjetoEmOndasId] IS NOT NULL", sql);
    }
}
