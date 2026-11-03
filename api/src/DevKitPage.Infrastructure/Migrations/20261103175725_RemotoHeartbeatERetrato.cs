using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemotoHeartbeatERetrato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MaquinasRemotas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    MaquinaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    VersaoDevKit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Origem = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AppAberto = table.Column<bool>(type: "INTEGER", nullable: false),
                    EstadoDoAgente = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TurnoEmAndamento = table.Column<bool>(type: "INTEGER", nullable: false),
                    UltimoHeartbeatEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Retrato = table.Column<string>(type: "TEXT", nullable: false, defaultValue: ""),
                    RetratoGeradoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RetratoRecebidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CriadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaquinasRemotas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaquinasRemotas_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaquinasRemotas_AtualizadaEmUtc",
                table: "MaquinasRemotas",
                column: "AtualizadaEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_MaquinasRemotas_UsuarioId_MaquinaId",
                table: "MaquinasRemotas",
                columns: new[] { "UsuarioId", "MaquinaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaquinasRemotas");
        }
    }
}
