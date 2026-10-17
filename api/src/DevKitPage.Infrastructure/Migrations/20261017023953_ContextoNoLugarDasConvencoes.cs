using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <summary>
    /// A US #556: as convenções da US #491 viram o CONTEXTO, SEM PERDA de dados — a tabela é renomeada (nada é apagado),
    /// o time vira equipe, cada item ganha a origem (as convenções existentes são instruções do usuário) e a versão, e quem
    /// já gravou convenção de um time entra como participante APROVADO dele (quem já usa não perde o acesso). O mesmo, no
    /// Azure SQL, é o script api/scripts/sqlserver/ContextoNoLugarDasConvencoes.sql.
    /// </summary>
    public partial class ContextoNoLugarDasConvencoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.DropIndex(
                name: "IX_ConvencoesDaEmpresa_AutorId",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.RenameTable(
                name: "ConvencoesDaEmpresa",
                newName: "Contextos");

            migrationBuilder.RenameColumn(
                name: "Time",
                table: "Contextos",
                newName: "Equipe");

            migrationBuilder.AddColumn<string>(
                name: "Origem",
                table: "Contextos",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "usuario");

            migrationBuilder.AddColumn<int>(
                name: "Versao",
                table: "Contextos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("UPDATE \"Contextos\" SET \"Nivel\" = 'equipe' WHERE \"Nivel\" = 'time';");

            migrationBuilder.CreateTable(
                name: "ParticipantesDeEquipe",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: false),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Equipe = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Situacao = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParticipantesDeEquipe", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ParticipantesDeEquipe_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ParticipantesDeEquipe_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Quem já gravou a convenção de um time na US #491 participa dele, aprovado.
            migrationBuilder.Sql(
                "INSERT INTO \"ParticipantesDeEquipe\" (\"EmpresaId\", \"UsuarioId\", \"Equipe\", \"Situacao\", \"AtualizadoEmUtc\") " +
                "SELECT \"EmpresaId\", \"AutorId\", \"Equipe\", 'aprovado', MAX(\"AtualizadoEmUtc\") FROM \"Contextos\" " +
                "WHERE \"Nivel\" = 'equipe' GROUP BY \"EmpresaId\", \"AutorId\", \"Equipe\";");

            migrationBuilder.CreateIndex(
                name: "IX_Contextos_AutorId",
                table: "Contextos",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_Contextos_EmpresaId_Nivel_Equipe_UsuarioId_Chave_Origem",
                table: "Contextos",
                columns: new[] { "EmpresaId", "Nivel", "Equipe", "UsuarioId", "Chave", "Origem" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantesDeEquipe_EmpresaId_Equipe",
                table: "ParticipantesDeEquipe",
                columns: new[] { "EmpresaId", "Equipe" });

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantesDeEquipe_EmpresaId_UsuarioId_Equipe",
                table: "ParticipantesDeEquipe",
                columns: new[] { "EmpresaId", "UsuarioId", "Equipe" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ParticipantesDeEquipe_UsuarioId",
                table: "ParticipantesDeEquipe",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A volta: as instruções voltam a ser convenções; as sugestões dos agentes não existiam antes, e saem.
            migrationBuilder.DropTable(
                name: "ParticipantesDeEquipe");

            migrationBuilder.DropIndex(
                name: "IX_Contextos_EmpresaId_Nivel_Equipe_UsuarioId_Chave_Origem",
                table: "Contextos");

            migrationBuilder.DropIndex(
                name: "IX_Contextos_AutorId",
                table: "Contextos");

            migrationBuilder.Sql("DELETE FROM \"Contextos\" WHERE \"Origem\" <> 'usuario';");
            migrationBuilder.Sql("UPDATE \"Contextos\" SET \"Nivel\" = 'time' WHERE \"Nivel\" = 'equipe';");

            migrationBuilder.DropColumn(
                name: "Origem",
                table: "Contextos");

            migrationBuilder.DropColumn(
                name: "Versao",
                table: "Contextos");

            migrationBuilder.RenameColumn(
                name: "Equipe",
                table: "Contextos",
                newName: "Time");

            migrationBuilder.RenameTable(
                name: "Contextos",
                newName: "ConvencoesDaEmpresa");

            migrationBuilder.CreateIndex(
                name: "IX_ConvencoesDaEmpresa_AutorId",
                table: "ConvencoesDaEmpresa",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave",
                table: "ConvencoesDaEmpresa",
                columns: new[] { "EmpresaId", "Nivel", "Time", "UsuarioId", "Chave" },
                unique: true);
        }
    }
}
