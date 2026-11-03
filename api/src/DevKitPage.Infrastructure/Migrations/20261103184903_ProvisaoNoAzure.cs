using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProvisaoNoAzure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PermissoesDeUsuario",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Recurso = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ConcedidaPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ConcedidaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissoesDeUsuario", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PermissoesDeUsuario_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecursosProvisionados",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Fila = table.Column<string>(type: "TEXT", maxLength: 63, nullable: false),
                    Regiao = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    GrupoDeRecursos = table.Column<string>(type: "TEXT", maxLength: 90, nullable: false),
                    Operacao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Tentativas = table.Column<int>(type: "INTEGER", nullable: false),
                    Erro = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    IdNoAzure = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ProximaTentativaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReservadoPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ReservadoAteUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProntoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RemovidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecursosProvisionados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecursosProvisionados_Usuarios_UsuarioId",
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
                    RecursoId = table.Column<long>(type: "INTEGER", nullable: true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Evento = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Detalhe = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosDaProvisao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventosDaProvisao_RecursosProvisionados_RecursoId",
                        column: x => x.RecursoId,
                        principalTable: "RecursosProvisionados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventosDaProvisao_RecursoId_EmUtc",
                table: "EventosDaProvisao",
                columns: new[] { "RecursoId", "EmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventosDaProvisao_UsuarioId_EmUtc",
                table: "EventosDaProvisao",
                columns: new[] { "UsuarioId", "EmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PermissoesDeUsuario_UsuarioId_Recurso",
                table: "PermissoesDeUsuario",
                columns: new[] { "UsuarioId", "Recurso" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecursosProvisionados_Estado_ProximaTentativaEmUtc",
                table: "RecursosProvisionados",
                columns: new[] { "Estado", "ProximaTentativaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecursosProvisionados_Nome",
                table: "RecursosProvisionados",
                column: "Nome",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecursosProvisionados_UsuarioId_Estado",
                table: "RecursosProvisionados",
                columns: new[] { "UsuarioId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventosDaProvisao");

            migrationBuilder.DropTable(
                name: "PermissoesDeUsuario");

            migrationBuilder.DropTable(
                name: "RecursosProvisionados");
        }
    }
}
