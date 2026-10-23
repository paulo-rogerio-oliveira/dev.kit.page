using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O design compartilhado (US #669) na infraestrutura: o script do Azure SQL com as colunas do modelo, o armazém em
/// disco preso à pasta, e o envio que perde a imagem não deixa versão órfã no banco.
/// </summary>
public sealed class Us669Tests
{
    [Fact]
    public void Script_do_azure_sql_cria_as_tabelas_do_design_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us669.sql");

        foreach (var tabela in new[] { "ProjetosDeDesign", "TelasDeDesign", "VersoesDeDesign" })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
                Assert.Contains(coluna, script);
        }

        Assert.Contains("CREATE UNIQUE INDEX [IX_VersoesDeDesign_TelaId_Numero]", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_ProjetosDeDesign_Espaco_Nivel_Time_UsuarioId_Chave]", script);
    }

    [Fact]
    public async Task O_armazem_em_disco_grava_le_apaga_e_nao_sai_da_pasta()
    {
        var pasta = Path.Combine(Path.GetTempPath(), $"devkitpage-armazem-{Guid.NewGuid():N}");
        try
        {
            var armazem = new ArmazemDeImagensEmDisco(Options.Create(new OpcoesDoDesign { Pasta = pasta }));
            await armazem.GravarAsync("empresa-1/7/home/v001-abc.png", [1, 2, 3], default);

            Assert.Equal(new byte[] { 1, 2, 3 }, await armazem.LerAsync("empresa-1/7/home/v001-abc.png", default));
            await armazem.ApagarAsync("empresa-1/7/home/v001-abc.png", default);
            Assert.Null(await armazem.LerAsync("empresa-1/7/home/v001-abc.png", default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => armazem.GravarAsync("../fora.png", [1], default));
        }
        finally
        {
            if (Directory.Exists(pasta))
                Directory.Delete(pasta, recursive: true);
        }
    }

    [Fact]
    public async Task O_envio_cuja_imagem_falha_nao_deixa_versao_no_banco()
    {
        var pasta = Path.Combine(Path.GetTempPath(), $"devkitpage-design-{Guid.NewGuid():N}");
        await using var baseDeTeste = new BaseDeTeste(new() { ["Design:Pasta"] = pasta });
        await baseDeTeste.InicializarAsync();
        var usuario = await baseDeTeste.ComAsync(sp => Task.FromResult(sp.GetRequiredService<DevKitPageDb>().Usuarios.Single().Id));
        var escopo = new EscopoDoDesign(usuario, null, false);
        var envio = new EnvioDeVersaoDeDesign("https://github.com/acme/portal", null, null, "home", null, null, 1, "captura", null,
            "light", "web", "http://x", "main", "abc", null, "agente", null, null);
        var png = Png();

        await baseDeTeste.ComAsync(async sp =>
        {
            var falho = new DesignDaEmpresa(sp.GetRequiredService<DevKitPageDb>(), new ArmazemQueFalha(),
                sp.GetRequiredService<IOptions<OpcoesDoDesign>>(), TimeProvider.System);
            await Assert.ThrowsAsync<IOException>(() => falho.EnviarAsync(escopo, envio, png, default));
            return 0;
        });

        // O reenvio (a fila do dev.kit) grava a versão de novo, como nova.
        var reenvio = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IDesignDaEmpresa>().EnviarAsync(escopo, envio, png, default));
        Assert.Equal(SituacaoDoEnvio.Nova, reenvio.Situacao);
        Assert.NotNull(await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IDesignDaEmpresa>().ImagemAsync(escopo, reenvio.Versao!.Projeto, "home", 1, default)));
        if (Directory.Exists(pasta))
            Directory.Delete(pasta, recursive: true);
    }

    private static byte[] Png()
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        png.AddRange([0, 0, 0, 10, 0, 0, 0, 10, 8, 6, 0, 0, 0]);
        return png.ToArray();
    }

    private sealed class ArmazemQueFalha : IArmazemDeImagens
    {
        public Task GravarAsync(string chave, byte[] conteudo, CancellationToken ct) => throw new IOException("blob fora do ar");

        public Task<byte[]?> LerAsync(string chave, CancellationToken ct) => Task.FromResult<byte[]?>(null);

        public Task ApagarAsync(string chave, CancellationToken ct) => Task.CompletedTask;
    }
}
