using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261029080000_Us738OrigemDoCatalogo")]
public sealed class Us738OrigemDoCatalogo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Origem", table: "Catalogos", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>(name: "MinDevKitDaOrigem", table: "Catalogos", type: "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Origem", table: "Catalogos");
        migrationBuilder.DropColumn(name: "MinDevKitDaOrigem", table: "Catalogos");
    }
}
