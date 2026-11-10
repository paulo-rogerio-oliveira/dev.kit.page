using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261110170000_Matrix")]
public sealed class MatrixMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("RevisoesMatrix", t => new
        {
            Id = t.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            Espaco = t.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
            Aplicacao = t.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
            Tipo = t.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
            DocumentoId = t.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
            Versao = t.Column<int>(type: "INTEGER", nullable: false),
            DonoId = t.Column<int>(type: "INTEGER", nullable: false),
            ProjetoId = t.Column<long>(type: "INTEGER", nullable: true),
            AutorId = t.Column<int>(type: "INTEGER", nullable: false),
            EmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            Documento = t.Column<string>(type: "TEXT", nullable: false),
        }, constraints: t => t.PrimaryKey("PK_RevisoesMatrix", x => x.Id));
        m.CreateIndex("IX_RevisoesMatrix_Espaco_Aplicacao_Tipo_DocumentoId_Versao", "RevisoesMatrix",
            ["Espaco", "Aplicacao", "Tipo", "DocumentoId", "Versao"], unique: true);
        m.CreateIndex("IX_RevisoesMatrix_ProjetoId", "RevisoesMatrix", "ProjetoId");
    }
    protected override void Down(MigrationBuilder m) => m.DropTable("RevisoesMatrix");
}
