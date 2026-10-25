using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A US #491 na base de verdade: o catálogo de repositórios da empresa (o mesmo caminho escrito de dois jeitos é UM
/// repositório, gravar de novo troca a descrição, o filtro, o isolamento entre empresas) e as convenções (a da empresa é
/// do gestor, a do time de qualquer um, valor vazio apaga, cada time vê só a dele) — e o script do Azure SQL.
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
    public async Task Outra_empresa_nao_ve_o_catalogo_nem_as_convencoes()
    {
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        var gestorDeA = await DevAsync(a, "gestor@a.com", gestor: true);
        var caio = await DevAsync(b, "caio@b.com");

        await Conhecimento(c => c.GravarRepositorioAsync(gestorDeA, new GravacaoDeRepositorioV1("https://github.com/acme/gbl-2000", "ERP"), default));
        await Conhecimento(c => c.GravarConvencaoAsync(gestorDeA, new ConvencaoV1("empresa", "branch.padrao", "a/{id}"), default));

        Assert.Empty(await Conhecimento(c => c.RepositoriosAsync(caio, null, default)));
        Assert.Empty(await Conhecimento(c => c.ConvencoesAsync(caio, null, default)));
    }

    /// <summary>A da empresa é do gestor; a do time, de qualquer um; cada time vê só a dele; vazio apaga.</summary>
    [Fact]
    public async Task Convencoes_da_empresa_sao_do_gestor_e_as_do_time_de_quem_trabalha_nele()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);

        var recusada = await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("empresa", "branch.padrao", "x/{id}"), default));
        Assert.Equal(FalhaNaGestao.Proibida, recusada.Falha);

        await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("empresa", "branch.padrao", "feature/{equipe}/{id}"), default));
        await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("time", "branch.padrao", "vendas/{id}", "Vendas"), default));
        await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("time", "branch.padrao", "fin/{id}", "financeiro"), default));

        var deVendas = await Conhecimento(c => c.ConvencoesAsync(ana, " VENDAS ", default));
        Assert.Equal(new[] { ("empresa", "feature/{equipe}/{id}", ""), ("time", "vendas/{id}", "vendas") },
            deVendas.Select(c => (c.Nivel!, c.Valor!, c.Time!)));
        Assert.All(deVendas, c => Assert.NotNull(c.AtualizadoEm)); // a auditoria: quando, e quem
        Assert.All(deVendas, c => Assert.False(string.IsNullOrWhiteSpace(c.Autor)));
        Assert.Single(await Conhecimento(c => c.ConvencoesAsync(ana, null, default))); // sem time, só a da empresa

        // Vazio apaga a do time: volta a valer a da empresa.
        await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("time", "branch.padrao", "", "vendas"), default));
        Assert.Single(await Conhecimento(c => c.ConvencoesAsync(ana, "vendas", default)));
    }

    /// <summary>
    /// A do USUÁRIO é dele: cada um grava a sua, o dev.kit lê só a própria, e a auditoria do gestor vê a de todos (a do
    /// dev, não a do colega).
    /// </summary>
    [Fact]
    public async Task A_convencao_do_usuario_e_dele_e_o_gestor_audita_todas()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var bia = await DevAsync(a, "bia@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);

        await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("usuario", "branch.padrao", "ana/{id}"), default));
        await Conhecimento(c => c.GravarConvencaoAsync(bia, new ConvencaoV1("usuario", "branch.padrao", "bia/{id}"), default));

        Assert.Equal(new[] { "ana/{id}" }, (await Conhecimento(c => c.ConvencoesAsync(ana, null, default))).Select(c => c.Valor!));
        Assert.Equal(new[] { "bia/{id}" }, (await Conhecimento(c => c.AuditoriaDeConvencoesAsync(bia, default))).Select(c => c.Valor!));
        var auditoria = await Conhecimento(c => c.AuditoriaDeConvencoesAsync(gestor, default));
        Assert.Equal(2, auditoria.Count);
        Assert.All(auditoria, c => Assert.Equal(c.Usuario, c.Autor));
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

    [Theory]
    [InlineData("RepositoriosDaEmpresa")]
    [InlineData("ConvencoesDaEmpresa")]
    public void Script_do_azure_sql_cria_cada_tabela_com_as_colunas_do_modelo(string tabela)
    {
        // Os scripts na ordem em que rodam: o Valor das convenções cresce no Us550.sql (o contexto, US #550); a Origem e
        // a Versao entram no Us663.sql (a regra do usuário × a sugestão do agente, US #663).
        var script = BaseDeTeste.ScriptDoAzureSql("Us491.sql") + BaseDeTeste.ScriptDoAzureSql("Us550.sql") + BaseDeTeste.ScriptDoAzureSql("Us663.sql");

        Assert.Contains($"OBJECT_ID(N'[dbo].[{tabela}]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer(tabela))
            Assert.Contains(coluna, script);
    }
}
