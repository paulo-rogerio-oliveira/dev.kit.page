using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261124090000_PedidosDeArtefatos")]
public sealed class PedidosDeArtefatosMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("PedidosDeArtefatos", t => new
        {
            ProjetoId = t.Column<long>(type: "INTEGER", nullable: false),
            Id = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            Conteudo = t.Column<string>(type: "TEXT", nullable: false),
            Revisao = t.Column<long>(type: "INTEGER", nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_PedidosDeArtefatos", p => new { p.ProjetoId, p.Id });
            t.ForeignKey("FK_PedidosDeArtefatos_ProjetosCompartilhados_ProjetoId", p => p.ProjetoId,
                "ProjetosCompartilhados", "Id", onDelete: ReferentialAction.Cascade);
        });
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("PedidosDeArtefatos");
}
