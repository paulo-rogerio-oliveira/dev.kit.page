using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Provisao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LiberacoesDeProvisao",
                columns: table => new
                {
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Liberada = table.Column<bool>(type: "INTEGER", nullable: false),
                    AlteradaPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AlteradaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiberacoesDeProvisao", x => x.UsuarioId);
                    table.ForeignKey(
                        name: "FK_LiberacoesDeProvisao_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecursosNaNuvem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Geracao = table.Column<int>(type: "INTEGER", nullable: false),
                    Operacao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Regiao = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    GrupoDeRecursos = table.Column<string>(type: "TEXT", maxLength: 90, nullable: false),
                    Conta = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Fila = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Tentativas = table.Column<int>(type: "INTEGER", nullable: false),
                    ProximaTentativaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConcessaoAteUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Trabalhador = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UltimoErro = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ChaveDoCliente = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PedidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProntoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecursosNaNuvem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecursosNaNuvem_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EventosDaProvisao",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecursoId = table.Column<long>(type: "INTEGER", nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operacao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Mensagem = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosDaProvisao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosDaProvisao_RecursosNaNuvem_RecursoId",
                        column: x => x.RecursoId,
                        principalTable: "RecursosNaNuvem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosDaProvisao_RecursoId",
                table: "EventosDaProvisao",
                column: "RecursoId");

            migrationBuilder.CreateIndex(
                name: "IX_RecursosNaNuvem_Conta",
                table: "RecursosNaNuvem",
                column: "Conta",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecursosNaNuvem_Estado_ProximaTentativaEmUtc",
                table: "RecursosNaNuvem",
                columns: new[] { "Estado", "ProximaTentativaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecursosNaNuvem_UsuarioId_Tipo_Geracao",
                table: "RecursosNaNuvem",
                columns: new[] { "UsuarioId", "Tipo", "Geracao" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosDaProvisao");

            migrationBuilder.DropTable(
                name: "LiberacoesDeProvisao");

            migrationBuilder.DropTable(
                name: "RecursosNaNuvem");
        }
    }
}
