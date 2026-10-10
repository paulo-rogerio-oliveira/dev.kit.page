using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AvaliacoesDeEntrega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvaliacoesDeEntrega",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaquinaId = table.Column<int>(type: "INTEGER", nullable: false),
                    SessaoId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Turno = table.Column<int>(type: "INTEGER", nullable: false),
                    Boa = table.Column<bool>(type: "INTEGER", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Agente = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Modelo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Fluxo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WorkItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Dia = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvaliacoesDeEntrega", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AvaliacoesDeEntrega_Maquinas_MaquinaId",
                        column: x => x.MaquinaId,
                        principalTable: "Maquinas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvaliacoesDeEntrega_EmUtc",
                table: "AvaliacoesDeEntrega",
                column: "EmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AvaliacoesDeEntrega_MaquinaId_SessaoId_Turno",
                table: "AvaliacoesDeEntrega",
                columns: new[] { "MaquinaId", "SessaoId", "Turno" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvaliacoesDeEntrega");
        }
    }
}
