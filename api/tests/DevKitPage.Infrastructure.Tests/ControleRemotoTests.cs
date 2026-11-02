using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O controle remoto no banco: o script do Azure SQL e a migration contra o modelo, o heartbeat pelo relógio do servidor, o
/// retrato fora de ordem, o teto de máquinas e o expurgo.
/// </summary>
public sealed class ControleRemotoTests : IAsyncLifetime
{
    private const string Maquina = "3f2b1c9e5d7a4b60a1c2d3e4f5061728";

    private readonly BaseDeTeste _base = new(new Dictionary<string, string?> { ["Remoto:MaximoDeMaquinas"] = "2", ["Remoto:RetencaoDias"] = "30" });
    private int _usuario;

    public async Task InitializeAsync()
    {
        await _base.InicializarAsync();
        _usuario = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Select(u => u.Id).SingleAsync());
    }

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private static HeartbeatV1 Sinal(string maquina = Maquina, string apelido = "Notebook", string agente = RemotoV1.AgenteAtivo)
        => new(RemotoV1.Versao, maquina, apelido, "1.61.0", false, agente, 2);

    private static RetratoV1 Foto(DateTimeOffset geradoEm, string nome = "Portal")
        => new(RemotoV1.Versao, Maquina, geradoEm, [new("portal", nome, ProjetosV1.Aprovado, 3, 1, 1, 1, 2)], [], []);

    private Task<T> ComRemotoAsync<T>(Func<IControleRemoto, Task<T>> acao)
        => _base.ComAsync(sp => acao(sp.GetRequiredService<IControleRemoto>()));

    private Task<ResultadoDaGestao<HeartbeatRecebidoV1>> HeartbeatAsync(HeartbeatV1 sinal)
        => ComRemotoAsync(r => r.RegistrarHeartbeatAsync(_usuario, sinal, default));

    private Task<ResultadoDaGestao<RetratoRecebidoV1>> RetratoAsync(RetratoV1 foto)
        => ComRemotoAsync(r => r.RegistrarRetratoAsync(_usuario, foto, default));

    private Task<PainelRemotoV1?> PainelAsync() => ComRemotoAsync(r => r.PainelAsync(_usuario, Maquina, default));

    [Fact]
    public void Script_do_azure_sql_cria_as_tabelas_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("ControleRemoto.sql");

        foreach (var tabela in new[] { "MaquinasRemotas", "RetratosRemotos" })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
                Assert.Contains(coluna, script);
            Assert.Equal(["ControleRemoto.sql"], EsquemaDoBanco.ScriptsQueTrazem(tabela, null));
        }

        Assert.Contains("CREATE UNIQUE INDEX [IX_MaquinasRemotas_UsuarioId_MaquinaId]", script);
        Assert.Contains("CREATE INDEX [IX_MaquinasRemotas_UltimoHeartbeatEmUtc]", script);
    }

    [Fact]
    public async Task A_migracao_cria_as_tabelas_e_nao_deixa_mudanca_de_modelo_pendente()
    {
        var (aplicadas, pendente) = await _base.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains(aplicadas, m => m.EndsWith("_ControleRemoto", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    [Fact]
    public async Task O_heartbeat_seguinte_atualiza_a_mesma_maquina_mesmo_escrita_de_outro_jeito()
    {
        await HeartbeatAsync(Sinal());
        _base.Relogio.Agora = _base.Relogio.Agora.AddMinutes(1);
        await HeartbeatAsync(Sinal("3F2B1C9E-5D7A-4B60-A1C2-D3E4F5061728", "Outro nome", RemotoV1.AgentePausado));

        var maquina = Assert.Single(await ComRemotoAsync(r => r.MaquinasAsync(_usuario, default)));
        Assert.Equal((Maquina, "Outro nome", RemotoV1.AgentePausado, 2, false, "1.61.0"),
            (maquina.MaquinaId, maquina.Apelido, maquina.Agente, maquina.TurnosEmAndamento, maquina.DevKitAberto, maquina.VersaoDevKit));
        Assert.Equal(_base.Relogio.Agora, maquina.UltimoHeartbeat);
    }

    [Fact]
    public async Task Sem_apelido_a_maquina_ganha_o_nome_derivado_do_id()
    {
        await HeartbeatAsync(Sinal(apelido: " "));

        Assert.Equal("máquina 3f2b1c9e", Assert.Single(await ComRemotoAsync(r => r.MaquinasAsync(_usuario, default))).Apelido);
    }

    [Fact]
    public async Task A_maquina_cai_quando_o_heartbeat_passa_da_tolerancia_e_o_estado_fica_como_o_ultimo_conhecido()
    {
        await HeartbeatAsync(Sinal());

        _base.Relogio.Agora = _base.Relogio.Agora.AddSeconds(180);
        Assert.True((await PainelAsync())!.Maquina.Conectada);

        _base.Relogio.Agora = _base.Relogio.Agora.AddSeconds(1);
        var maquina = (await PainelAsync())!.Maquina;
        Assert.False(maquina.Conectada);
        Assert.Equal(RemotoV1.AgenteAtivo, maquina.Agente);
    }

    [Fact]
    public async Task Dois_primeiros_heartbeats_ao_mesmo_tempo_deixam_uma_maquina_so()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => HeartbeatAsync(Sinal())));

        Assert.Single(await ComRemotoAsync(r => r.MaquinasAsync(_usuario, default)));
    }

    [Fact]
    public async Task A_maquina_acima_do_teto_e_recusada_e_as_que_ja_existem_continuam()
    {
        const string segunda = "00000000000000000000000000000002";
        await HeartbeatAsync(Sinal());
        await HeartbeatAsync(Sinal(segunda));

        var terceira = await HeartbeatAsync(Sinal("00000000000000000000000000000003"));

        Assert.Equal(FalhaNaGestao.Conflito, terceira.Falha);
        Assert.NotNull((await HeartbeatAsync(Sinal(segunda))).Valor);
        Assert.Equal(2, (await ComRemotoAsync(r => r.MaquinasAsync(_usuario, default))).Count);
    }

    [Fact]
    public async Task O_retrato_mais_recente_vence_e_o_atrasado_e_o_reenvio_nao_mudam_nada()
    {
        await HeartbeatAsync(Sinal());
        var agora = _base.Relogio.Agora;

        Assert.True((await RetratoAsync(Foto(agora.AddMinutes(-2), "Novo"))).Valor!.Aplicado);
        var atrasado = (await RetratoAsync(Foto(agora.AddMinutes(-3), "Velho"))).Valor!;
        var reenvio = (await RetratoAsync(Foto(agora.AddMinutes(-2), "Reenvio"))).Valor!;

        Assert.False(atrasado.Aplicado);
        Assert.Equal(agora.AddMinutes(-2), atrasado.GeradoEm);
        Assert.False(reenvio.Aplicado);
        Assert.Equal("Novo", (await PainelAsync())!.Retrato!.Projetos[0].Nome);

        Assert.True((await RetratoAsync(Foto(agora.AddMinutes(-1), "Mais novo"))).Valor!.Aplicado);
        var painel = (await PainelAsync())!;
        Assert.Equal("Mais novo", painel.Retrato!.Projetos[0].Nome);
        Assert.Equal(agora, painel.Maquina.RetratoRecebidoEm);
    }

    [Fact]
    public async Task O_relogio_adiantado_da_maquina_nao_congela_a_foto()
    {
        await HeartbeatAsync(Sinal());
        var agora = _base.Relogio.Agora;

        Assert.True((await RetratoAsync(Foto(agora.AddYears(10), "Adiantado"))).Valor!.Aplicado);
        Assert.Equal(agora, (await PainelAsync())!.Retrato!.GeradoEm);

        _base.Relogio.Agora = agora.AddMinutes(1);
        Assert.True((await RetratoAsync(Foto(_base.Relogio.Agora, "Certo"))).Valor!.Aplicado);
        Assert.Equal("Certo", (await PainelAsync())!.Retrato!.Projetos[0].Nome);
    }

    [Fact]
    public async Task O_heartbeat_nao_toca_no_retrato()
    {
        await HeartbeatAsync(Sinal());
        await RetratoAsync(Foto(_base.Relogio.Agora));

        await HeartbeatAsync(Sinal());

        Assert.NotNull((await PainelAsync())!.Retrato);
    }

    [Fact]
    public async Task O_expurgo_tira_a_maquina_parada_com_o_retrato_e_deixa_a_ativa()
    {
        const string ativa = "00000000000000000000000000000002";
        await HeartbeatAsync(Sinal());
        await RetratoAsync(Foto(_base.Relogio.Agora));

        _base.Relogio.Agora = _base.Relogio.Agora.AddDays(31);
        await HeartbeatAsync(Sinal(ativa));
        var apagadas = await _base.ComAsync(sp => sp.GetRequiredService<IExpurgoDoRemoto>().ExpurgarAsync(default));

        Assert.Equal(1, apagadas);
        Assert.Equal(ativa, Assert.Single(await ComRemotoAsync(r => r.MaquinasAsync(_usuario, default))).MaquinaId);
        Assert.Equal(0, await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().RetratosRemotos.CountAsync()));
    }
}
