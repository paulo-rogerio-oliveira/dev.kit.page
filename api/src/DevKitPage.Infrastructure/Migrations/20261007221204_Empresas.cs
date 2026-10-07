using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Empresas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Usuarios",
                type: "INTEGER",
                nullable: true);

            // Todo usuário que já existe é o admin semeado: a coluna nasce "admin" para ele.
            migrationBuilder.AddColumn<string>(
                name: "Papel",
                table: "Usuarios",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "admin");

            migrationBuilder.AddColumn<string>(
                name: "Colaborador",
                table: "Maquinas",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentiuEmUtc",
                table: "Maquinas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmpresaId",
                table: "Maquinas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AcessosAosDados",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Login = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: true),
                    OQue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Linhas = table.Column<int>(type: "INTEGER", nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcessosAosDados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Empresas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Plano = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Assentos = table.Column<int>(type: "INTEGER", nullable: false),
                    CodigoDeAdesao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CriadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empresas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_EmpresaId",
                table: "Usuarios",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_Maquinas_EmpresaId",
                table: "Maquinas",
                column: "EmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_AcessosAosDados_EmUtc",
                table: "AcessosAosDados",
                column: "EmUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_CodigoDeAdesao",
                table: "Empresas",
                column: "CodigoDeAdesao",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Maquinas_Empresas_EmpresaId",
                table: "Maquinas",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Usuarios_Empresas_EmpresaId",
                table: "Usuarios",
                column: "EmpresaId",
                principalTable: "Empresas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Maquinas_Empresas_EmpresaId",
                table: "Maquinas");

            migrationBuilder.DropForeignKey(
                name: "FK_Usuarios_Empresas_EmpresaId",
                table: "Usuarios");

            migrationBuilder.DropTable(
                name: "AcessosAosDados");

            migrationBuilder.DropTable(
                name: "Empresas");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_EmpresaId",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Maquinas_EmpresaId",
                table: "Maquinas");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Papel",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Colaborador",
                table: "Maquinas");

            migrationBuilder.DropColumn(
                name: "ConsentiuEmUtc",
                table: "Maquinas");

            migrationBuilder.DropColumn(
                name: "EmpresaId",
                table: "Maquinas");
        }
    }
}
