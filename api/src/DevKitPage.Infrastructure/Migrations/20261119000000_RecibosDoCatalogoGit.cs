using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261119000000_RecibosDoCatalogoGit")]
public sealed class RecibosDoCatalogoGitMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("RecibosDoCatalogoGit", t => new
        {
            Sha = t.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
            VersaoId = t.Column<long>(type: "INTEGER", nullable: false),
            Documento = t.Column<string>(type: "TEXT", nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_RecibosDoCatalogoGit", x => x.Sha);
            t.ForeignKey("FK_RecibosDoCatalogoGit_VersoesDoCatalogo_VersaoId", x => x.VersaoId, "VersoesDoCatalogo", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_RecibosDoCatalogoGit_VersaoId", "RecibosDoCatalogoGit", "VersaoId", unique: true);
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("RecibosDoCatalogoGit");
}
