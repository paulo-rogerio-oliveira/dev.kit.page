using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConversaRemota : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComandosRemotos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    ComandoId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MaquinaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Dispositivo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    RecursoId = table.Column<long>(type: "INTEGER", nullable: true),
                    Erro = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiraEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EntregueEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConcluidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComandosRemotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComandosRemotos_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventosDaConversa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    MaquinaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Conteudo = table.Column<string>(type: "TEXT", nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosDaConversa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosDaConversa_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RespostasRemotas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    MaquinaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RespostaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ComandoId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    GeradaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecebidaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RespostasRemotas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RespostasRemotas_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComandosRemotos_Estado_ExpiraEmUtc",
                table: "ComandosRemotos",
                columns: new[] { "Estado", "ExpiraEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ComandosRemotos_UsuarioId_ComandoId",
                table: "ComandosRemotos",
                columns: new[] { "UsuarioId", "ComandoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComandosRemotos_UsuarioId_CriadoEmUtc",
                table: "ComandosRemotos",
                columns: new[] { "UsuarioId", "CriadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ComandosRemotos_UsuarioId_MaquinaId_CriadoEmUtc",
                table: "ComandosRemotos",
                columns: new[] { "UsuarioId", "MaquinaId", "CriadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventosDaConversa_EmUtc",
                table: "EventosDaConversa",
                column: "EmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EventosDaConversa_UsuarioId_Id",
                table: "EventosDaConversa",
                columns: new[] { "UsuarioId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_RespostasRemotas_RecebidaEmUtc",
                table: "RespostasRemotas",
                column: "RecebidaEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RespostasRemotas_UsuarioId_MaquinaId_RecebidaEmUtc",
                table: "RespostasRemotas",
                columns: new[] { "UsuarioId", "MaquinaId", "RecebidaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RespostasRemotas_UsuarioId_MaquinaId_RespostaId",
                table: "RespostasRemotas",
                columns: new[] { "UsuarioId", "MaquinaId", "RespostaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComandosRemotos");

            migrationBuilder.DropTable(
                name: "EventosDaConversa");

            migrationBuilder.DropTable(
                name: "RespostasRemotas");
        }
    }
}
