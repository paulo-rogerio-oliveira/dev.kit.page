using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations;

public partial class Us747ProjetosCompartilhados : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(
            name: "Documento", table: "ProjetosCompartilhados", type: "TEXT",
            nullable: false, defaultValue: "");

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "Documento", table: "ProjetosCompartilhados");
}
