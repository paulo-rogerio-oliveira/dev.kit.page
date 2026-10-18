using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A US #550 na base de verdade: o contexto (<c>contexto.&lt;tema&gt;</c>) com 4000 caracteres e várias linhas no
/// SQLite migrado, as permissões por papel no serviço (a equipe do gestor, o usuário do dono), o admin geral gravando o
/// contexto de um usuário da empresa (e não o de outra), as equipes conhecidas — e o script do Azure SQL.
/// </summary>
public sealed class Us550Tests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new(new() { ["Conta:UrlDoSite"] = "https://site.test" });

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Com<T>(Func<IServiceProvider, Task<T>> acao) => _base.ComAsync(acao);

    private Task<T> Conhecimento<T>(Func<IConhecimentoDaEmpresa, Task<T>> acao) => Com(sp => acao(sp.GetRequiredService<IConhecimentoDaEmpresa>()));

    private Task<EmpresaResumo> EmpresaAsync(string nome)
        => Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova(nome, "Empresarial", 5), default));

    /// <summary>Um dev que aderiu pelo código e definiu a senha pelo link; devolve o escopo dele (o login é o e-mail).</summary>
    private async Task<EscopoDaEmpresa> DevAsync(EmpresaResumo empresa, string email, bool gestor = false)
    {
        Assert.NotNull((await Com(sp => sp.GetRequiredService<IContas>().AderirAsync(new AdesaoV1(empresa.CodigoDeAdesao, email, "Dev"), default))).Valor);
        Assert.Empty(await Com(sp => sp.GetRequiredService<IContas>().DefinirSenhaAsync(_base.Emails.TokenPara(email), "senhaDoDev2026", default)));
        var id = await Com(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return new EscopoDaEmpresa(empresa.Id, id, gestor);
    }

    private Task<string> LoginAsync(int id) => Com(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Id == id).Select(u => u.Login).SingleAsync());

    [Fact]
    public async Task O_contexto_de_4000_caracteres_e_varias_linhas_grava_e_volta_inteiro()
    {
        var a = await EmpresaAsync("Empresa A");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);
        var texto = string.Join("\r\n", Enumerable.Repeat("- Revise com o checklist do time.", 200))[..4000].TrimEnd();

        var gravado = await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("empresa", "contexto.revisao", texto), default));

        Assert.Equal(texto, gravado.Valor!.Valor);
        Assert.Equal(texto, Assert.Single(await Conhecimento(c => c.ConvencoesAsync(gestor, null, default))).Valor);
    }

    /// <summary>O contexto da EQUIPE é do gestor (o branch do time continua de qualquer um); o do USUÁRIO, do dono.</summary>
    [Fact]
    public async Task O_contexto_da_equipe_e_do_gestor_e_o_do_usuario_do_dono()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var bia = await DevAsync(a, "bia@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);

        Assert.Equal(FalhaNaGestao.Proibida, (await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("time", "contexto.stack", "Angular 19", "Vendas"), default))).Falha);
        Assert.Equal(FalhaNaGestao.Proibida, (await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("empresa", "contexto.stack", ".NET 10"), default))).Falha);
        Assert.NotNull((await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("time", "branch.bug", "bug/{id}", "Vendas"), default))).Valor);
        Assert.NotNull((await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("time", "contexto.stack", "Angular 19", "Vendas"), default))).Valor);

        // O dev grava o próprio; o Usuario do corpo não muda o dono para quem não é admin.
        var daAna = await Conhecimento(c => c.GravarConvencaoAsync(ana, new ConvencaoV1("usuario", "contexto.estilo", "Respostas curtas", Usuario: "bia@a.com"), default));
        Assert.NotNull(daAna.Valor);
        Assert.Empty(await Conhecimento(c => c.ConvencoesAsync(bia, null, default)));
        Assert.Equal("Respostas curtas", Assert.Single(await Conhecimento(c => c.ConvencoesAsync(ana, null, default))).Valor);
    }

    [Fact]
    public async Task O_admin_grava_o_contexto_de_um_usuario_da_empresa_e_nao_o_de_outra()
    {
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        var ana = await DevAsync(a, "ana@a.com");
        var caio = await DevAsync(b, "caio@b.com");
        var admin = EscopoDaEmpresa.DoAdmin(a.Id, ana.UsuarioId); // o id do autor só precisa existir

        var gravado = await Conhecimento(c => c.GravarConvencaoAsync(admin, new ConvencaoV1("usuario", "contexto.estilo", "Explique o porquê", Usuario: "ana@a.com"), default));
        Assert.NotNull(gravado.Valor);
        Assert.Equal("Explique o porquê", Assert.Single(await Conhecimento(c => c.ConvencoesAsync(ana, null, default))).Valor);

        var loginDoCaio = await LoginAsync(caio.UsuarioId);
        var deOutra = await Conhecimento(c => c.GravarConvencaoAsync(admin, new ConvencaoV1("usuario", "contexto.estilo", "x", Usuario: loginDoCaio), default));
        Assert.Equal(FalhaNaGestao.NaoEncontrado, deOutra.Falha);
        Assert.Empty(await Conhecimento(c => c.ConvencoesAsync(caio, null, default)));

        // O admin vê na auditoria todos os níveis da empresa do caminho.
        await Conhecimento(c => c.GravarConvencaoAsync(admin, new ConvencaoV1("time", "contexto.stack", "Angular", "Vendas"), default));
        Assert.Equal(new[] { "time", "usuario" }, (await Conhecimento(c => c.AuditoriaDeConvencoesAsync(admin, default))).Select(c => c.Nivel!));
    }

    [Fact]
    public async Task As_equipes_conhecidas_sao_os_times_distintos_e_normalizados_da_empresa()
    {
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);
        var gestorDeB = await DevAsync(b, "gestor@b.com", gestor: true);

        await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("time", "contexto.stack", "x", " Vendas "), default));
        await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("time", "branch.bug", "bug/{id}", "VENDAS"), default));
        await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("time", "contexto.stack", "y", "Financeiro"), default));
        await Conhecimento(c => c.GravarConvencaoAsync(gestorDeB, new ConvencaoV1("time", "contexto.stack", "z", "Suporte"), default));

        Assert.Equal(new[] { "financeiro", "vendas" }, await Conhecimento(c => c.EquipesAsync(gestor, default)));
        Assert.Equal(new[] { "suporte" }, await Conhecimento(c => c.EquipesAsync(gestorDeB, default)));
    }

    [Fact]
    public void Script_do_azure_sql_alarga_o_valor_so_quando_a_coluna_e_menor()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us550.sql");

        Assert.Contains("ALTER COLUMN [Valor] nvarchar(4000) NOT NULL", script);
        Assert.Contains("COL_LENGTH(N'[dbo].[ConvencoesDaEmpresa]', N'Valor') < 8000", script); // idempotente
        Assert.Contains("[Valor] nvarchar(4000) NOT NULL", BaseDeTeste.ColunasNoSqlServer("ConvencoesDaEmpresa"));
    }
}
