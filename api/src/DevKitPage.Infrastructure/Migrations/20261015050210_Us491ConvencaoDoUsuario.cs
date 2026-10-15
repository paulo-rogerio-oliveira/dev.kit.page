using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Us491ConvencaoDoUsuario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.AddColumn<int>(
                name: "UsuarioId",
                table: "ConvencoesDaEmpresa",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave",
                table: "ConvencoesDaEmpresa",
                columns: new[] { "EmpresaId", "Nivel", "Time", "UsuarioId", "Chave" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.DropColumn(
                name: "UsuarioId",
                table: "ConvencoesDaEmpresa");

            migrationBuilder.CreateIndex(
                name: "IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_Chave",
                table: "ConvencoesDaEmpresa",
                columns: new[] { "EmpresaId", "Nivel", "Time", "Chave" },
                unique: true);
        }
    }
}
