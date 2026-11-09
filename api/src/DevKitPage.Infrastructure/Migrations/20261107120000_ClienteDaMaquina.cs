using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevKitPage.Infrastructure.Migrations;

/// <summary>
/// O client credentials da máquina (remote-control-v3, item 9): o cliente de cada máquina, com a chave pública, e os jti já
/// gastos das asserções.
/// </summary>
[DbContext(typeof(DevKitPageDb))]
[Migration("20261107120000_ClienteDaMaquina")]
public sealed class ClienteDaMaquinaMigration : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("ClientesDaMaquina", t => new
        {
            Id = t.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            MaquinaId = t.Column<int>(type: "INTEGER", nullable: false),
            UsuarioId = t.Column<int>(type: "INTEGER", nullable: false),
            ChavePublica = t.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
            Carimbo = t.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
            RegistradoEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
            RevogadoEmUtc = t.Column<DateTime>(type: "TEXT", nullable: true),
            UltimoTokenEmUtc = t.Column<DateTime>(type: "TEXT", nullable: true),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_ClientesDaMaquina", x => x.Id);
            t.ForeignKey("FK_ClientesDaMaquina_Maquinas_MaquinaId", x => x.MaquinaId, "Maquinas", "Id", onDelete: ReferentialAction.Cascade);
            t.ForeignKey("FK_ClientesDaMaquina_Usuarios_UsuarioId", x => x.UsuarioId, "Usuarios", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_ClientesDaMaquina_MaquinaId", "ClientesDaMaquina", "MaquinaId", unique: true);
        m.CreateIndex("IX_ClientesDaMaquina_UsuarioId", "ClientesDaMaquina", "UsuarioId");

        m.CreateTable("AssercoesDoCliente", t => new
        {
            Id = t.Column<long>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            ClienteId = t.Column<int>(type: "INTEGER", nullable: false),
            Jti = t.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
            ExpiraEmUtc = t.Column<DateTime>(type: "TEXT", nullable: false),
        }, constraints: t =>
        {
            t.PrimaryKey("PK_AssercoesDoCliente", x => x.Id);
            t.ForeignKey("FK_AssercoesDoCliente_ClientesDaMaquina_ClienteId", x => x.ClienteId, "ClientesDaMaquina", "Id", onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_AssercoesDoCliente_ClienteId_Jti", "AssercoesDoCliente", ["ClienteId", "Jti"], unique: true);
        m.CreateIndex("IX_AssercoesDoCliente_ExpiraEmUtc", "AssercoesDoCliente", "ExpiraEmUtc");
    }

    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("AssercoesDoCliente");
        m.DropTable("ClientesDaMaquina");
    }
}
