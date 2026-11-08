using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>O handshake do chat remoto (remote-control-v3, item 1): o último catálogo de cada máquina, por usuário.</summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261107000000_CatalogoRemoto")]
public sealed class CatalogoRemotoMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("CatalogosRemotos", t => new
        {
            Id = t.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            UsuarioId = t.Column<int>(type: "INTEGER", nullable: false),
            MaquinaId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            Catalogo = t.Column<string>(type: "TEXT", nullable: false),
            GeradoEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            RecebidoEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_CatalogosRemotos", x => x.Id);
            t.ForeignKey("FK_CatalogosRemotos_Usuarios_UsuarioId", x => x.UsuarioId, "Usuarios", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_CatalogosRemotos_UsuarioId_MaquinaId", "CatalogosRemotos", ["UsuarioId", "MaquinaId"], unique: true);
        m.CreateIndex("IX_CatalogosRemotos_RecebidoEmUtc", "CatalogosRemotos", "RecebidoEmUtc");
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("CatalogosRemotos");
}
