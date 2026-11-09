using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>
/// (controle-v4, item 3) O dispositivo da decisão (celular ou navegador), opcional: a decisão gravada antes fica sem ele. No
/// Azure SQL, a mesma coluna vem de api/scripts/sqlserver/BoardRemoto.sql (o script das correções do board remoto, ainda no
/// job sql da mesma publicação).
/// </summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261108060000_DispositivoDaDecisao")]
public sealed class DispositivoDaDecisaoMigration : Migration
{
    protected override void Up(MigrationBuilder m)
        => m.AddColumn<string>("Dispositivo", "RespostasDasDecisoes", type: "TEXT", maxLength: 20, nullable: true);

    protected override void Down(MigrationBuilder m)
        => m.DropColumn("Dispositivo", "RespostasDasDecisoes");
}
