using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>
/// O App Service e o Static Web App (item 12) no SQLite — o espelho de <c>api/scripts/sqlserver/Provisao.sql</c>: o nome do
/// usuário, o plano, o custo, os passos feitos e a VAGA da cota, com o índice único filtrado que a torna atômica. O nome no
/// Azure passa de 24 a 60 caracteres só no modelo: o SQLite guarda TEXT sem tamanho, e reconstruir a tabela não mudaria nada.
/// </summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261105210000_AplicativosNoAzure")]
public sealed class AplicativosNoAzure : Migration
{
    private const string Tabela = "RecursosProvisionados";
    private const string IndiceDaVaga = "IX_RecursosProvisionados_UsuarioId_Tipo_Vaga";

    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<int>("Vaga", Tabela, type: "INTEGER", nullable: false, defaultValue: 0);
        m.AddColumn<string>("Rotulo", Tabela, type: "TEXT", maxLength: 20, nullable: false, defaultValue: "");
        m.AddColumn<string>("Plano", Tabela, type: "TEXT", maxLength: 20, nullable: false, defaultValue: "");
        m.AddColumn<string>("NomeDoPlano", Tabela, type: "TEXT", maxLength: 60, nullable: false, defaultValue: "");
        m.AddColumn<decimal>("CustoMensal", Tabela, type: "TEXT", precision: 10, scale: 2, nullable: false, defaultValue: 0m);
        m.AddColumn<string>("Passos", Tabela, type: "TEXT", maxLength: 100, nullable: false, defaultValue: "");

        // A vaga dos recursos ativos que já existiam (as filas do item 4): 1, 2, ... por usuário e tipo, na ordem do pedido.
        m.Sql("""
            UPDATE RecursosProvisionados SET Vaga = (
                SELECT COUNT(*) FROM RecursosProvisionados r
                WHERE r.UsuarioId = RecursosProvisionados.UsuarioId AND r.Tipo = RecursosProvisionados.Tipo
                  AND r.Estado <> 'removido' AND r.Id <= RecursosProvisionados.Id)
            WHERE Estado <> 'removido';
            """);

        m.CreateIndex(IndiceDaVaga, Tabela, ["UsuarioId", "Tipo", "Vaga"], unique: true, filter: "[Estado] <> 'removido'");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropIndex(IndiceDaVaga, Tabela);
        foreach (var coluna in new[] { "Passos", "CustoMensal", "NomeDoPlano", "Plano", "Rotulo", "Vaga" })
            m.DropColumn(coluna, Tabela);
    }
}
