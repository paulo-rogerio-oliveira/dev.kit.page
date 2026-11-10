using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

public sealed class MatrixPersistenciaTests
{
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
