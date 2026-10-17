using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O contexto (US #556) na base de verdade: o CRUD com a versão (o 409 traz o item atual), a instrução e a sugestão da
/// mesma chave lado a lado, promover e descartar, a participação na equipe (o dev pede ao sincronizar, o gestor aprova),
/// a visão de cada papel, o admin do board pelo mesmo serviço e o isolamento entre empresas.
/// </summary>
public sealed class ContextoDaEmpresaTests : IAsyncLifetime
{
    private readonly BaseDeTeste _base = new(new() { ["Conta:UrlDoSite"] = "https://site.test" });

    public Task InitializeAsync() => _base.InicializarAsync();

    public async Task DisposeAsync() => await _base.DisposeAsync();

    private Task<T> Contexto<T>(Func<IContextoDaEmpresa, Task<T>> acao) => _base.ComAsync(sp => acao(sp.GetRequiredService<IContextoDaEmpresa>()));

    private Task<EmpresaResumo> EmpresaAsync(string nome)
        => _base.ComAsync(sp => sp.GetRequiredService<IEmpresas>().CriarAsync(new EmpresaNova(nome, "Empresarial", 5), default));

    /// <summary>Um dev que aderiu pelo código e definiu a senha; devolve o escopo do contexto dele.</summary>
    private async Task<EscopoDoContexto> DevAsync(EmpresaResumo empresa, string email, bool gestor = false)
    {
        Assert.NotNull((await _base.ComAsync(sp => sp.GetRequiredService<IContas>().AderirAsync(new AdesaoV1(empresa.CodigoDeAdesao, email, $"Nome {email}"), default))).Valor);
        Assert.Empty(await _base.ComAsync(sp => sp.GetRequiredService<IContas>().DefinirSenhaAsync(_base.Emails.TokenPara(email), "senhaDoDev2026", default)));
        var id = await _base.ComAsync(sp => sp.GetRequiredService<DevKitPageDb>().Usuarios.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return EscopoDoContexto.DoToken(new EscopoDaEmpresa(empresa.Id, id, gestor));
    }

    private Task<ResultadoDoContexto> GravarAsync(EscopoDoContexto escopo, string nivel, string chave, string valor, string equipe = "", string origem = "", int? versao = null)
        => Contexto(c => c.GravarAsync(escopo, new GravacaoDeContextoV1(nivel, chave, valor, equipe, origem, versao), default));

    private Task Aprovar(EscopoDoContexto gestor, EscopoDoContexto dev, string equipe)
        => Contexto(c => c.DecidirParticipacaoAsync(gestor, equipe, new DecisaoDeParticipacaoV1(dev.UsuarioId, SituacoesDeParticipacao.Aprovado), default));

    [Fact]
    public async Task Cria_le_altera_com_a_versao_recusa_a_versao_velha_e_exclui()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");

        var criado = (await GravarAsync(ana, "usuario", "contexto.testes", "xUnit")).Valor!;
        Assert.Equal((1, "usuario", "xUnit"), (criado.Versao, criado.Origem, criado.Valor));

        var alterado = (await GravarAsync(ana, "usuario", "contexto.testes", "xUnit e Vitest", versao: 1)).Valor!;
        Assert.Equal((criado.Id, 2), (alterado.Id, alterado.Versao));

        // A edição feita sobre a versão 1 chegou depois: conflito, com o item atual — nada se sobrescreve em silêncio.
        var velho = await GravarAsync(ana, "usuario", "contexto.testes", "só xUnit", versao: 1);
        Assert.Equal(FalhaNaGestao.Conflito, velho.Falha);
        Assert.Equal(("xUnit e Vitest", 2), (velho.Atual!.Valor, velho.Atual.Versao));

        Assert.Equal("xUnit e Vitest", Assert.Single((await Contexto(c => c.ComporAsync(ana, null, default))).Itens).Valor);
        Assert.NotNull((await Contexto(c => c.ExcluirAsync(ana, criado.Id, default))).Valor);
        Assert.Empty((await Contexto(c => c.ComporAsync(ana, null, default))).Itens);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Contexto(c => c.ExcluirAsync(ana, criado.Id, default))).Falha);

        // Editar o que foi excluído depois da leitura também é conflito (sem item atual).
        var excluido = await GravarAsync(ana, "usuario", "contexto.testes", "de novo", versao: 2);
        Assert.Equal((FalhaNaGestao.Conflito, (ContextoV1?)null), (excluido.Falha, excluido.Atual));
    }

    [Fact]
    public async Task Instrucao_e_sugestao_da_mesma_chave_coexistem_e_a_sugestao_se_promove_ou_se_descarta()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");

        await GravarAsync(ana, "usuario", "contexto.build", "dotnet build do projeto");
        var sugestao = (await GravarAsync(ana, "usuario", "contexto.build", "compile só o projeto alterado", origem: "agente")).Valor!;
        var outra = (await GravarAsync(ana, "usuario", "nota.encoding", "os .frm são Windows-1252", origem: "agente")).Valor!;

        var itens = (await Contexto(c => c.ComporAsync(ana, null, default))).Itens;
        Assert.Equal(new[] { ("contexto.build", "agente"), ("contexto.build", "usuario"), ("nota.encoding", "agente") },
            itens.Select(i => (i.Chave, i.Origem)));

        // Promover: a instrução recebe o texto da sugestão, e a sugestão sai.
        var promovida = (await Contexto(c => c.PromoverAsync(ana, sugestao.Id, default))).Valor!;
        Assert.Equal(("usuario", "compile só o projeto alterado", 2), (promovida.Origem, promovida.Valor, promovida.Versao));
        Assert.Equal(FalhaNaGestao.Recusada, (await Contexto(c => c.PromoverAsync(ana, promovida.Id, default))).Falha);

        // Descartar é excluir a sugestão: a instrução da mesma chave continua lá.
        Assert.NotNull((await Contexto(c => c.ExcluirAsync(ana, outra.Id, default))).Valor);
        Assert.Equal(new[] { ("contexto.build", "usuario") }, (await Contexto(c => c.ComporAsync(ana, null, default))).Itens.Select(i => (i.Chave, i.Origem)));
    }

    /// <summary>
    /// O dev só PEDE: a equipe declarada na sincronização vira pendente, sem acesso; o gestor aprova e o contexto da equipe
    /// passa a vir; recusar tira de novo.
    /// </summary>
    [Fact]
    public async Task A_equipe_declarada_vira_pedido_pendente_ate_o_gestor_aprovar()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);
        await GravarAsync(gestor, "equipe", "contexto.revisao", "PR com dois revisores", "Vendas");

        var antes = await Contexto(c => c.ComporAsync(ana, " VENDAS ", default));
        Assert.Equal(("vendas", SituacoesDeParticipacao.Pendente), (antes.Equipe, antes.Participacao));
        Assert.Empty(antes.Itens);
        Assert.Equal(FalhaNaGestao.Proibida, (await Contexto(c => c.DaEquipeAsync(ana, "vendas", default))).Falha);
        Assert.Equal(FalhaNaGestao.Proibida, (await GravarAsync(ana, "equipe", "contexto.x", "y", "vendas")).Falha);

        var visaoDoGestor = (await Contexto(c => c.VisaoAsync(gestor, default)))!;
        var pedido = Assert.Single(visaoDoGestor.Participantes);
        Assert.Equal((ana.UsuarioId, "vendas", SituacoesDeParticipacao.Pendente), (pedido.UsuarioId, pedido.Equipe, pedido.Situacao));
        Assert.Equal(new[] { "vendas" }, (await Contexto(c => c.VisaoAsync(ana, default)))!.EquipesPendentes);

        // O dev não decide; o gestor aprova.
        Assert.Equal(FalhaNaGestao.Proibida,
            (await Contexto(c => c.DecidirParticipacaoAsync(ana, "vendas", new DecisaoDeParticipacaoV1(ana.UsuarioId, "aprovado"), default))).Falha);
        await Aprovar(gestor, ana, "Vendas");

        var depois = await Contexto(c => c.ComporAsync(ana, "vendas", default));
        Assert.Equal(SituacoesDeParticipacao.Aprovado, depois.Participacao);
        Assert.Equal("PR com dois revisores", Assert.Single(depois.Itens).Valor);
        Assert.NotNull((await GravarAsync(ana, "equipe", "contexto.x", "y", "vendas")).Valor);

        await Contexto(c => c.DecidirParticipacaoAsync(gestor, "vendas", new DecisaoDeParticipacaoV1(ana.UsuarioId, "recusado"), default));
        Assert.Equal(FalhaNaGestao.Proibida, (await Contexto(c => c.DaEquipeAsync(ana, "vendas", default))).Falha);
    }

    [Fact]
    public async Task A_visao_de_cada_papel()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var bia = await DevAsync(a, "bia@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);
        await Aprovar(gestor, ana, "vendas");

        await GravarAsync(gestor, "empresa", "contexto.stack", ".NET 10");
        await GravarAsync(gestor, "equipe", "contexto.vendas", "ERP", "vendas");
        await GravarAsync(gestor, "equipe", "contexto.fin", "Contábil", "financeiro");
        await GravarAsync(ana, "usuario", "contexto.ana", "prefiro testes pequenos");
        await GravarAsync(bia, "usuario", "contexto.bia", "da bia");

        var daAna = (await Contexto(c => c.VisaoAsync(ana, default)))!;
        Assert.Equal((Papeis.Dev, false), (daAna.Papel, daAna.EditaEmpresa));
        Assert.Equal(new[] { "contexto.stack", "contexto.vendas", "contexto.ana" }, daAna.Itens.Select(i => i.Chave));
        Assert.Equal(new[] { "vendas" }, daAna.Equipes);
        Assert.Empty(daAna.Participantes);

        var doGestor = (await Contexto(c => c.VisaoAsync(gestor, default)))!;
        Assert.Equal((Papeis.Gestor, true), (doGestor.Papel, doGestor.EditaEmpresa));
        Assert.Equal(new[] { "contexto.stack", "contexto.fin", "contexto.vendas" }, doGestor.Itens.Select(i => i.Chave));
        Assert.Equal(new[] { "financeiro", "vendas" }, doGestor.Equipes);
        Assert.Single(doGestor.Participantes);

        var doAdmin = (await Contexto(c => c.VisaoAsync(EscopoDoContexto.DoAdmin(a.Id, 1), default)))!;
        Assert.Equal(5, doAdmin.Itens.Count); // tudo, inclusive o contexto pessoal de cada dev
        Assert.Null(await Contexto(c => c.VisaoAsync(EscopoDoContexto.DoAdmin(9999, 1), default)));
    }

    [Fact]
    public async Task O_admin_do_board_grava_em_qualquer_empresa_pelo_mesmo_servico()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var admin = EscopoDoContexto.DoAdmin(a.Id, 1);

        var doAdmin = (await Contexto(c => c.GravarAsync(admin, new GravacaoDeContextoV1("usuario", "contexto.ana", "revisado pelo admin", UsuarioId: ana.UsuarioId), default))).Valor!;
        Assert.Equal("Nome ana@a.com", doAdmin.Usuario);
        Assert.Equal("revisado pelo admin", Assert.Single((await Contexto(c => c.ComporAsync(ana, null, default))).Itens).Valor);
        Assert.NotNull((await GravarAsync(admin, "equipe", "contexto.x", "y", "qualquer")).Valor);

        Assert.Equal(FalhaNaGestao.Recusada, (await GravarAsync(admin, "usuario", "contexto.y", "sem dono")).Falha);
        Assert.Equal(FalhaNaGestao.NaoEncontrado,
            (await Contexto(c => c.GravarAsync(EscopoDoContexto.DoAdmin(9999, 1), new GravacaoDeContextoV1("empresa", "contexto.x", "y"), default))).Falha);
    }

    [Fact]
    public async Task Outra_empresa_nao_ve_nao_exclui_e_nao_aprova()
    {
        var a = await EmpresaAsync("Empresa A");
        var b = await EmpresaAsync("Empresa B");
        var gestorDeA = await DevAsync(a, "gestor@a.com", gestor: true);
        var ana = await DevAsync(a, "ana@a.com");
        var gestorDeB = await DevAsync(b, "gestor@b.com", gestor: true);
        var item = (await GravarAsync(gestorDeA, "empresa", "contexto.stack", ".NET 10")).Valor!;

        Assert.Empty((await Contexto(c => c.ComporAsync(gestorDeB, null, default))).Itens);
        Assert.Equal(FalhaNaGestao.NaoEncontrado, (await Contexto(c => c.ExcluirAsync(gestorDeB, item.Id, default))).Falha);
        Assert.Equal(FalhaNaGestao.NaoEncontrado,
            (await Contexto(c => c.DecidirParticipacaoAsync(gestorDeB, "vendas", new DecisaoDeParticipacaoV1(ana.UsuarioId, "aprovado"), default))).Falha);
    }

    [Fact]
    public async Task O_dev_nao_exclui_o_da_empresa_e_o_servico_tambem_barra_a_credencial()
    {
        var a = await EmpresaAsync("Empresa A");
        var ana = await DevAsync(a, "ana@a.com");
        var gestor = await DevAsync(a, "gestor@a.com", gestor: true);
        var item = (await GravarAsync(gestor, "empresa", "contexto.stack", ".NET 10")).Valor!;

        Assert.Equal(FalhaNaGestao.Proibida, (await Contexto(c => c.ExcluirAsync(ana, item.Id, default))).Falha);
        Assert.Equal(FalhaNaGestao.Recusada, (await GravarAsync(ana, "usuario", "contexto.pat", "ghp_1234567890abcdefghijABCDEFGHIJ")).Falha);
        Assert.DoesNotContain((await Contexto(c => c.ComporAsync(ana, null, default))).Itens, i => i.Nivel == "usuario");
    }
}
