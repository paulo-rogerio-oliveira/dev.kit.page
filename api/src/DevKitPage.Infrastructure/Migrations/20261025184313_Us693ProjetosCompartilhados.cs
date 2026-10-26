using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Us693ProjetosCompartilhados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjetosCompartilhados",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: true),
                    Espaco = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Nivel = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Time = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Repositorios = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    SemanaConvertida = table.Column<bool>(type: "INTEGER", nullable: false),
                    Definicao = table.Column<string>(type: "TEXT", nullable: false),
                    Anexos = table.Column<string>(type: "TEXT", nullable: false),
                    Autonomia = table.Column<bool>(type: "INTEGER", nullable: false),
                    Paralelismo = table.Column<int>(type: "INTEGER", nullable: false),
                    DestinoDaPr = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    Situacao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    AutorId = table.Column<int>(type: "INTEGER", nullable: false),
                    AtualizadoPorId = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjetosCompartilhados", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjetosCompartilhados_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjetosCompartilhados_Usuarios_AutorId",
                        column: x => x.AutorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItensDeProjetoCompartilhado",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjetoId = table.Column<long>(type: "INTEGER", nullable: false),
                    Numero = table.Column<int>(type: "INTEGER", nullable: false),
                    Titulo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Fluxo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Repositorios = table.Column<string>(type: "TEXT", nullable: false),
                    Componentes = table.Column<string>(type: "TEXT", nullable: false),
                    Dependencias = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Onda = table.Column<int>(type: "INTEGER", nullable: false),
                    WorkItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Situacao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensDeProjetoCompartilhado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensDeProjetoCompartilhado_ProjetosCompartilhados_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "ProjetosCompartilhados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItensDeProjetoCompartilhado_ProjetoId_Numero",
                table: "ItensDeProjetoCompartilhado",
                columns: new[] { "ProjetoId", "Numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjetosCompartilhados_AutorId",
                table: "ProjetosCompartilhados",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjetosCompartilhados_EmpresaId_AtualizadoEmUtc",
                table: "ProjetosCompartilhados",
                columns: new[] { "EmpresaId", "AtualizadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjetosCompartilhados_Espaco_Nivel_Time_UsuarioId_Slug",
                table: "ProjetosCompartilhados",
                columns: new[] { "Espaco", "Nivel", "Time", "UsuarioId", "Slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensDeProjetoCompartilhado");

            migrationBuilder.DropTable(
                name: "ProjetosCompartilhados");
        }
    }
}
