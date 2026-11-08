using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O client credentials da máquina na infraestrutura (remote-control-v3 item 9): o script do Azure SQL com as colunas do
/// modelo, a migração sem mudança pendente, o jti de uso único, o vínculo que cai com a senha trocada e o expurgo dos jti.
/// </summary>
public sealed class ClienteDaMaquinaTests
{
    private const string Chave = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE";

    private static async Task<(BaseDeTeste Base, int Ana, int Maquina)> BaseAsync()
    {
        var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();
        var ids = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            var ana = new Usuario { Login = "ana", Papel = Papeis.Dev, SenhaHash = "hash-1", CriadoEmUtc = baseDeTeste.Relogio.Agora.UtcDateTime };
            var maquina = new Maquina { MaquinaId = "m-ana", ChaveHash = "x", RegistradaEmUtc = baseDeTeste.Relogio.Agora.UtcDateTime };
            db.Usuarios.Add(ana);
            db.Maquinas.Add(maquina);
            await db.SaveChangesAsync();
            return (ana.Id, maquina.Id);
        });
        return (baseDeTeste, ids.Item1, ids.Item2);
    }

    /// <summary>Vincula a máquina pelo serviço (a chamada não devolve nada; o <c>ComAsync</c> pede um valor).</summary>
    private static Task<bool> RegistrarAsync(BaseDeTeste baseDeTeste, int maquina, int usuario, string carimbo)
        => baseDeTeste.ComAsync(async sp =>
        {
            await sp.GetRequiredService<IClientesDaMaquina>().RegistrarAsync(maquina, usuario, Chave, carimbo, default);
            return true;
        });

    [Fact]
    public void Script_do_azure_sql_cria_as_tabelas_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("ClienteDaMaquina.sql");

        Assert.Contains("OBJECT_ID(N'[dbo].[ClientesDaMaquina]', N'U') IS NULL", script);
        Assert.Contains("OBJECT_ID(N'[dbo].[AssercoesDoCliente]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("ClientesDaMaquina").Concat(BaseDeTeste.ColunasNoSqlServer("AssercoesDoCliente")))
            Assert.Contains(coluna, script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_ClientesDaMaquina_MaquinaId]", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_AssercoesDoCliente_ClienteId_Jti]", script);
        Assert.Contains("REFERENCES [dbo].[Maquinas] ([Id]) ON DELETE CASCADE", script);
        // A verificação do esquema na subida aponta este script quando as tabelas faltam no Azure SQL.
        Assert.Equal(["ClienteDaMaquina.sql"], EsquemaDoBanco.ScriptsQueTrazem("ClientesDaMaquina", null));
        Assert.Equal(["ClienteDaMaquina.sql"], EsquemaDoBanco.ScriptsQueTrazem("AssercoesDoCliente", null));
    }

    [Fact]
    public async Task O_jti_vale_uma_vez_a_senha_trocada_derruba_o_vinculo_e_o_expurgo_leva_os_vencidos()
    {
        var (baseDeTeste, ana, maquina) = await BaseAsync();
        await using var _ = baseDeTeste;
        var (aplicadas, pendente) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });
        Assert.Contains(aplicadas, m => m.EndsWith("_ClienteDaMaquina", StringComparison.Ordinal));
        Assert.False(pendente);

        var agora = baseDeTeste.Relogio.Agora.UtcDateTime;
        await RegistrarAsync(baseDeTeste, maquina, ana, RegrasDoClienteDaMaquina.Carimbo("hash-1"));
        var cliente = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().AtivoAsync("m-ana", default)))!;
        Assert.Equal((ana, "m-ana"), (cliente.UsuarioId, cliente.ClientId));
        Assert.Equal(["m-ana"], await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().VinculadasAsync(ana, default)));

        Assert.True(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().GastarAssercaoAsync(cliente.Id, "jti-1", agora.AddMinutes(2), default)));
        Assert.False(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().GastarAssercaoAsync(cliente.Id, "jti-1", agora.AddMinutes(2), default)));
        Assert.True(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().ValeAsync(cliente.Id, ana, default)));

        // A senha trocada (outro hash): o carimbo gravado no vínculo não confere mais.
        await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Id == ana)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SenhaHash, "hash-2")));
        Assert.False(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().ValeAsync(cliente.Id, ana, default)));

        baseDeTeste.Relogio.Agora = baseDeTeste.Relogio.Agora.AddMinutes(3);
        await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IExpurgoDoRemoto>().ExpurgarAsync(default));
        Assert.Equal(0, await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().AssercoesDoCliente.CountAsync()));
    }

    [Fact]
    public async Task Desvincular_revoga_so_a_maquina_do_dono_e_o_vinculo_novo_reabre()
    {
        var (baseDeTeste, ana, maquina) = await BaseAsync();
        await using var _ = baseDeTeste;
        var carimbo = RegrasDoClienteDaMaquina.Carimbo("hash-1");
        await RegistrarAsync(baseDeTeste, maquina, ana, carimbo);

        Assert.False(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().DesvincularAsync(ana + 1, "m-ana", default)));
        Assert.True(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().DesvincularAsync(ana, "m-ana", default)));
        Assert.Null(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().AtivoAsync("m-ana", default)));
        Assert.Empty(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().VinculadasAsync(ana, default)));

        await RegistrarAsync(baseDeTeste, maquina, ana, carimbo);
        Assert.NotNull(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IClientesDaMaquina>().AtivoAsync("m-ana", default)));
        Assert.Equal(1, await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().ClientesDaMaquina.CountAsync()));
    }

    [Theory]
    [InlineData(0, 300, 60)]
    [InlineData(61, 300, 60)]
    [InlineData(15, 10, 60)]
    [InlineData(15, 300, 301)]
    public void A_configuracao_fora_da_faixa_nao_sobe(int token, int assercao, int tolerancia)
    {
        var opcoes = new OpcoesDoClienteDaMaquina { ValidadeDoTokenMinutos = token, ValidadeMaximaDaAssercaoSegundos = assercao, ToleranciaDoRelogioSegundos = tolerancia };

        var erro = Assert.Throws<InvalidOperationException>(() => OpcoesDoClienteDaMaquina.Validar(opcoes));
        Assert.StartsWith(OpcoesDoClienteDaMaquina.Secao + ":", erro.Message, StringComparison.Ordinal);
        OpcoesDoClienteDaMaquina.Validar(new OpcoesDoClienteDaMaquina());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("nao-e-base64", false)]
    [InlineData(Chave, false)]
    public void A_chave_publica_precisa_ser_um_spki_p256_inteiro(string? chave, bool valida)
        => Assert.Equal(valida, RegrasDoClienteDaMaquina.ChavePublicaValida(chave));

    [Fact]
    public void A_chave_publica_p256_do_dev_kit_e_aceita_e_a_p384_nao()
    {
        using var p256 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);

        Assert.True(RegrasDoClienteDaMaquina.ChavePublicaValida(Convert.ToBase64String(p256.ExportSubjectPublicKeyInfo())));
        Assert.False(RegrasDoClienteDaMaquina.ChavePublicaValida(Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo())));
    }
}
