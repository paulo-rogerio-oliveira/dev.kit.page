using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>
/// (semana 7) A conversa com o agente de uma task pelo site: a task de cada fala (vazia na do Orquestrador) e o índice da
/// conversa da task. O destino da mensagem vai no Contexto do comando, sem coluna. No Azure SQL, o mesmo vem de
/// api/scripts/sqlserver/ConversaDaTask.sql.
/// </summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261109060000_ConversaDaTask")]
public sealed class ConversaDaTaskMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<string>("Sessao", "FalasRemotas", type: "TEXT", maxLength: 64, nullable: false, defaultValue: "");
        m.CreateIndex("IX_FalasRemotas_UsuarioId_MaquinaId_Sessao_DitaEmUtc", "FalasRemotas", ["UsuarioId", "MaquinaId", "Sessao", "DitaEmUtc"]);
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex("IX_FalasRemotas_UsuarioId_MaquinaId_Sessao_DitaEmUtc", "FalasRemotas");
        m.DropColumn("Sessao", "FalasRemotas");
    }
}
