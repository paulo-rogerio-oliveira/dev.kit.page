using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261031230000_UltimoLoginDoUsuario")]
public sealed class UltimoLoginDoUsuario : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "UltimoLoginEmUtc", table: "Usuarios", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "UltimoLoginEmUtc", table: "Usuarios");
    }
}
