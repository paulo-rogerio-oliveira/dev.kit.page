using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PedidosDeDemonstracao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PedidosDeDemonstracao",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 254, nullable: false),
                    Empresa = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Mensagem = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ConsentimentoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecebidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PedidosDeDemonstracao", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PedidosDeDemonstracao_RecebidoEmUtc",
                table: "PedidosDeDemonstracao",
                column: "RecebidoEmUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PedidosDeDemonstracao");
        }
    }
}
