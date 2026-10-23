using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <summary>
    /// A origem do contexto (US #663): <c>ConvencoesDaEmpresa</c> ganha <c>Origem</c> (<c>usuario</c>, a regra, ou
    /// <c>agente</c>, a sugestão) e <c>Versao</c>, e o índice único passa a incluir a origem — a regra e a sugestão do
    /// mesmo tema coexistem. Só acrescenta: o que já existia vira <c>Origem=usuario</c>, <c>Versao=1</c>. No Azure SQL,
    /// <c>api/scripts/sqlserver/Us663.sql</c>.
    /// </summary>
    public partial class Us663OrigemDoContexto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.AddColumn<string>(
                name: "Origem",
                table: "ConvencoesDaEmpresa",
                type: "TEXT",
                maxLength: 10,
                nullable: false,
                defaultValue: "usuario");

            migrationBuilder.AddColumn<int>(
                name: "Versao",
                table: "ConvencoesDaEmpresa",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave_Origem",
                table: "ConvencoesDaEmpresa",
                columns: new[] { "EmpresaId", "Nivel", "Time", "UsuarioId", "Chave", "Origem" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave_Origem",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.DropColumn(
                name: "Origem",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.DropColumn(
                name: "Versao",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.CreateIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave",
                table: "ConvencoesDaEmpresa",
                columns: new[] { "EmpresaId", "Nivel", "Time", "UsuarioId", "Chave" },
                unique: true);
        }
    }
}
