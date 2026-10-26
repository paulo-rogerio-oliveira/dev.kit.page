using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// Os projetos compartilhados (US #693) na infraestrutura: o script do Azure SQL com as colunas do modelo, a migração
/// que cria as tabelas no SQLite, e a gravação que substitui o plano inteiro e soma a versão.
/// </summary>
public sealed class Us693Tests
{
    [Fact]
    public void Script_do_azure_sql_cria_as_tabelas_dos_projetos_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us693.sql");

        foreach (var tabela in new[] { "ProjetosCompartilhados", "ItensDeProjetoCompartilhado" })
        {
            Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
            foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
                Assert.Contains(coluna, script);
        }

        Assert.Contains("CREATE UNIQUE INDEX [IX_ProjetosCompartilhados_Espaco_Nivel_Time_UsuarioId_Slug]", script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_ItensDeProjetoCompartilhado_ProjetoId_Numero]", script);
    }

    [Fact]
    public async Task A_migracao_cria_as_tabelas_e_nao_deixa_mudanca_de_modelo_pendente()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();

        var (aplicadas, pendente) = await baseDeTeste.ComAsync(async sp =>
        {
            var db = sp.GetRequiredService<DevKitPageDb>();
            return ((await db.Database.GetAppliedMigrationsAsync()).ToArray(), db.Database.HasPendingModelChanges());
        });

        Assert.Contains(aplicadas, m => m.EndsWith("_Us693ProjetosCompartilhados", StringComparison.Ordinal));
        Assert.False(pendente);
    }

    [Fact]
    public async Task Regravar_com_a_versao_lida_substitui_o_plano_inteiro_e_soma_a_versao()
    {
        await using var baseDeTeste = new BaseDeTeste();
        await baseDeTeste.InicializarAsync();
        var usuario = await baseDeTeste.ComAsync(sp => Task.FromResult(sp.GetRequiredService<DevKitPageDb>().Usuarios.Single().Id));
        var escopo = new EscopoDoDesign(usuario, null, false);
        ItemDoProjetoV1 Item(int n, params int[] deps) => new(n, $"Item {n}", "", "planejar-e-executar", [], ["Core"], deps, 1, 0, ProjetosV1.ItemPlanejado);
        EnvioDeProjetoV1 Envio(int? versao, params ItemDoProjetoV1[] itens) => new("meu-projeto", "Meu projeto", null, null,
            ["https://github.com/acme/portal"], "feature/dev/projeto-meu-projeto", false, "texto", [], true, 2, "main",
            ProjetosV1.Rascunho, versao, itens);

        var criado = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().GravarAsync(escopo, Envio(null, Item(1), Item(2, 1)), default));
        Assert.True(criado.Valor!.Novo);
        Assert.Equal((1, "usuario", 2), (criado.Valor.Projeto.Versao, criado.Valor.Projeto.Nivel, criado.Valor.Projeto.Itens.Count));

        var regravado = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().GravarAsync(escopo, Envio(1, Item(1)), default));
        Assert.False(regravado.Valor!.Novo);
        Assert.Equal(2, regravado.Valor.Projeto.Versao);
        Assert.Single(regravado.Valor.Projeto.Itens);

        // A versão velha é conflito: quem leu a v1 não sobrescreve a v2.
        var velho = await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().GravarAsync(escopo, Envio(1, Item(1)), default));
        Assert.Equal(FalhaNaGestao.Conflito, velho.Falha);

        // Apagar leva os itens junto.
        await baseDeTeste.ComAsync(sp => sp.GetRequiredService<IProjetosDaEmpresa>().ApagarAsync(escopo, criado.Valor.Projeto.Id, default));
        Assert.Equal(0, await baseDeTeste.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().ItensDeProjetoCompartilhado.CountAsync()));
    }
}
