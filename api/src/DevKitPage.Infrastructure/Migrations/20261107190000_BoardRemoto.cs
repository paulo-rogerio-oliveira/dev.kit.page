using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>
/// As correções do board remoto (07/11): os cards dispensados no site, o contexto do fluxo do comando (o fluxo, o agente e os
/// parâmetros) e as imagens da fala. No Azure SQL, o mesmo vem de api/scripts/sqlserver/BoardRemoto.sql.
/// </summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261107190000_BoardRemoto")]
public sealed class BoardRemotoMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("PendenciasDispensadas", t => new
        {
            Id = t.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            UsuarioId = t.Column<int>(type: "INTEGER", nullable: false),
            MaquinaId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            PendenciaId = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            DispensadaEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_PendenciasDispensadas", x => x.Id);
            t.ForeignKey("FK_PendenciasDispensadas_Usuarios_UsuarioId", x => x.UsuarioId, "Usuarios", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_PendenciasDispensadas_UsuarioId_MaquinaId_PendenciaId", "PendenciasDispensadas", ["UsuarioId", "MaquinaId", "PendenciaId"], unique: true);

        m.AddColumn<string>("Contexto", "ComandosRemotos", type: "TEXT", nullable: false, defaultValue: "");
        m.AddColumn<string>("Anexos", "FalasRemotas", type: "TEXT", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("PendenciasDispensadas");
        m.DropColumn("Contexto", "ComandosRemotos");
        m.DropColumn("Anexos", "FalasRemotas");
    }
}
