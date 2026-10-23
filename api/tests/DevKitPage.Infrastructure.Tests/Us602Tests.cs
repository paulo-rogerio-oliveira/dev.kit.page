using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A US #602 na base de verdade: o relato gravado mascarado e ligado ao usuário e à empresa, o reenvio da mesma chave
/// sem duplicar, a lista com filtros e paginação no escopo (o gestor só vê a empresa dele, e o detalhe de outra é
/// nulo), a troca de estado, a resposta (o novo passa a em análise), os meus relatos só do autor — e o script do
/// Azure SQL.
/// </summary>
public sealed class Us602Tests : IAsyncLifetime
{
    /// <summary>Um PNG mínimo: a assinatura de 8 bytes e um pouco de corpo.</summary>
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private readonly BaseDeTeste _base = new(new() { ["Conta:UrlDoSite"] = "https://site.test" });

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Com<T>(Func<IServiceProvider, Task<T>> acao) => _base.ComAsync(acao);

    private Task<T> Relatos<T>(Func<IRelatos, Task<T>> acao) => Com(sp => acao(sp.GetRequiredService<IRelatos>()));

    private Task<EmpresaResumo> EmpresaAsync(string nome)
        => Com(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova(nome, "Empresarial", 5), default));

    /// <summary>Um dev que aderiu pelo código; devolve o id.</summary>
    private async Task<int> DevAsync(EmpresaResumo empresa, string email)
    {
        Assert.NotNull((await Com(sp => sp.GetRequiredService<IContas>().AderirAsync(new AdesaoV1(empresa.CodigoDeAdesao, email, "Dev " + email), default))).Valor);
        return await Com(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
    }

    private Task<int> AdminIdAsync() => Com(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.EhAdmin).Select(u => u.Id).SingleAsync());

    private Task<RelatoRecebidoV1> RelatarAsync(int usuario, int? empresa, string texto, string categoria = CategoriasDeRelato.Defeito, string? chave = null, string? contexto = null, byte[]? captura = null)
        => Relatos(r => r.RegistrarAsync(usuario, empresa, new RelatoNovoV1(categoria, texto, contexto, null, "208", chave), captura, default));

    [Fact]
    public async Task O_relato_e_gravado_novo_mascarado_e_ligado_ao_usuario_e_a_empresa()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");

        var recebido = await RelatarAsync(ana, a.Id, "Falhou com password=Sup3r! no clone", contexto: "{\"conexao\":\"Server=db;User Id=sa;Password=x;\",\"versao\":\"208\"}",
            captura: Png);

        Assert.Equal(EstadosDeRelato.Novo, recebido.Estado);
        var detalhe = (await Relatos(r => r.DetalheAsync(EscopoDoPainel.Tudo, recebido.Id, default)))!;
        Assert.DoesNotContain("Sup3r!", detalhe.Texto);
        Assert.DoesNotContain("Password=x", detalhe.Contexto);
        Assert.Contains("\"versao\":\"208\"", detalhe.Contexto);
        Assert.Equal(("Dev ana@a.com", a.Id, "Empresa A", "208"), (detalhe.Autor, detalhe.EmpresaId, detalhe.Empresa, detalhe.VersaoDevKit));
        Assert.Equal(Png, Convert.FromBase64String(detalhe.Captura!));
    }

    [Fact]
    public async Task O_reenvio_da_mesma_chave_devolve_o_mesmo_relato_e_sem_chave_grava_outro()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var bia = await DevAsync(a, "bia@a.com");

        var primeiro = await RelatarAsync(ana, a.Id, "PR no repositório errado", chave: "chave-1");
        var reenvio = await RelatarAsync(ana, a.Id, "PR no repositório errado", chave: "chave-1");
        var deOutro = await RelatarAsync(bia, a.Id, "PR no repositório errado", chave: "chave-1");
        await RelatarAsync(ana, a.Id, "sem chave");
        await RelatarAsync(ana, a.Id, "sem chave");

        Assert.Equal(primeiro.Id, reenvio.Id);
        Assert.NotEqual(primeiro.Id, deOutro.Id);
        Assert.Equal(5 - 1, await Com(sp => sp.GetRequiredService<DevKitPageDb>().Relatos.CountAsync()));
    }

    [Fact]
    public async Task A_lista_filtra_pagina_e_o_gestor_so_ve_a_empresa_dele()
    {
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        var ana = await DevAsync(a, "ana@a.com");
        var caio = await DevAsync(b, "caio@b.com");
        var deA = await RelatarAsync(ana, a.Id, "defeito de A");
        await RelatarAsync(ana, a.Id, "sugestão de A", CategoriasDeRelato.Sugestao);
        _base.Relogio.Agora = _base.Relogio.Agora.AddDays(2);
        var deB = await RelatarAsync(caio, b.Id, "dúvida de B", CategoriasDeRelato.Duvida);
        var gestorA = new EscopoDoPainel(a.Id);

        var tudo = await Relatos(r => r.ListarAsync(EscopoDoPainel.Tudo, new FiltroDeRelatos(), default));
        Assert.Equal(3, tudo.Total);
        Assert.Equal(deB.Id, tudo.Itens[0].Id); // o mais novo primeiro
        Assert.Equal(["defeito de A", "sugestão de A"], (await Relatos(r => r.ListarAsync(gestorA, new FiltroDeRelatos(), default))).Itens.Select(i => i.Resumo).Order());

        // O filtro de empresa do gestor não sai da empresa dele; o do admin, sim.
        Assert.Equal(2, (await Relatos(r => r.ListarAsync(gestorA, new FiltroDeRelatos(EmpresaId: b.Id), default))).Total);
        Assert.Equal(deB.Id, Assert.Single((await Relatos(r => r.ListarAsync(EscopoDoPainel.Tudo, new FiltroDeRelatos(EmpresaId: b.Id), default))).Itens).Id);
        Assert.Equal(deA.Id, Assert.Single((await Relatos(r => r.ListarAsync(gestorA, new FiltroDeRelatos(Categoria: "Defeito"), default))).Itens).Id);
        var hoje = DateOnly.FromDateTime(_base.Relogio.Agora.UtcDateTime);
        Assert.Equal(deB.Id, Assert.Single((await Relatos(r => r.ListarAsync(EscopoDoPainel.Tudo, new FiltroDeRelatos(De: hoje, Ate: hoje), default))).Itens).Id);
        var pagina2 = await Relatos(r => r.ListarAsync(EscopoDoPainel.Tudo, new FiltroDeRelatos(Pagina: 2, Tamanho: 2), default));
        Assert.Equal((3, 2, 1), (pagina2.Total, pagina2.NumeroDaPagina, pagina2.Itens.Count));

        // O relato de outra empresa não existe para o gestor.
        Assert.Null(await Relatos(r => r.DetalheAsync(gestorA, deB.Id, default)));
        Assert.Null(await Relatos(r => r.AlterarEstadoAsync(gestorA, deB.Id, EstadosDeRelato.Resolvido, default)));
        Assert.Null(await Relatos(r => r.ResponderAsync(gestorA, deB.Id, ana, "x", default)));
        Assert.Equal(EstadosDeRelato.Novo, (await Relatos(r => r.DetalheAsync(EscopoDoPainel.Tudo, deB.Id, default)))!.Estado);
    }

    [Fact]
    public async Task A_resposta_leva_o_novo_a_em_analise_e_o_estado_muda_ate_resolvido()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var admin = await AdminIdAsync();
        var relato = await RelatarAsync(ana, a.Id, "PR no repositório errado");

        var respondido = (await Relatos(r => r.ResponderAsync(EscopoDoPainel.Tudo, relato.Id, admin, "  Corrigido na 209.  ", default)))!;
        Assert.Equal((EstadosDeRelato.EmAnalise, "Corrigido na 209.", "admin"), (respondido.Estado, respondido.Resposta, respondido.RespondidoPor));
        Assert.NotNull(respondido.RespondidoEm);

        var resolvido = (await Relatos(r => r.AlterarEstadoAsync(new EscopoDoPainel(a.Id), relato.Id, "Resolvido", default)))!;
        Assert.Equal(EstadosDeRelato.Resolvido, resolvido.Estado);
        Assert.True(Assert.Single((await Relatos(r => r.ListarAsync(EscopoDoPainel.Tudo, new FiltroDeRelatos(Estado: "resolvido"), default))).Itens).Respondido);
    }

    [Fact]
    public async Task Os_meus_relatos_sao_so_do_autor_com_o_estado_e_a_resposta()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var bia = await DevAsync(a, "bia@a.com");
        var admin = await AdminIdAsync();
        var daAna = await RelatarAsync(ana, a.Id, "da Ana", chave: "k-ana");
        await RelatarAsync(bia, a.Id, "da Bia");
        await Relatos(r => r.ResponderAsync(EscopoDoPainel.Tudo, daAna.Id, admin, "Resolvido na 209", default));

        var meus = await Relatos(r => r.MeusAsync(ana, default));

        var meu = Assert.Single(meus);
        Assert.Equal(("da Ana", EstadosDeRelato.EmAnalise, "Resolvido na 209", "k-ana"), (meu.Texto, meu.Estado, meu.Resposta, meu.ChaveDoCliente));
        Assert.Equal("da Bia", Assert.Single(await Relatos(r => r.MeusAsync(bia, default))).Texto);
    }

    [Fact]
    public void Script_do_azure_sql_cria_a_tabela_dos_relatos_com_as_colunas_do_modelo()
    {
        var script = BaseDeTeste.ScriptDoAzureSql("Us602.sql");

        Assert.Contains("OBJECT_ID(N'[dbo].[Relatos]', N'U') IS NULL", script);
        foreach (var coluna in BaseDeTeste.ColunasNoSqlServer("Relatos"))
            Assert.Contains(coluna, script);
        Assert.Contains("CREATE UNIQUE INDEX [IX_Relatos_UsuarioId_ChaveDoCliente]", script);
    }
}
