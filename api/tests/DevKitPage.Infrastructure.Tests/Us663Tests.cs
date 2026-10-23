using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A US #663 na base de verdade: a migration que dá ORIGEM ao contexto sobre convenções que já existiam, a regra e a
/// sugestão do mesmo tema lado a lado, a promoção (troca a regra e remove a sugestão, com a versão conferida), o descarte
/// — e o script do Azure SQL.
/// </summary>
public sealed class Us663Tests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new(new() { ["Conta:UrlDoSite"] = "https://site.test" });

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Com<T>(Func<IServiceProvider, Task<T>> acao) => _base.ComAsync(acao);

    private Task<T> Conhecimento<T>(Func<IConhecimentoDaEmpresa, Task<T>> acao) => Com(sp => acao(sp.GetRequiredService<IConhecimentoDaEmpresa>()));

    private Task<EmpresaResumo> EmpresaAsync(string nome)
        => Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova(nome, "Empresarial", 5), default));

    private async Task<EscopoDaEmpresa> DevAsync(EmpresaResumo empresa, string email, bool gestor = false)
    {
        Assert.NotNull((await Com(sp => sp.GetRequiredService<IContas>().AderirAsync(new AdesaoV1(empresa.CodigoDeAdesao, email, "Dev"), default))).Valor);
        Assert.Empty(await Com(sp => sp.GetRequiredService<IContas>().DefinirSenhaAsync(_base.Emails.TokenPara(email), "senhaDoDev2026", default)));
        var id = await Com(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return new EscopoDaEmpresa(empresa.Id, id, gestor);
    }

    /// <summary>
    /// A migration só ACRESCENTA: as convenções gravadas antes dela (a base na Us602) ficam com os mesmos valores e
    /// autores, e passam a ser REGRA (Origem=usuario) na versão 1.
    /// </summary>
    [Fact]
    public async Task A_migration_mantem_as_convencoes_que_existiam_como_regra_na_versao_1()
    {
        var arquivo = Path.Combine(Path.GetTempPath(), $"devkitpage-us663-{Guid.NewGuid():N}.db");
        try
        {
            await using (var conexao = new SqliteConnection($"Data Source={arquivo}"))
            {
                await conexao.OpenAsync();
                await using var db = new DevKitPageDb(new DbContextOptionsBuilder<DevKitPageDb>().UseSqlite(conexao).Options);
                var migrador = db.GetService<IMigrator>();
                await migrador.MigrateAsync("20261018171735_Us602Relatos");

                await db.Database.ExecuteSqlRawAsync(
                    "PRAGMA foreign_keys = OFF; " +
                    "INSERT INTO ConvencoesDaEmpresa (EmpresaId, Nivel, Time, UsuarioId, Chave, Valor, AutorId, AtualizadoEmUtc) VALUES " +
                    "(1, 'empresa', '', 0, 'contexto.stack', '.NET 10', 7, '2026-10-01 10:00:00'), " +
                    "(1, 'time', 'vendas', 0, 'branch.bug', 'bug/{{id}}', 8, '2026-10-02 10:00:00');");

                await migrador.MigrateAsync();

                var linhas = await db.ConvencoesDaEmpresa.AsNoTracking().OrderBy(c => c.Chave).ToListAsync();
                Assert.Equal(new[] { ("branch.bug", "bug/{id}", 8), ("contexto.stack", ".NET 10", 7) }, linhas.Select(c => (c.Chave, c.Valor, c.AutorId)));
                Assert.All(linhas, c => Assert.Equal((OrigensDeConvencao.Usuario, 1), (c.Origem, c.Versao)));
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(arquivo); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task A_regra_e_a_sugestao_do_mesmo_tema_coexistem_e_a_rota_antiga_so_ve_a_regra()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);

        await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("empresa", "contexto.stack", ".NET 10"), default));
        var sugestao = (await Conhecimento(c => c.SugerirAsync(ana, new SugestaoDeContextoV1("empresa", "stack", "Use .NET 10 com xUnit."), default))).Valor!;

        Assert.Equal((OrigensDeConvencao.Agente, 1), (sugestao.Origem, sugestao.Versao));
        Assert.Equal(".NET 10", Assert.Single(await Conhecimento(c => c.ConvencoesAsync(ana, null, default))).Valor);
        var todas = await Conhecimento(c => c.ConvencoesAsync(ana, null, comSugestoes: true, default));
        Assert.Equal(new[] { (".NET 10", "usuario"), ("Use .NET 10 com xUnit.", "agente") }, todas.OrderBy(c => c.Origem == "agente").Select(c => (c.Valor!, c.Origem!)));

        // A mesma sugestão de novo (o reenvio da fila offline) não sobe a versão; outro texto a substitui.
        Assert.Equal(1, (await Conhecimento(c => c.SugerirAsync(ana, new SugestaoDeContextoV1("empresa", "stack", "Use .NET 10 com xUnit."), default))).Valor!.Versao);
        Assert.Equal(2, (await Conhecimento(c => c.SugerirAsync(ana, new SugestaoDeContextoV1("empresa", "stack", "Use .NET 10, xUnit e NSubstitute."), default))).Valor!.Versao);

        // Gravar a regra do tema não apaga a sugestão (e vice-versa).
        await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("empresa", "contexto.stack", ".NET 10 LTS"), default));
        Assert.Equal(2, (await Conhecimento(c => c.ConvencoesAsync(ana, null, comSugestoes: true, default))).Count);
    }

    [Fact]
    public async Task Promover_troca_a_regra_remove_a_sugestao_e_confere_a_versao()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);
        await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("time", "contexto.stack", "Angular 17", "Vendas"), default));
        var sugestao = (await Conhecimento(c => c.SugerirAsync(ana, new SugestaoDeContextoV1("time", "stack", "Angular 19 standalone", "Vendas"), default))).Valor!;

        // A Ana (dev) não grava a regra da equipe: não promove.
        Assert.Equal(FalhaNaGestao.Proibida, (await Conhecimento(c => c.PromoverAsync(ana, sugestao.Id!.Value, sugestao.Versao, default))).Falha);
        // A versão velha é conflito, e nada muda.
        Assert.Equal(FalhaNaGestao.Conflito, (await Conhecimento(c => c.PromoverAsync(gestor, sugestao.Id!.Value, sugestao.Versao + 1, default))).Falha);

        var regra = (await Conhecimento(c => c.PromoverAsync(gestor, sugestao.Id!.Value, sugestao.Versao, default))).Valor!;

        Assert.Equal(("Angular 19 standalone", "usuario", 2), (regra.Valor!, regra.Origem!, regra.Versao!.Value));
        var vendas = await Conhecimento(c => c.ConvencoesAsync(ana, "vendas", comSugestoes: true, default));
        Assert.Equal("Angular 19 standalone", Assert.Single(vendas).Valor);
        // A sugestão já promovida não existe mais.
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Conhecimento(c => c.PromoverAsync(gestor, sugestao.Id!.Value, null, default))).Falha);
    }

    [Fact]
    public async Task Descartar_tira_so_a_sugestao_e_e_de_quem_grava_a_camada_ou_do_autor()
    {
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        var ana = await DevAsync(a, "ana@a.com");
        var bia = await DevAsync(a, "bia@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);
        var gestorDeB = await DevAsync(b, "gestor@b.com", gestor: true);
        await Conhecimento(c => c.GravarConvencaoAsync(gestor, new ConvencaoV1("empresa", "contexto.revisao", "Checklist do time"), default));
        var daAna = (await Conhecimento(c => c.SugerirAsync(ana, new SugestaoDeContextoV1("empresa", "revisao", "Rodar o lint antes do PR"), default))).Valor!;

        Assert.Equal(FalhaNaGestao.Proibida, (await Conhecimento(c => c.DescartarAsync(bia, daAna.Id!.Value, default))).Falha);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Conhecimento(c => c.DescartarAsync(gestorDeB, daAna.Id!.Value, default))).Falha);
        Assert.NotNull((await Conhecimento(c => c.DescartarAsync(ana, daAna.Id!.Value, default))).Valor);

        // A regra do mesmo tema fica; a regra não se descarta pelo id (só a sugestão).
        var regra = Assert.Single(await Conhecimento(c => c.ConvencoesAsync(ana, null, comSugestoes: true, default)));
        Assert.Equal("Checklist do time", regra.Valor);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Conhecimento(c => c.DescartarAsync(gestor, regra.Id!.Value, default))).Falha);

        // A sugestão no nível usuário é do próprio: a do outro não se vê nem se descarta.
        var doUsuario = (await Conhecimento(c => c.SugerirAsync(ana, new SugestaoDeContextoV1("usuario", "estilo", "Respostas curtas"), default))).Valor!;
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Conhecimento(c => c.DescartarAsync(bia, doUsuario.Id!.Value, default))).Falha);
        Assert.DoesNotContain(await Conhecimento(c => c.ConvencoesAsync(bia, null, comSugestoes: true, default)), x => x.Nivel == "usuario");
    }

    [Fact]
    public void Script_do_azure_sql_acrescenta_a_origem_e_a_versao_e_troca_o_indice_de_forma_idempotente()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us663.sql");

        Assert.Contains("COL_LENGTH(N'[dbo].[ConvencoesDaEmpresa]', N'Origem') IS NULL", script);
        Assert.Contains("COL_LENGTH(N'[dbo].[ConvencoesDaEmpresa]', N'Versao') IS NULL", script);
        Assert.Contains("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave'", script);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ConvencoesDaEmpresa_EmpresaId_Nivel_Time_UsuarioId_Chave_Origem'", script);
        Assert.Contains("([EmpresaId], [Nivel], [Time], [UsuarioId], [Chave], [Origem])", script);
        Assert.DoesNotContain("DROP COLUMN", script); // nada é apagado
        Assert.DoesNotContain("DELETE", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("ConvencoesDaEmpresa").Where(c => c.StartsWith("[Origem]") || c.StartsWith("[Versao]")))
            Assert.Contains(coluna, script);
    }
}
