using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>
/// Os artefatos dos projetos (controle-v4, item 2): o zip do pacote .NET no armazém e a URL do preview da página estática. No
/// Azure SQL, o mesmo vem de api/scripts/sqlserver/ArtefatosDosProjetos.sql.
/// </summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261108060000_ArtefatosDosProjetos")]
public sealed class ArtefatosDosProjetosMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("ArtefatosDosProjetos", t => new
        {
            Id = t.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            ProjetoId = t.Column<long>(type: "INTEGER", nullable: false),
            Tipo = t.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
            Repositorio = t.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
            Sha = t.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
            Chave = t.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
            NomeDoArquivo = t.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
            Url = t.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
            Ambiente = t.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
            Hash = t.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
            Tamanho = t.Column<long>(type: "INTEGER", nullable: false),
            AutorId = t.Column<int>(type: "INTEGER", nullable: false),
            CriadoEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            RetidoAteUtc = t.Column<DateTime>(type: "TEXT", nullable: true),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ArtefatosDosProjetos", x => x.Id);
            t.ForeignKey("FK_ArtefatosDosProjetos_ProjetosCompartilhados_ProjetoId", x => x.ProjetoId, "ProjetosCompartilhados", "Id",
                onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_ArtefatosDosProjetos_ProjetoId_CriadoEmUtc", "ArtefatosDosProjetos", ["ProjetoId", "CriadoEmUtc"]);
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("ArtefatosDosProjetos");
}
