using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <summary>
    /// O admin orientado a utilidade (US #687): a licença por usuário (as colunas de <c>Usuarios</c> e o histórico), os
    /// dados básicos e o contrato de <c>Empresas</c>, as fotos do "Saúde agora" do painel e o histórico e os anexos dos
    /// itens do catálogo. Só acrescenta: o que já existe fica sem validade definida (a renovação segue como antes) e sem
    /// dados novos. No Azure SQL, <c>api/scripts/sqlserver/Us687.sql</c>.
    /// </summary>
    public partial class Us687AdminDeUtilidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LicencaAlteradaEmUtc",
                table: "Usuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LicencaRevogadaEmUtc",
                table: "Usuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LicencaSemPrazo",
                table: "Usuarios",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LicencaTipo",
                table: "Usuarios",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LicencaValidaAteUtc",
                table: "Usuarios",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AlteradaEmUtc",
                table: "Empresas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AlteradaPor",
                table: "Empresas",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Cidade",
                table: "Empresas",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Cnpj",
                table: "Empresas",
                type: "TEXT",
                maxLength: 14,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Contato",
                table: "Empresas",
                type: "TEXT",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ContratoAteUtc",
                table: "Empresas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Segmento",
                table: "Empresas",
                type: "TEXT",
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Site",
                table: "Empresas",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Telefone",
                table: "Empresas",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Uf",
                table: "Empresas",
                type: "TEXT",
                maxLength: 2,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AnexosDeItemDoCatalogo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TipoDeConteudo = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Conteudo = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnexosDeItemDoCatalogo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnexosDeItemDoCatalogo_ItensEditaveisDeCatalogo_ItemId",
                        column: x => x.ItemId,
                        principalTable: "ItensEditaveisDeCatalogo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FotosDeEstado",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaquinaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Chave = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Detalhe = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Valor = table.Column<long>(type: "INTEGER", nullable: true),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FotosDeEstado", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FotosDeEstado_Maquinas_MaquinaId",
                        column: x => x.MaquinaId,
                        principalTable: "Maquinas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoricosDeItemDoCatalogo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Acao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricosDeItemDoCatalogo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricosDeItemDoCatalogo_ItensEditaveisDeCatalogo_ItemId",
                        column: x => x.ItemId,
                        principalTable: "ItensEditaveisDeCatalogo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HistoricosDeLicenca",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    Acao = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ValidaAteUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Autor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricosDeLicenca", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoricosDeLicenca_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnexosDeItemDoCatalogo_ItemId",
                table: "AnexosDeItemDoCatalogo",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_FotosDeEstado_MaquinaId_Tipo_Chave",
                table: "FotosDeEstado",
                columns: new[] { "MaquinaId", "Tipo", "Chave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FotosDeEstado_Tipo_EmUtc",
                table: "FotosDeEstado",
                columns: new[] { "Tipo", "EmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricosDeItemDoCatalogo_ItemId_EmUtc",
                table: "HistoricosDeItemDoCatalogo",
                columns: new[] { "ItemId", "EmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricosDeLicenca_UsuarioId_EmUtc",
                table: "HistoricosDeLicenca",
                columns: new[] { "UsuarioId", "EmUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnexosDeItemDoCatalogo");

            migrationBuilder.DropTable(
                name: "FotosDeEstado");

            migrationBuilder.DropTable(
                name: "HistoricosDeItemDoCatalogo");

            migrationBuilder.DropTable(
                name: "HistoricosDeLicenca");

            migrationBuilder.DropColumn(
                name: "LicencaAlteradaEmUtc",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "LicencaRevogadaEmUtc",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "LicencaSemPrazo",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "LicencaTipo",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "LicencaValidaAteUtc",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "AlteradaEmUtc",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "AlteradaPor",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Cidade",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Cnpj",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Contato",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "ContratoAteUtc",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Segmento",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Site",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Telefone",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Uf",
                table: "Empresas");
        }
    }
}
