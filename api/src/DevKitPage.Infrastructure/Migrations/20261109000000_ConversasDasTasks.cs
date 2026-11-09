using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>Separa as conversas das tasks da conversa do Orquestrador, preservando o histórico existente.</summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261109000000_ConversasDasTasks")]
public sealed class ConversasDasTasksMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        foreach (var tabela in new[] { "ComandosRemotos", "RespostasRemotas", "FalasRemotas" })
            m.AddColumn<string>("ConversaId", tabela, type: "TEXT", maxLength: 64, nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder m)
    {
        foreach (var tabela in new[] { "ComandosRemotos", "RespostasRemotas", "FalasRemotas" })
            m.DropColumn("ConversaId", tabela);
    }
}
