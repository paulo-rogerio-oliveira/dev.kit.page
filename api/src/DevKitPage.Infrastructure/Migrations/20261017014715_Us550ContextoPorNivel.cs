using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <summary>
    /// O contexto por nível (US #550): o <c>Valor</c> de <c>ConvencoesDaEmpresa</c> passa a ter teto de 4000 caracteres
    /// no modelo. No SQLite a coluna já é <c>TEXT</c> sem tamanho, então não há DDL — a migration fica para o modelo
    /// (o snapshot) andar junto; no Azure SQL a coluna cresce por <c>api/scripts/sqlserver/Us550.sql</c>.
    /// </summary>
    public partial class Us550ContextoPorNivel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
