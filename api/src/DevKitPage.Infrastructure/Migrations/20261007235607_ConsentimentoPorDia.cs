using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConsentimentoPorDia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DadosDesde",
                table: "Maquinas",
                type: "TEXT",
                nullable: true);

            // As adesões que já existem: o gestor vê a partir do dia do consentimento, nunca antes.
            migrationBuilder.Sql("UPDATE Maquinas SET DadosDesde = date(ConsentiuEmUtc) WHERE ConsentiuEmUtc IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DadosDesde",
                table: "Maquinas");
        }
    }
}
