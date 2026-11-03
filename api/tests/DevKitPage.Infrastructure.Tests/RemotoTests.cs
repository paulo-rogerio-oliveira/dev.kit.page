using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O controle remoto na infraestrutura: o script do Azure SQL com as colunas do modelo, a migração sem mudança pendente,
/// a conexão contada pelo relógio da API, o isolamento por usuário e a retenção pelo último contato.
/// </summary>
public sealed class RemotoTests
{
    private static HeartbeatV1 Heartbeat(string maquina = "m-1") => new(RemotoV1.Contrato, maquina, "PC", "75", "servico", false, "", false);

    private static async Task<(BaseDeTeste Base, int Ana, int Bia)> BaseAsync()
    {
        var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();
        var ids = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var ana = new Usuario { Login = "ana", Papel = Papeis.Dev, CriadoEmUtc = baseDeTeste.Relogio.Agora.UtcDateTime };
            var bia = new Usuario { Login = "bia", Papel = Papeis.Dev, CriadoEmUtc = baseDeTeste.Relogio.Agora.UtcDateTime };
            db.Usuarios.AddRange(ana, bia);
            await db.SaveChangesAsync();
            return (ana.Id, bia.Id);
        });
        return (baseDeTeste, ids.Item1, ids.Item2);
    }

    [Fact]
    public void Script_do_azure_sql_cria_a_tabela_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Remoto.sql");

        Assert.Contains("OBJECT_ID(N'[dbo].[MaquinasRemotas]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("MaquinasRemotas"))
            Assert.Contains(coluna, script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_MaquinasRemotas_UsuarioId_MaquinaId]", script);
        Assert.Equal(["Remoto.sql"], EsquemaDoBanco.ScriptsQueTrazem("MaquinasRemotas", null));
    }

    [Fact]
    public async Task A_migracao_cria_a_tabela_e_nao_deixa_mudanca_de_modelo_pendente()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();

        var (aplicadas, pendente) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains(aplicadas, m => m.EndsWith("_RemotoHeartbeatERetrato", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    [Fact]
    public async Task A_maquina_desconecta_sem_heartbeat_pelo_relogio_da_api_e_fica_so_com_o_dono()
    {
        var (baseDeTeste, ana, bia) = await BaseAsync();
        await using var _ = baseDeTeste;
        await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IRemoto>().RegistrarHeartbeatAsync(ana, Heartbeat(), default));

        var conectada = Assert.Single(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IRemoto>().MaquinasAsync(ana, default)));
        Assert.True(conectada.Conectada);
        Assert.Equal(RemotoV1.AgenteParado, conectada.EstadoDoAgente);
        Assert.Empty(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IRemoto>().MaquinasAsync(bia, default)));

        baseDeTeste.Relogio.Agora = baseDeTeste.Relogio.Agora.AddSeconds(RemotoV1.SegundosAteDesconectar + 1);
        Assert.False(Assert.Single(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IRemoto>().MaquinasAsync(ana, default))).Conectada);
    }

    [Fact]
    public async Task A_retencao_apaga_so_a_maquina_sem_contato_alem_do_prazo()
    {
        var (baseDeTeste, ana, bia) = await BaseAsync();
        await using var _ = baseDeTeste;
        await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IRemoto>().RegistrarHeartbeatAsync(ana, Heartbeat("antiga"), default));
        baseDeTeste.Relogio.Agora = baseDeTeste.Relogio.Agora.AddDays(29);
        await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IRemoto>().RegistrarHeartbeatAsync(bia, Heartbeat("recente"), default));
        baseDeTeste.Relogio.Agora = baseDeTeste.Relogio.Agora.AddDays(2);

        var apagadas = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IExpurgoDoRemoto>().ExpurgarAsync(default));

        Assert.Equal(1, apagadas);
        var restantes = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().MaquinasRemotas.Select(m => m.MaquinaId).ToListAsync());
        Assert.Equal(["recente"], restantes);
    }
}
