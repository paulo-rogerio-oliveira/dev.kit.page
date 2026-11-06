using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>
/// O ACESSO aos projetos de design: o dono do projeto (<c>DonoId</c>) e os acessos que ele concede (leitor ou editor). Os
/// projetos que já existem ganham o dono reconhecido — o do nível usuário, ou quem enviou a primeira versão — e quem
/// já tinha enviado versões vira editor, para ninguém perder o envio. Espelhada por <c>api/scripts/sqlserver/DesignAcesso.sql</c>.
/// </summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261105060000_DesignAcesso")]
public sealed class DesignAcesso : Migration
{
    /// <summary>O dono de cada projeto existente: o do nível usuário, ou o autor da primeira versão enviada.</summary>
    internal const string DonosDosProjetos = """
        UPDATE "ProjetosDeDesign" SET "DonoId" = CASE
            WHEN "Nivel" = 'usuario' AND "UsuarioId" > 0 THEN "UsuarioId"
            ELSE COALESCE((SELECT v."AutorId" FROM "VersoesDeDesign" v INNER JOIN "TelasDeDesign" t ON t."Id" = v."TelaId"
                WHERE t."ProjetoId" = "ProjetosDeDesign"."Id" ORDER BY v."EnviadaEmUtc", v."Id" LIMIT 1), 0)
        END
        WHERE "DonoId" = 0;
        """;

    /// <summary>Quem já enviou versões a um projeto, sem ser o dono, fica editor (concedido por 0: a migração).</summary>
    internal const string EditoresDosProjetos = """
        INSERT INTO "AcessosAoDesign" ("ProjetoId", "UsuarioId", "Papel", "ConcedidoPorId", "ConcedidoEmUtc")
        SELECT DISTINCT t."ProjetoId", v."AutorId", 'editor', 0, strftime('%Y-%m-%d %H:%M:%S', 'now')
        FROM "VersoesDeDesign" v
        INNER JOIN "TelasDeDesign" t ON t."Id" = v."TelaId"
        INNER JOIN "ProjetosDeDesign" p ON p."Id" = t."ProjetoId"
        WHERE v."AutorId" <> p."DonoId";
        """;

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "DonoId", table: "ProjetosDeDesign", type: "INTEGER", nullable: false, defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "AcessosAoDesign",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ProjetoId = table.Column<long>(type: "INTEGER", nullable: false),
                UsuarioId = table.Column<int>(type: "INTEGER", nullable: false),
                Papel = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                ConcedidoPorId = table.Column<int>(type: "INTEGER", nullable: false),
                ConcedidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AcessosAoDesign", x => x.Id);
                table.ForeignKey(
                    name: "FK_AcessosAoDesign_ProjetosDeDesign_ProjetoId",
                    column: x => x.ProjetoId,
                    principalTable: "ProjetosDeDesign",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_AcessosAoDesign_Usuarios_UsuarioId",
                    column: x => x.UsuarioId,
                    principalTable: "Usuarios",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "IX_AcessosAoDesign_ProjetoId_UsuarioId", table: "AcessosAoDesign", columns: ["ProjetoId", "UsuarioId"], unique: true);
        migrationBuilder.CreateIndex(name: "IX_AcessosAoDesign_UsuarioId", table: "AcessosAoDesign", column: "UsuarioId");

        migrationBuilder.Sql(DonosDosProjetos);
        migrationBuilder.Sql(EditoresDosProjetos);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AcessosAoDesign");
        migrationBuilder.DropColumn(name: "DonoId", table: "ProjetosDeDesign");
    }
}
