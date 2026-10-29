using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Us747ProjetosCompartilhados : Migration
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
                    Espaco = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Nivel = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Time = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 96, nullable: false),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Documento = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjetosCompartilhados", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjetosCompartilhados_AtualizadoEmUtc",
                table: "ProjetosCompartilhados",
                column: "AtualizadoEmUtc");

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
                name: "ProjetosCompartilhados");
        }
    }
}
