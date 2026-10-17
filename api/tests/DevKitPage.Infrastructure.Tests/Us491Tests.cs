using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A US #491 na base de verdade: o catálogo de repositórios da empresa (o mesmo caminho escrito de dois jeitos é UM
/// repositório, gravar de novo troca a descrição, o filtro, o isolamento entre empresas) e o script do Azure SQL. As
/// convenções viraram o contexto (US #556): <see cref="ContextoDaEmpresaTests"/> e <see cref="ContextoMigracaoTests"/>.
/// </summary>
public sealed class Us491Tests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new(new() { ["Conta:UrlDoSite"] = "https://site.test" });

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Com<T>(Func<IServiceProvider, Task<T>> acao) => _base.ComAsync(acao);

    private Task<T> Conhecimento<T>(Func<IConhecimentoDaEmpresa, Task<T>> acao) => Com(sp => acao(sp.GetRequiredService<IConhecimentoDaEmpresa>()));

    private Task<EmpresaResumo> EmpresaAsync(string nome)
        => Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova(nome, "Empresarial", 5), default));

    /// <summary>Um dev que aderiu pelo código e definiu a senha pelo link; devolve o escopo dele.</summary>
    private async Task<EscopoDaEmpresa> DevAsync(EmpresaResumo empresa, string email, bool gestor = false)
    {
        Assert.NotNull((await Com(sp => sp.GetRequiredService<IContas>().AderirAsync(new AdesaoV1(empresa.CodigoDeAdesao, email, "Dev"), default))).Valor);
        Assert.Empty(await Com(sp => sp.GetRequiredService<IContas>().DefinirSenhaAsync(_base.Emails.TokenPara(email), "senhaDoDev2026", default)));
        var id = await Com(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return new EscopoDaEmpresa(empresa.Id, id, gestor); // o papel de gestor vem do token
    }

    [Fact]
    public async Task O_mesmo_caminho_e_um_repositorio_e_gravar_de_novo_troca_a_descricao()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var bia = await DevAsync(a, "bia@a.com");

        await Conhecimento(c => c.GravarRepositorioAsync(ana, new GravacaoDeRepositorioV1("https://github.com/acme/GBL-2000.git", "ERP"), default));
        var regravado = await Conhecimento(c => c.GravarRepositorioAsync(bia, new GravacaoDeRepositorioV1("https://github.com/acme/gbl-2000/", "ERP de vendas (.NET 8)"), default));
        await Conhecimento(c => c.GravarRepositorioAsync(bia, new GravacaoDeRepositorioV1("https://github.com/acme/uau-1006", "Portal do cliente"), default));

        Assert.Equal("https://github.com/acme/gbl-2000/", regravado.Valor!.Origem);
        var lista = await Conhecimento(c => c.RepositoriosAsync(ana, null, default));
        Assert.Equal(2, lista.Count);
        Assert.Equal("ERP de vendas (.NET 8)", lista.Single(r => r.Origem.Contains("gbl", StringComparison.Ordinal)).Descricao);
        Assert.Equal(new[] { "Portal do cliente" }, (await Conhecimento(c => c.RepositoriosAsync(ana, "PORTAL", default))).Select(r => r.Descricao));
    }

    [Fact]
    public async Task Outra_empresa_nao_ve_o_catalogo()
    {
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        var gestorDeA = await DevAsync(a, "gestor@a.com", gestor: true);
        var caio = await DevAsync(b, "caio@b.com");

        await Conhecimento(c => c.GravarRepositorioAsync(gestorDeA, new GravacaoDeRepositorioV1("https://github.com/acme/gbl-2000", "ERP"), default));

        Assert.Empty(await Conhecimento(c => c.RepositoriosAsync(caio, null, default)));
    }

    [Fact]
    public async Task Remove_do_catalogo_o_gestor_ou_quem_gravou()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var bia = await DevAsync(a, "bia@a.com");
        await Conhecimento(c => c.GravarRepositorioAsync(ana, new GravacaoDeRepositorioV1("https://github.com/acme/gbl-2000", "ERP"), default));

        Assert.Equal(FalhaNaGestao.Proibida, (await Conhecimento(c => c.RemoverRepositorioAsync(bia, "https://github.com/acme/GBL-2000.git", default))).Falha);
        Assert.NotNull((await Conhecimento(c => c.RemoverRepositorioAsync(ana, "https://github.com/acme/gbl-2000/", default))).Valor);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Conhecimento(c => c.RemoverRepositorioAsync(ana, "https://github.com/acme/gbl-2000", default))).Falha);
    }

    /// <summary>A tabela das convenções virou a Contextos (US #556): o script dela é o ContextoNoLugarDasConvencoes.sql.</summary>
    [Fact]
    public void Script_do_azure_sql_cria_o_catalogo_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us491.sql");

        Assert.Contains("OBJECT_ID(N'[dbo].[RepositoriosDaEmpresa]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("RepositoriosDaEmpresa"))
            Assert.Contains(coluna, script);
    }
}
