using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Us602Relatos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Relatos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: true),
                    Categoria = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Texto = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    Contexto = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    Captura = table.Column<byte[]>(type: "BLOB", nullable: true),
                    VersaoDevKit = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ChaveDoCliente = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Estado = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Resposta = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    RespondidoPorId = table.Column<int>(type: "INTEGER", nullable: true),
                    RespondidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Relatos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Relatos_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Relatos_Usuarios_RespondidoPorId",
                        column: x => x.RespondidoPorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Relatos_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Relatos_CriadoEmUtc",
                table: "Relatos",
                column: "CriadoEmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Relatos_EmpresaId_CriadoEmUtc",
                table: "Relatos",
                columns: new[] { "EmpresaId", "CriadoEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Relatos_RespondidoPorId",
                table: "Relatos",
                column: "RespondidoPorId");

            migrationBuilder.CreateIndex(
                name: "IX_Relatos_UsuarioId_ChaveDoCliente",
                table: "Relatos",
                columns: new[] { "UsuarioId", "ChaveDoCliente" },
                unique: true,
                filter: "[ChaveDoCliente] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Relatos");
        }
    }
}
