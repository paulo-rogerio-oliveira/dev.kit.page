using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

public sealed class MatrixPersistenciaTests
{
    [Fact]
    public async Task Atualiza_base_anterior_e_reinicia_sem_perder_dados_ou_revisoes()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            // Evolução para a frente: o Down da Matrix apaga o journal e não é recuperação.
            await db.GetService<IMigrator>().MigrateAsync("20261108060000_DispositivoDaDecisao");
            db.Usuarios.Add(new Usuario { Login = "anterior-matrix", SenhaHash = "hash-ficticio", CriadoEmUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            Assert.DoesNotContain("20261110170000_Matrix", await db.Database.GetAppliedMigrationsAsync());
            return true;
        });

        await baseDeTeste.InicializarAsync();
        await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            Assert.Equal("hash-ficticio", (await db.Usuarios.SingleAsync()).SenhaHash);
            var autor = await db.Usuarios.Select(u => u.Id).SingleAsync();
            // O journal conserva os corpos de versões antigas e extensões sem reserializar.
            foreach (var (tipo, versao, corpo) in new[]
            {
                ("matriz", 1, "{\"contrato\":1,\"matriz\":{\"celulas\":[]}}"),
                ("matriz", 2, "{\"contrato\":1,\"matriz\":{\"celulas\":[],\"coberturas\":[]}}"),
                ("evento", 1, "{\"contrato\":1,\"evento\":{\"evento\":\"gatilho:1\"}}"),
                ("registro", 1, "{\"contrato\":1,\"registro\":{\"resultado\":\"parcial\"}}")
            })
                db.Set<RevisaoMatrixNoBanco>().Add(new()
                {
                    Espaco = "usuario:" + autor, Aplicacao = "demo", Tipo = tipo, DocumentoId = "demanda",
                    Versao = versao, DonoId = autor, AutorId = autor, EmUtc = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), Documento = corpo
                });
            await db.SaveChangesAsync();
            return true;
        });
        var antes = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>()
            .Set<RevisaoMatrixNoBanco>().AsNoTracking().OrderBy(r => r.Id).ToArrayAsync());

        // Inicialização real repetida e contextos novos, sem limpar o banco temporário.
        await baseDeTeste.InicializarAsync();
        await baseDeTeste.InicializarAsync();
        await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var depois = await db.Set<RevisaoMatrixNoBanco>().AsNoTracking().OrderBy(r => r.Id).ToArrayAsync();
            Assert.Equal(antes.Select(Foto), depois.Select(Foto));
            Assert.Equal("anterior-matrix", (await db.Usuarios.SingleAsync()).Login);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            return true;
        });
    }

    private static object Foto(RevisaoMatrixNoBanco r)
        => new { r.Id, r.Espaco, r.Aplicacao, r.Tipo, r.DocumentoId, r.Versao, r.DonoId, r.ProjetoId, r.AutorId, r.EmUtc, r.Documento };

    [Fact]
    public async Task Migracao_idempotente_e_snapshot_sem_pendencia_e_sql_incluido_na_publicacao()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();
        await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            await db.Database.MigrateAsync();
            Assert.Contains("20261110170000_Matrix", await db.Database.GetAppliedMigrationsAsync());
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Equal(0, await db.Set<RevisaoMatrixNoBanco>().CountAsync());
            return true;
        });
        var sql = BaseDeTeste.ScriptDoAzureSql("Matrix.sql");
        Assert.Contains("IF OBJECT_ID", sql);
        Assert.Contains("IF NOT EXISTS", sql);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("RevisoesMatrix")) Assert.Contains(coluna, sql);
        Assert.Contains("COLLATE Latin1_General_100_BIN2", sql);
    }
}
