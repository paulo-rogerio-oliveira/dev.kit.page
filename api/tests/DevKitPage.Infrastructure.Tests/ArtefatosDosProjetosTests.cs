using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// Os artefatos do projeto (controle-v4, item 2) na infraestrutura: o script do Azure SQL com as colunas do modelo, a migração
/// sem mudança pendente, e o projeto apagado que leva as linhas dos artefatos junto (a FK em cascata).
/// </summary>
public sealed class ArtefatosDosProjetosTests
{
    [Fact]
    public void Script_do_azure_sql_cria_a_tabela_com_as_colunas_do_modelo_e_a_pre_checagem()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("ArtefatosDosProjetos.sql");

        Assert.Contains("OBJECT_ID(N'[dbo].[ArtefatosDosProjetos]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("ArtefatosDosProjetos"))
            Assert.Contains(coluna, script);
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
