using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ControleRemoto : Migration
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
                    MaquinaId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Apelido = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    VersaoDevKit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    DevKitAberto = table.Column<bool>(type: "INTEGER", nullable: false),
                    Agente = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TurnosEmAndamento = table.Column<int>(type: "INTEGER", nullable: false),
                    UltimoHeartbeatEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CriadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "RetratosRemotos",
                columns: table => new
                {
                    MaquinaRemotaId = table.Column<long>(type: "INTEGER", nullable: false),
                    GeradoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecebidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Documento = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetratosRemotos", x => x.MaquinaRemotaId);
                    table.ForeignKey(
                        name: "FK_RetratosRemotos_MaquinasRemotas_MaquinaRemotaId",
                        column: x => x.MaquinaRemotaId,
                        principalTable: "MaquinasRemotas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaquinasRemotas_UltimoHeartbeatEmUtc",
                table: "MaquinasRemotas",
                column: "UltimoHeartbeatEmUtc");

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
                name: "RetratosRemotos");

            migrationBuilder.DropTable(
                name: "MaquinasRemotas");
        }
    }
}
