using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RoiDeWorkItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoisDeWorkItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaquinaId = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    De = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Ate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TurnosDoAgente = table.Column<int>(type: "INTEGER", nullable: false),
                    Sessoes = table.Column<int>(type: "INTEGER", nullable: false),
                    Horas = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    HorasNoBoard = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    HorasNoTimesheet = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    LeadTimeDias = table.Column<double>(type: "REAL", nullable: true),
                    Aberto = table.Column<bool>(type: "INTEGER", nullable: false),
                    PullRequests = table.Column<int>(type: "INTEGER", nullable: false),
                    PullRequestsMergeadas = table.Column<int>(type: "INTEGER", nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoisDeWorkItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoisDeWorkItem_Maquinas_MaquinaId",
                        column: x => x.MaquinaId,
                        principalTable: "Maquinas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoisDeWorkItem_EmUtc",
                table: "RoisDeWorkItem",
                column: "EmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RoisDeWorkItem_MaquinaId_WorkItemId",
                table: "RoisDeWorkItem",
                columns: new[] { "MaquinaId", "WorkItemId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoisDeWorkItem");
        }
    }
}
