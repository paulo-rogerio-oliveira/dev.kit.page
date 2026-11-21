using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>Identidade opcional e única para recuperação de upload/registro, preservando os registros anteriores.</summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261119090000_PublicacaoDoArtefato")]
public sealed class PublicacaoDoArtefatoMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("Publicacao", "ArtefatosDosProjetos", type: "TEXT", maxLength: 64, nullable: true);
        m.CreateIndex("IX_ArtefatosDosProjetos_ProjetoId_Publicacao", "ArtefatosDosProjetos",
            new[] { "ProjetoId", "Publicacao" }, unique: true, filter: "[Publicacao] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex("IX_ArtefatosDosProjetos_ProjetoId_Publicacao", "ArtefatosDosProjetos");
        m.DropColumn("Publicacao", "ArtefatosDosProjetos");
    }
}
