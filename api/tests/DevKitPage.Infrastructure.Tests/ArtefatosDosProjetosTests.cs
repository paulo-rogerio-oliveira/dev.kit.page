using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// Os artefatos do projeto (controle-v4, item 2) na infraestrutura: o script do Azure SQL com as colunas do modelo, a migração
/// sem mudança pendente, e o projeto apagado que leva as linhas dos artefatos junto (a FK em cascata).
/// </summary>
public sealed class ArtefatosDosProjetosTests
{
    [Fact]
    public void Blob_exige_chave_para_assinar_e_link_tem_somente_leitura_https_e_prazo()
    {
        // Conexões fictícias: gerar a SAS é local, sem consultar Storage.
        var semChave = new ArmazemDeArtefatosNoBlob(Options.Create(new OpcoesDosArtefatos
        {
            ConexaoDoBlob = "BlobEndpoint=https://contateste.blob.core.windows.net/;SharedAccessSignature=sv=2025-01-05&sig=ficticia",
        }));
        Assert.Throws<InvalidOperationException>(() => semChave.Link("projetos/1/app.zip", "app.zip", DateTimeOffset.UtcNow.AddHours(1)));
        var chave = Convert.ToBase64String(new byte[32]);
        var comChave = new ArmazemDeArtefatosNoBlob(Options.Create(new OpcoesDosArtefatos
        {
            ConexaoDoBlob = "DefaultEndpointsProtocol=https;AccountName=contateste;AccountKey=" + chave + ";EndpointSuffix=core.windows.net",
        }));
        var ate = DateTimeOffset.UtcNow.AddHours(1);
        var link = new Uri(comChave.Link("projetos/1/app.zip", "app.zip", ate));
        Assert.Equal("https", link.Scheme);
        var parametros = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Query);
        Assert.Equal("r", parametros["sp"].ToString());
        Assert.Equal("https", parametros["spr"].ToString());
        Assert.Equal("b", parametros["sr"].ToString());
        Assert.Equal(ate.ToUnixTimeSeconds(), DateTimeOffset.Parse(parametros["se"].ToString()).ToUnixTimeSeconds());
        Assert.Contains("app.zip", parametros["rscd"].ToString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(parametros["sig"]));
        Assert.DoesNotContain(chave, link.ToString(), StringComparison.Ordinal);
    }

    private sealed class Armazem : IArmazemDeArtefatos
    {
        public Dictionary<string, byte[]> Arquivos { get; } = [];
        public bool SemAssinatura { get; set; }
        public bool Interromper { get; set; }
        public async Task GravarAsync(string chave, Stream conteudo, CancellationToken ct)
        {
            using var memoria = new MemoryStream();
            await conteudo.CopyToAsync(memoria, ct);
            Arquivos[chave] = memoria.ToArray();
            if (Interromper) throw new IOException("Upload interrompido.");
        }
        public Task ApagarAsync(string chave, CancellationToken ct) { Arquivos.Remove(chave); return Task.CompletedTask; }
        public string Link(string chave, string nomeDoArquivo, DateTimeOffset expira)
            => SemAssinatura ? throw new InvalidOperationException("Storage sem assinatura.") : "https://download.exemplo/" + chave;
    }

    [Fact]
    public async Task Confere_tamanho_real_recupera_interrupcao_e_repete_sem_duplicar_nem_prolongar_retencao()
    {
        var armazem = new Armazem();
        await using var baseDeTeste = new BaseDeTeste(new() { ["Artefatos:TamanhoMaximoMb"] = "1" },
            s => s.AddSingleton<IArmazemDeArtefatos>(armazem));
        await baseDeTeste.InicializarAsync();
        var usuario = await baseDeTeste.ComAsync(sp => Task.FromResult(sp.GetRequiredService<DevKitPageDb>().Usuarios.Single().Id));
        var escopo = new EscopoDoDesign(usuario, null, false);
        var projeto = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().GravarAsync(escopo,
            new EnvioDeProjetoV1("upload", "Upload", null, null, ["https://github.com/acme/app"], "feature/upload", false,
                "", [], true, 2, "main", ProjetosV1.Rascunho, null, []), default))).Valor!.Projeto;
        var arquivo = Path.GetTempFileName();
        try
        {
            async Task<ResultadoDaGestao<ArtefatoV1>> Enviar(long declarado = 1) => await baseDeTeste.ComAsync(sp =>
                sp.GetRequiredService<IArtefatosDosProjetos>().RegistrarPacoteAsync(escopo, projeto.Id,
                    new("https://github.com/acme/app", "abcdef1234567890", null, "app.zip", arquivo, declarado), default));

            await File.WriteAllBytesAsync(arquivo, new byte[1024 * 1024 + 1]);
            Assert.Equal(ArtefatosDosProjetosNoBanco.CodigoDoTeto, (await Enviar()).Codigo);
            Assert.Empty(armazem.Arquivos);
            await File.WriteAllBytesAsync(arquivo, [1, 2, 3, 4]);
            armazem.SemAssinatura = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => Enviar());
            Assert.Empty(armazem.Arquivos);
            Assert.Equal(0, await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().ArtefatosDosProjetos.CountAsync()));
            armazem.SemAssinatura = false;
            armazem.Interromper = true;
            await Assert.ThrowsAsync<IOException>(() => Enviar());
            Assert.Empty(armazem.Arquivos);
            armazem.Interromper = false;
            var primeiro = (await Enviar()).Valor!;
            Assert.Equal(4, primeiro.Tamanho);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(new byte[] { 1, 2, 3, 4 })), primeiro.Hash);
            baseDeTeste.Relogio.Agora = baseDeTeste.Relogio.Agora.AddDays(1);
            var repetido = (await Enviar(999)).Valor!;
            Assert.Equal((primeiro.Id, primeiro.RetidoAte), (repetido.Id, repetido.RetidoAte));
            Assert.Single(armazem.Arquivos);
            Assert.Equal(1, await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().ArtefatosDosProjetos.CountAsync()));
            baseDeTeste.Relogio.Agora = baseDeTeste.Relogio.Agora.AddDays(8);
            Assert.Null((await Enviar()).Valor);
        }
        finally { File.Delete(arquivo); }
    }

    [Fact]
    public void Script_do_azure_sql_cria_a_tabela_com_as_colunas_do_modelo_e_a_pre_checagem()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("ArtefatosDosProjetos.sql");

        Assert.Contains("OBJECT_ID(N'[dbo].[ArtefatosDosProjetos]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("ArtefatosDosProjetos"))
            Assert.Contains(coluna, script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("PedidosDeArtefatos"))
            Assert.Contains(coluna, script);
        Assert.Contains("CONSTRAINT [PK_PedidosDeArtefatos] PRIMARY KEY ([ProjetoId], [Id])", script);
        Assert.Contains("THROW 50001", script);
        Assert.Contains("REFERENCES [dbo].[ProjetosCompartilhados] ([Id]) ON DELETE CASCADE", script);
        Assert.Contains("CREATE INDEX [IX_ArtefatosDosProjetos_ProjetoId_CriadoEmUtc]", script);
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

        Assert.Contains(aplicadas, m => m.EndsWith("_ArtefatosDosProjetos", StringComparison.Ordinal));
        Assert.Contains(aplicadas, m => m.EndsWith("_PedidosDeArtefatos", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    [Fact]
    public async Task Apagar_o_projeto_leva_os_artefatos_dele()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();
        var usuario = await baseDeTeste.ComAsync(sp => Task.FromResult(sp.GetRequiredService<DevKitPageDb>().Usuarios.Single().Id));
        var escopo = new EscopoDoDesign(usuario, null, false);
        var projeto = (await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().GravarAsync(escopo,
            new EnvioDeProjetoV1("com-artefato", "Com artefato", null, null, ["https://github.com/acme/portal"],
                "feature/dev/projeto-com-artefato", false, "", [], true, 2, "main", ProjetosV1.Rascunho, null, []), default))).Valor!.Projeto;

        var registrado = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IArtefatosDosProjetos>().RegistrarPreviewAsync(escopo, projeto.Id,
            new PreviewDoProjetoV1("https://github.com/acme/portal", "3f9c2a1", "https://portal-com-artefato.azurestaticapps.net", "com-artefato"), default));
        Assert.NotNull(registrado.Valor);

        await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().ApagarAsync(escopo, projeto.Id, default));
        Assert.Equal(0, await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().ArtefatosDosProjetos.CountAsync()));
    }
}
