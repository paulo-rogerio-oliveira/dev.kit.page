using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261106020000_PushDoBoard")]
public sealed class PushDoBoardMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("AssinaturasDoPush", t => new
        {
            Id = t.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
            UsuarioId = t.Column<int>(type: "INTEGER", nullable: false),
            EndpointHash = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            DadosProtegidos = t.Column<string>(type: "TEXT", maxLength: 5000, nullable: false),
            ChavePublicaServidor = t.Column<string>(type: "TEXT", maxLength: 87, nullable: false),
            AtualizadaEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            VerificadaEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AssinaturasDoPush", x => x.Id);
            t.ForeignKey("FK_AssinaturasDoPush_Usuarios_UsuarioId", x => x.UsuarioId, "Usuarios", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_AssinaturasDoPush_EndpointHash", "AssinaturasDoPush", "EndpointHash", unique: true);
        m.CreateIndex("IX_AssinaturasDoPush_UsuarioId", "AssinaturasDoPush", "UsuarioId");
        m.CreateIndex("IX_AssinaturasDoPush_VerificadaEmUtc", "AssinaturasDoPush", "VerificadaEmUtc");
        m.CreateIndex("IX_AssinaturasDoPush_AtualizadaEmUtc", "AssinaturasDoPush", "AtualizadaEmUtc");
        m.CreateTable("EntregasDoPush", t => new
        {
            Id = t.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
            AssinaturaId = t.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
            MaquinaId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            PendenciaId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            SnapshotId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            Estado = t.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
            Reserva = t.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
            Tentativas = t.Column<int>(type: "INTEGER", nullable: false),
            CriadaEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            ExpiraEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            ProximaTentativaUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_EntregasDoPush", x => x.Id);
            t.ForeignKey("FK_EntregasDoPush_AssinaturasDoPush_AssinaturaId", x => x.AssinaturaId, "AssinaturasDoPush", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_EntregasDoPush_AssinaturaId_MaquinaId_PendenciaId_SnapshotId", "EntregasDoPush", ["AssinaturaId", "MaquinaId", "PendenciaId", "SnapshotId"], unique: true);
        m.CreateIndex("IX_EntregasDoPush_Estado_ProximaTentativaUtc", "EntregasDoPush", ["Estado", "ProximaTentativaUtc"]);
        m.CreateIndex("IX_EntregasDoPush_CriadaEmUtc", "EntregasDoPush", "CriadaEmUtc");
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("EntregasDoPush");
        m.DropTable("AssinaturasDoPush");
    }
}
