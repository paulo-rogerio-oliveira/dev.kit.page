using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Us461CatalogosNomeados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Catalogos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Basico = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Catalogos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItensEditaveisDeCatalogo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Categoria = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Arquivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Conteudo = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Dependencias = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    AlteradoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AlteradoPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RetiradoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensEditaveisDeCatalogo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensEditaveisDeCatalogo_Catalogos_CatalogoId",
                        column: x => x.CatalogoId,
                        principalTable: "Catalogos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VersoesDoCatalogo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Manifesto = table.Column<string>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Itens = table.Column<int>(type: "INTEGER", nullable: false),
                    Retirados = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PublicadaPor = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersoesDoCatalogo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VersoesDoCatalogo_Catalogos_CatalogoId",
                        column: x => x.CatalogoId,
                        principalTable: "Catalogos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VinculosDeCatalogo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CatalogoId = table.Column<int>(type: "INTEGER", nullable: false),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: false),
                    RelacionadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VinculosDeCatalogo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VinculosDeCatalogo_Catalogos_CatalogoId",
                        column: x => x.CatalogoId,
                        principalTable: "Catalogos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VinculosDeCatalogo_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PacotesDoCatalogo",
                columns: table => new
                {
                    VersaoId = table.Column<long>(type: "INTEGER", nullable: false),
                    Zip = table.Column<byte[]>(type: "BLOB", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PacotesDoCatalogo", x => x.VersaoId);
                    table.ForeignKey(
                        name: "FK_PacotesDoCatalogo_VersoesDoCatalogo_VersaoId",
                        column: x => x.VersaoId,
                        principalTable: "VersoesDoCatalogo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogos_Slug",
                table: "Catalogos",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItensEditaveisDeCatalogo_CatalogoId_Tipo_Nome",
                table: "ItensEditaveisDeCatalogo",
                columns: new[] { "CatalogoId", "Tipo", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VersoesDoCatalogo_CatalogoId_Versao",
                table: "VersoesDoCatalogo",
                columns: new[] { "CatalogoId", "Versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VinculosDeCatalogo_CatalogoId_EmpresaId",
                table: "VinculosDeCatalogo",
                columns: new[] { "CatalogoId", "EmpresaId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VinculosDeCatalogo_EmpresaId",
                table: "VinculosDeCatalogo",
                column: "EmpresaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensEditaveisDeCatalogo");

            migrationBuilder.DropTable(
                name: "PacotesDoCatalogo");

            migrationBuilder.DropTable(
                name: "VinculosDeCatalogo");

            migrationBuilder.DropTable(
                name: "VersoesDoCatalogo");

            migrationBuilder.DropTable(
                name: "Catalogos");
        }
    }
}
