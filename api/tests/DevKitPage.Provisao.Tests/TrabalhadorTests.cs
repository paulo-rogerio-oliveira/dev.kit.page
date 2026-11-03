using Azure.Identity;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace DevKitPage.Provisao.Tests;

/// <summary>
/// A composição do trabalhador: o motor e o provedor do Azure com a identidade gerenciada dele, o simulado só fora de
/// Production, e o nome da réplica na concessão.
/// </summary>
public sealed class TrabalhadorTests
{
    private static ServiceProvider Composicao(string ambiente, Dictionary<string, string?> valores)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
        var servicos = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(cfg);
        servicos.AdicionarInfraestrutura(cfg);
        servicos.AdicionarMotorDeProvisao();
        servicos.AdicionarNuvem(cfg, new HostingEnvironment { EnvironmentName = ambiente });
        return servicos.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [Fact]
    public void O_trabalhador_monta_o_motor_com_o_provedor_do_azure_e_a_identidade_atribuida()
    {
        using var servicos = Composicao(Environments.Production, new()
        {
            ["Provisao:Azure:AssinaturaId"] = "0b1c2d3e-4f50-6172-8394-a5b6c7d8e9f0",
            ["Provisao:Azure:IdentidadeClientId"] = "5f6e7d8c-9b0a-4c1d-8e2f-3a4b5c6d7e8f",
        });
        using var escopo = servicos.CreateScope();

        Assert.IsType<MotorDeProvisao>(escopo.ServiceProvider.GetRequiredService<IMotorDeProvisao>());
        Assert.IsType<ProvedorAzure>(escopo.ServiceProvider.GetRequiredService<IProvedorDeNuvem>());
        Assert.IsType<ManagedIdentityCredential>(escopo.ServiceProvider.GetRequiredService<Azure.Core.TokenCredential>());
    }

    [Fact]
    public void Sem_o_client_id_a_credencial_e_a_do_ambiente_de_quem_desenvolve()
        => Assert.IsType<DefaultAzureCredential>(ComposicaoDaNuvem.Credencial(new OpcoesDoAzure()));

    [Fact]
    public void O_simulado_vale_fora_de_production_e_e_recusado_nela()
    {
        using var desenvolvimento = Composicao(Environments.Development, new() { ["Provisao:Azure:Provedor"] = "simulado" });
        Assert.IsType<ProvedorSimulado>(desenvolvimento.GetRequiredService<IProvedorDeNuvem>());

        var erro = Assert.Throws<InvalidOperationException>(() => Composicao(Environments.Production, new() { ["Provisao:Azure:Provedor"] = "simulado" }));
        Assert.Contains("Production", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_simulado_passa_por_em_andamento_antes_de_pronto_e_recusa_a_regiao_de_falha()
    {
        var simulado = new ProvedorSimulado();
        var alvo = new AlvoNaNuvem("rg-devkit-u1", "dkabcdefghijklmnop", "comandos", "brazilsouth", RegrasDaProvisao.Etiquetas("dev", 1, 1));

        Assert.Equal(SituacaoNaNuvem.EmAndamento, (await simulado.GarantirFilaAsync(alvo, default)).Situacao);
        Assert.Equal(SituacaoNaNuvem.Pronto, (await simulado.GarantirFilaAsync(alvo, default)).Situacao);
        Assert.Equal(SituacaoNaNuvem.EmAndamento, (await simulado.RemoverFilaAsync(alvo, default)).Situacao);
        Assert.Equal(SituacaoNaNuvem.Pronto, (await simulado.RemoverFilaAsync(alvo, default)).Situacao);
        Assert.Equal(SituacaoNaNuvem.FalhaDefinitiva, (await simulado.GarantirFilaAsync(alvo with { Regiao = ProvedorSimulado.RegiaoQueFalha }, default)).Situacao);
    }

    [Theory]
    [InlineData("ca-provisao-7f9c", 12, "ca-provisao-7f9c-12")]
    [InlineData("DEV PC_01", 3, "dev-pc-01-3")]
    [InlineData("", 3, "trabalhador-3")]
    public void O_nome_da_replica_e_seguro_para_a_auditoria(string maquina, int processo, string esperado)
        => Assert.Equal(esperado, TrabalhadorDaProvisao.NomeDoTrabalhador(maquina, processo));

    [Fact]
    public void O_nome_da_replica_cabe_na_coluna()
        => Assert.Equal(80, TrabalhadorDaProvisao.NomeDoTrabalhador(new string('a', 200), 123456).Length);
}
