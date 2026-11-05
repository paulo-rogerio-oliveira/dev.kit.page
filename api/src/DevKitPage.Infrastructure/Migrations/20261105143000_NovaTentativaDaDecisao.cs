using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261105143000_NovaTentativaDaDecisao")]
public sealed class NovaTentativaDaDecisao : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.DropIndex("IX_RespostasDasDecisoes_UsuarioId_MaquinaId_PendenciaId_SnapshotId", "RespostasDasDecisoes");
        m.CreateIndex("IX_RespostasDasDecisoes_UsuarioId_MaquinaId_PendenciaId_SnapshotId", "RespostasDasDecisoes",
            ["UsuarioId", "MaquinaId", "PendenciaId", "SnapshotId"], unique: true, filter: "[Estado] <> 'vencida'");
    }

    protected override void Down(MigrationBuilder m)
    {
        // Não elimina tentativas auditadas: a reversão recusa se já houver histórico repetido.
        m.DropIndex("IX_RespostasDasDecisoes_UsuarioId_MaquinaId_PendenciaId_SnapshotId", "RespostasDasDecisoes");
        m.CreateIndex("IX_RespostasDasDecisoes_UsuarioId_MaquinaId_PendenciaId_SnapshotId", "RespostasDasDecisoes",
            ["UsuarioId", "MaquinaId", "PendenciaId", "SnapshotId"], unique: true);
    }
}
