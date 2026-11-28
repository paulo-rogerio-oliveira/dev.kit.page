using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>
/// A marca de disparo do projeto (boards-estados, item 5): coluna opcional, para a lista de resumos separar o aprovado que
/// aguarda o disparo do em execução sem ler o documento. Os projetos gravados antes ficam sem marca; nada é apagado.
/// </summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261127090000_DisparoDoProjeto")]
public sealed class DisparoDoProjetoMigration : Migration
{
    protected override void Up(MigrationBuilder m)
        => m.AddColumn<DateTime>("DisparadoEmUtc", "ProjetosCompartilhados", type: "TEXT", nullable: true);

    protected override void Down(MigrationBuilder m) => m.DropColumn("DisparadoEmUtc", "ProjetosCompartilhados");
}
