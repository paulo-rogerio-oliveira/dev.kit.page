using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

[DbContext(typeof(DevKitPageDb))]
[Migration("20261105073000_AcessoAosProjetos")]
public sealed class AcessoAosProjetos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("DonoId", "ProjetosCompartilhados", type: "INTEGER", nullable: false, defaultValue: 0);
        migrationBuilder.Sql("UPDATE ProjetosCompartilhados SET DonoId = CASE WHEN Nivel = 'usuario' AND UsuarioId > 0 THEN UsuarioId ELSE AutorId END");
        migrationBuilder.Sql("""
            CREATE TABLE "AcessosAosProjetos" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_AcessosAosProjetos" PRIMARY KEY AUTOINCREMENT,
                "ProjetoId" INTEGER NULL,
                "ProjetoEmOndasId" INTEGER NULL,
                "UsuarioId" INTEGER NOT NULL,
                "Papel" TEXT NOT NULL,
                "ConcedidoPorId" INTEGER NOT NULL,
                "ConcedidoEmUtc" TEXT NOT NULL,
                CONSTRAINT "CK_AcessosAosProjetos_Recurso" CHECK (
                    (ProjetoId IS NOT NULL AND ProjetoEmOndasId IS NULL) OR (ProjetoId IS NULL AND ProjetoEmOndasId IS NOT NULL)),
                CONSTRAINT "FK_AcessosAosProjetos_ProjetosDeDesign_ProjetoId" FOREIGN KEY ("ProjetoId") REFERENCES "ProjetosDeDesign" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_AcessosAosProjetos_ProjetosCompartilhados_ProjetoEmOndasId" FOREIGN KEY ("ProjetoEmOndasId") REFERENCES "ProjetosCompartilhados" ("Id"),
                CONSTRAINT "FK_AcessosAosProjetos_Usuarios_UsuarioId" FOREIGN KEY ("UsuarioId") REFERENCES "Usuarios" ("Id") ON DELETE RESTRICT
            );
            INSERT INTO "AcessosAosProjetos" ("Id", "ProjetoId", "UsuarioId", "Papel", "ConcedidoPorId", "ConcedidoEmUtc")
                SELECT "Id", "ProjetoId", "UsuarioId", "Papel", "ConcedidoPorId", "ConcedidoEmUtc" FROM "AcessosAoDesign";
            DROP TABLE "AcessosAoDesign";
            CREATE UNIQUE INDEX "IX_AcessosAosProjetos_ProjetoId_UsuarioId" ON "AcessosAosProjetos" ("ProjetoId", "UsuarioId");
            CREATE UNIQUE INDEX "IX_AcessosAosProjetos_ProjetoEmOndasId_UsuarioId" ON "AcessosAosProjetos" ("ProjetoEmOndasId", "UsuarioId");
            CREATE INDEX "IX_AcessosAosProjetos_UsuarioId" ON "AcessosAosProjetos" ("UsuarioId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => throw new NotSupportedException("A migração preserva os acessos: restaure o backup para reverter o esquema.");
}
