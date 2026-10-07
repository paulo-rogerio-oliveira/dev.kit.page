using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GruposDeErro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GruposDeErro",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Assinatura = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ResolvidoNaVersao = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PrimeiraVersao = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UltimaVersao = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PrimeiroVistoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UltimoVistoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GruposDeErro", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OcorrenciasDeErro",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GrupoId = table.Column<long>(type: "INTEGER", nullable: false),
                    MaquinaId = table.Column<int>(type: "INTEGER", nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VersaoDevKit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Trace = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OcorrenciasDeErro", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OcorrenciasDeErro_GruposDeErro_GrupoId",
                        column: x => x.GrupoId,
                        principalTable: "GruposDeErro",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OcorrenciasDeErro_Maquinas_MaquinaId",
                        column: x => x.MaquinaId,
                        principalTable: "Maquinas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GruposDeErro_Assinatura",
                table: "GruposDeErro",
                column: "Assinatura",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GruposDeErro_UltimoVistoEmUtc",
                table: "GruposDeErro",
                column: "UltimoVistoEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OcorrenciasDeErro_EmUtc",
                table: "OcorrenciasDeErro",
                column: "EmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OcorrenciasDeErro_GrupoId_EmUtc",
                table: "OcorrenciasDeErro",
                columns: new[] { "GrupoId", "EmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OcorrenciasDeErro_MaquinaId",
                table: "OcorrenciasDeErro",
                column: "MaquinaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OcorrenciasDeErro");

            migrationBuilder.DropTable(
                name: "GruposDeErro");
        }
    }
}
