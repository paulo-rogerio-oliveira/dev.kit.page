using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FalasDoDesktop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FalasRemotas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    MaquinaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FalaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Origem = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ComandoId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DitaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecebidaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FalasRemotas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FalasRemotas_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FalasRemotas_RecebidaEmUtc",
                table: "FalasRemotas",
                column: "RecebidaEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FalasRemotas_UsuarioId_MaquinaId_DitaEmUtc",
                table: "FalasRemotas",
                columns: new[] { "UsuarioId", "MaquinaId", "DitaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FalasRemotas_UsuarioId_MaquinaId_FalaId",
                table: "FalasRemotas",
                columns: new[] { "UsuarioId", "MaquinaId", "FalaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FalasRemotas");
        }
    }
}
