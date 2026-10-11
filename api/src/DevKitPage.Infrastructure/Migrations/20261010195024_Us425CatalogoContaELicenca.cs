using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevKitPage.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Us425CatalogoContaELicenca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Usuarios",
                type: "TEXT",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Impressao",
                table: "Maquinas",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AssinaturaCanceladaEmUtc",
                table: "Empresas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AssinaturaExterna",
                table: "Empresas",
                type: "TEXT",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Individual",
                table: "Empresas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "LoginObrigatorio",
                table: "Empresas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EventosDePagamento",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventoId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RecebidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosDePagamento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItensDeCatalogo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EmpresaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AutorId = table.Column<int>(type: "INTEGER", nullable: false),
                    VersaoAtual = table.Column<int>(type: "INTEGER", nullable: false),
                    HashAtual = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Dependencias = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RetiradoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensDeCatalogo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensDeCatalogo_Empresas_EmpresaId",
                        column: x => x.EmpresaId,
                        principalTable: "Empresas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItensDeCatalogo_Usuarios_AutorId",
                        column: x => x.AutorId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicencasEmitidas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MaquinaId = table.Column<int>(type: "INTEGER", nullable: false),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: true),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EmitidaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidaAteUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicencasEmitidas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicencasEmitidas_Maquinas_MaquinaId",
                        column: x => x.MaquinaId,
                        principalTable: "Maquinas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinksDeSenha",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CriadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiraEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinksDeSenha", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinksDeSenha_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrialsDeMaquina",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Impressao = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IniciadoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialsDeMaquina", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssinaturasDeCatalogo",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssinadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssinaturasDeCatalogo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssinaturasDeCatalogo_ItensDeCatalogo_ItemId",
                        column: x => x.ItemId,
                        principalTable: "ItensDeCatalogo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssinaturasDeCatalogo_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VersoesDeItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<long>(type: "INTEGER", nullable: false),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Conteudo = table.Column<string>(type: "TEXT", nullable: false),
                    AutorId = table.Column<int>(type: "INTEGER", nullable: false),
                    PublicadaEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersoesDeItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VersoesDeItem_ItensDeCatalogo_ItemId",
                        column: x => x.ItemId,
                        principalTable: "ItensDeCatalogo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_AssinaturaExterna",
                table: "Empresas",
                column: "AssinaturaExterna");

            migrationBuilder.CreateIndex(
                name: "IX_AssinaturasDeCatalogo_ItemId_UsuarioId",
                table: "AssinaturasDeCatalogo",
                columns: new[] { "ItemId", "UsuarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssinaturasDeCatalogo_UsuarioId",
                table: "AssinaturasDeCatalogo",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_EventosDePagamento_EventoId",
                table: "EventosDePagamento",
                column: "EventoId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItensDeCatalogo_AutorId",
                table: "ItensDeCatalogo",
                column: "AutorId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensDeCatalogo_EmpresaId_Tipo_Nome",
                table: "ItensDeCatalogo",
                columns: new[] { "EmpresaId", "Tipo", "Nome" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicencasEmitidas_MaquinaId_EmitidaEmUtc",
                table: "LicencasEmitidas",
                columns: new[] { "MaquinaId", "EmitidaEmUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LinksDeSenha_TokenHash",
                table: "LinksDeSenha",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LinksDeSenha_UsuarioId",
                table: "LinksDeSenha",
                column: "UsuarioId");

            migrationBuilder.CreateIndex(
                name: "IX_TrialsDeMaquina_Impressao",
                table: "TrialsDeMaquina",
                column: "Impressao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VersoesDeItem_ItemId_Versao",
                table: "VersoesDeItem",
                columns: new[] { "ItemId", "Versao" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssinaturasDeCatalogo");

            migrationBuilder.DropTable(
                name: "EventosDePagamento");

            migrationBuilder.DropTable(
                name: "LicencasEmitidas");

            migrationBuilder.DropTable(
                name: "LinksDeSenha");

            migrationBuilder.DropTable(
                name: "TrialsDeMaquina");

            migrationBuilder.DropTable(
                name: "VersoesDeItem");

            migrationBuilder.DropTable(
                name: "ItensDeCatalogo");

            migrationBuilder.DropIndex(
                name: "IX_Usuarios_Email",
                table: "Usuarios");

            migrationBuilder.DropIndex(
                name: "IX_Empresas_AssinaturaExterna",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Usuarios");

            migrationBuilder.DropColumn(
                name: "Impressao",
                table: "Maquinas");

            migrationBuilder.DropColumn(
                name: "AssinaturaCanceladaEmUtc",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "AssinaturaExterna",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "Individual",
                table: "Empresas");

            migrationBuilder.DropColumn(
                name: "LoginObrigatorio",
                table: "Empresas");
        }
    }
}
