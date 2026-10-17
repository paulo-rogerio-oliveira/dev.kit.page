using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Us491ConhecimentoDaEmpresa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConvencoesDaEmpresa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nivel = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Time = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Chave = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Valor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AutorId = table.Column<int>(type: "INTEGER", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConvencoesDaEmpresa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConvencoesDaEmpresa_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConvencoesDaEmpresa_Usuarios_AutorId",
                        column: x => x.AutorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RepositoriosDaEmpresa",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Origem = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Chave = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    AutorId = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoriosDaEmpresa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RepositoriosDaEmpresa_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RepositoriosDaEmpresa_Usuarios_AutorId",
                        column: x => x.AutorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConvencoesDaEmpresa_AutorId",
                table: "ConvencoesDaEmpresa",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave",
                table: "ConvencoesDaEmpresa",
                columns: new[] { "EmpresaId", "Nivel", "Time", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RepositoriosDaEmpresa_AutorId",
                table: "RepositoriosDaEmpresa",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoriosDaEmpresa_EmpresaId_Chave",
                table: "RepositoriosDaEmpresa",
                columns: new[] { "EmpresaId", "Chave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConvencoesDaEmpresa");

            migrationBuilder.DropTable(
                name: "RepositoriosDaEmpresa");
        }
    }
}
