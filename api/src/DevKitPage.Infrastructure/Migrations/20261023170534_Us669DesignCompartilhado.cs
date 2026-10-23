using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Us669DesignCompartilhado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjetosDeDesign",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: true),
                    Espaco = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Nivel = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Time = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Repositorio = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Chave = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjetosDeDesign", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjetosDeDesign_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelasDeDesign",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjetoId = table.Column<long>(type: "INTEGER", nullable: false),
                    Identificador = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelasDeDesign", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelasDeDesign_ProjetosDeDesign_ProjetoId",
                        column: x => x.ProjetoId,
                        principalTable: "ProjetosDeDesign",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VersoesDeDesign",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TelaId = table.Column<long>(type: "INTEGER", nullable: false),
                    Numero = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Base = table.Column<int>(type: "INTEGER", nullable: true),
                    Tema = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Largura = table.Column<int>(type: "INTEGER", nullable: false),
                    Altura = table.Column<int>(type: "INTEGER", nullable: false),
                    Origem = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Alvo = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    Commit = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    AutorId = table.Column<int>(type: "INTEGER", nullable: false),
                    AutorTipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Runner = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    CriadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EnviadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ChaveDaImagem = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersoesDeDesign", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VersoesDeDesign_TelasDeDesign_TelaId",
                        column: x => x.TelaId,
                        principalTable: "TelasDeDesign",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VersoesDeDesign_Usuarios_AutorId",
                        column: x => x.AutorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjetosDeDesign_EmpresaId_AtualizadoEmUtc",
                table: "ProjetosDeDesign",
                columns: new[] { "EmpresaId", "AtualizadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjetosDeDesign_Espaco_Nivel_Time_UsuarioId_Chave",
                table: "ProjetosDeDesign",
                columns: new[] { "Espaco", "Nivel", "Time", "UsuarioId", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjetosDeDesign_UsuarioId_AtualizadoEmUtc",
                table: "ProjetosDeDesign",
                columns: new[] { "UsuarioId", "AtualizadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TelasDeDesign_ProjetoId_Identificador",
                table: "TelasDeDesign",
                columns: new[] { "ProjetoId", "Identificador" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VersoesDeDesign_AutorId",
                table: "VersoesDeDesign",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_VersoesDeDesign_TelaId_Numero",
                table: "VersoesDeDesign",
                columns: new[] { "TelaId", "Numero" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VersoesDeDesign");

            migrationBuilder.DropTable(
                name: "TelasDeDesign");

            migrationBuilder.DropTable(
                name: "ProjetosDeDesign");
        }
    }
}
