using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261105024000_DecisoesESegundoFator")]
public sealed class DecisoesESegundoFator : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("Autenticadores", t => new
        {
            UsuarioId = t.Column<int>(type: "INTEGER", nullable: false),
            SegredoProtegido = t.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
            Ativo = t.Column<bool>(type: "INTEGER", nullable: false),
            PreparadoEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            UltimoPasso = t.Column<long>(type: "INTEGER", nullable: false),
            Tentativas = t.Column<int>(type: "INTEGER", nullable: false),
            JanelaEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_Autenticadores", x => x.UsuarioId);
            t.ForeignKey("FK_Autenticadores_Usuarios_UsuarioId", x => x.UsuarioId, "Usuarios", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateTable("RespostasDasDecisoes", t => new
        {
            Id = t.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            UsuarioId = t.Column<int>(type: "INTEGER", nullable: false),
            MaquinaId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            PendenciaId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            SnapshotId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            Acao = t.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
            Opcao = t.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
            Origem = t.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
            Destino = t.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
            Sha = t.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
            EmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            SegundoFator = t.Column<bool>(type: "INTEGER", nullable: false),
            Estado = t.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_RespostasDasDecisoes", x => x.Id);
            t.ForeignKey("FK_RespostasDasDecisoes_Usuarios_UsuarioId", x => x.UsuarioId, "Usuarios", "Id", onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_RespostasDasDecisoes_UsuarioId_MaquinaId_PendenciaId_SnapshotId", "RespostasDasDecisoes", ["UsuarioId", "MaquinaId", "PendenciaId", "SnapshotId"], unique: true);
        m.CreateIndex("IX_RespostasDasDecisoes_UsuarioId_MaquinaId_Estado", "RespostasDasDecisoes", ["UsuarioId", "MaquinaId", "Estado"]);
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("RespostasDasDecisoes"); m.DropTable("Autenticadores");
    }
}
