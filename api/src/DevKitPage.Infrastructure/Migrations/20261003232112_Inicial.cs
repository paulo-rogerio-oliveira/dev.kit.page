using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Maquinas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaquinaId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Apelido = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ChaveHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    VersaoDevKit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RegistradaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UltimoEnvioEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Maquinas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Login = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SenhaHash = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EhAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeveTrocarSenha = table.Column<bool>(type: "INTEGER", nullable: false),
                    FalhasSeguidas = table.Column<int>(type: "INTEGER", nullable: false),
                    BloqueadoAteUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventosDeUso",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MaquinaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SessaoId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Quantidade = table.Column<int>(type: "INTEGER", nullable: false),
                    Valor = table.Column<long>(type: "INTEGER", nullable: true),
                    Detalhe = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Dia = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    RecebidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosDeUso", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosDeUso_Maquinas_MaquinaId",
                        column: x => x.MaquinaId,
                        principalTable: "Maquinas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TotaisDiarios",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaquinaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Dia = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Detalhe = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Quantidade = table.Column<long>(type: "INTEGER", nullable: false),
                    Eventos = table.Column<long>(type: "INTEGER", nullable: false),
                    Valor = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TotaisDiarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TotaisDiarios_Maquinas_MaquinaId",
                        column: x => x.MaquinaId,
                        principalTable: "Maquinas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosDeUso_EmUtc",
                table: "EventosDeUso",
                column: "EmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EventosDeUso_EventId",
                table: "EventosDeUso",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventosDeUso_MaquinaId_EmUtc",
                table: "EventosDeUso",
                columns: new[] { "MaquinaId", "EmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Maquinas_ChaveHash",
                table: "Maquinas",
                column: "ChaveHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Maquinas_MaquinaId",
                table: "Maquinas",
                column: "MaquinaId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TotaisDiarios_Dia",
                table: "TotaisDiarios",
                column: "Dia");

            migrationBuilder.CreateIndex(
                name: "IX_TotaisDiarios_MaquinaId_Dia_Tipo_Detalhe",
                table: "TotaisDiarios",
                columns: new[] { "MaquinaId", "Dia", "Tipo", "Detalhe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Login",
                table: "Usuarios",
                column: "Login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosDeUso");

            migrationBuilder.DropTable(
                name: "TotaisDiarios");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "Maquinas");
        }
    }
}
