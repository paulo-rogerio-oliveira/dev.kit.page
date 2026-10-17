using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O contexto do dev.kit (US #556) de ponta a ponta, por papel: o dev vê o dele, o das equipes aprovadas e o da empresa;
/// o gestor edita o da empresa e decide quem participa de cada equipe; o admin do board vê e altera tudo pelas rotas dele;
/// o agente (com o token do usuário) só grava sugestão. E as recusas: 401, 403, 404 (outra empresa), 409 (versão velha) e
/// 422 (credencial, sem ecoar o valor nem no log).
/// </summary>
public sealed class ContextoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private sealed record Cenario(HttpClient Admin, EmpresaResumo A, HttpClient Ana, HttpClient Bia, HttpClient Gestor);

    private async Task<Cenario> CenarioAsync()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        return new Cenario(admin, a, ana, bia, gestor);
    }

    private static async Task<ContextoV1> GravarAsync(HttpClient cliente, GravacaoDeContextoV1 gravacao)
    {
        var resposta = await cliente.PutAsJsonAsync("/api/contexto", gravacao);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<ContextoV1>())!;
    }

    [Fact]
    public async Task Cada_papel_ve_o_que_lhe_cabe()
    {
        var (_, _, ana, bia, gestor) = await CenarioAsync();
        await ApiDeTeste.AprovarNaEquipeAsync(gestor, ana, "ana@a.com", "vendas");

        await GravarAsync(gestor, new GravacaoDeContextoV1("empresa", "contexto.stack", ".NET 10 e React 19"));
        await GravarAsync(gestor, new GravacaoDeContextoV1("equipe", "contexto.fin", "Contábil", "financeiro"));
        await GravarAsync(ana, new GravacaoDeContextoV1("equipe", "contexto.vendas", "ERP de vendas", "vendas"));
        await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.ana", "testes pequenos"));
        await GravarAsync(bia, new GravacaoDeContextoV1("usuario", "contexto.bia", "da bia"));

        var daAna = (await ana.GetFromJsonAsync<VisaoDoContextoV1>("/api/contexto/visao"))!;
        Assert.Equal(new[] { "contexto.stack", "contexto.vendas", "contexto.ana" }, daAna.Itens.Select(i => i.Chave));
        Assert.False(daAna.EditaEmpresa);

        // O dev não grava o da empresa nem lê o de uma equipe alheia.
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync("/api/contexto", new GravacaoDeContextoV1("empresa", "contexto.x", "y"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/contexto/equipes/financeiro")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/contexto/equipes/vendas")).StatusCode);

        var doGestor = (await gestor.GetFromJsonAsync<VisaoDoContextoV1>("/api/contexto/visao"))!;
        Assert.True(doGestor.EditaEmpresa);
        Assert.Equal(new[] { "contexto.stack", "contexto.fin", "contexto.vendas" }, doGestor.Itens.Select(i => i.Chave));

        // O que o dev.kit compõe: empresa + equipe + usuário.
        var composto = (await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto?equipe=Vendas"))!;
        Assert.Equal(("vendas", "aprovado"), (composto.Equipe, composto.Participacao));
        Assert.Equal(new[] { "empresa", "equipe", "usuario" }, composto.Itens.Select(i => i.Nivel));
    }

    [Fact]
    public async Task Sem_token_401_e_o_admin_usa_as_rotas_dele()
    {
        var (admin, a, ana, _, gestor) = await CenarioAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/contexto")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/contexto")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync($"/api/admin/empresas/{a.Id}/contexto")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await gestor.GetAsync($"/api/admin/empresas/{a.Id}/contexto")).StatusCode);
    }

    [Fact]
    public async Task O_admin_do_board_ve_e_altera_tudo_de_qualquer_empresa()
    {
        var (admin, a, ana, _, gestor) = await CenarioAsync();
        var anaId = await ApiDeTeste.AprovarNaEquipeAsync(gestor, ana, "ana@a.com", "vendas");
        var pessoal = await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.ana", "testes pequenos"));
        var rota = $"/api/admin/empresas/{a.Id}/contexto";

        var visao = (await admin.GetFromJsonAsync<VisaoDoContextoV1>(rota))!;
        Assert.Equal((Papeis.Admin, true), (visao.Papel, visao.EditaEmpresa));
        Assert.Equal("contexto.ana", Assert.Single(visao.Itens).Chave); // até o contexto pessoal do dev
        Assert.Single(visao.Participantes);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/admin/empresas/9999/contexto")).StatusCode);

        // Grava em todos os níveis, no usuário dizendo de quem; exclui qualquer item; decide a participação.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(rota, new GravacaoDeContextoV1("empresa", "contexto.stack", ".NET 10"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(rota, new GravacaoDeContextoV1("equipe", "contexto.fin", "Contábil", "financeiro"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(rota, new GravacaoDeContextoV1("usuario", "contexto.ana", "revisado", UsuarioId: anaId, VersaoEsperada: 1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/admin/empresas/9999/contexto", new GravacaoDeContextoV1("empresa", "contexto.x", "y"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"{rota}/{pessoal.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PutAsJsonAsync($"{rota}/equipes/vendas/participantes", new DecisaoDeParticipacaoV1(anaId, "recusado"))).StatusCode);

        Assert.Equal(new[] { "contexto.stack" }, (await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto"))!.Itens.Select(i => i.Chave));
    }

    /// <summary>O agente usa o token do usuário, grava só SUGESTÃO — e só onde o usuário grava.</summary>
    [Fact]
    public async Task O_agente_so_grava_sugestao_ao_lado_da_instrucao()
    {
        var (_, _, ana, _, gestor) = await CenarioAsync();
        await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.build", "dotnet build do projeto"));
        var nota = await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.build", "compile só o projeto alterado", Origem: "agente"));
        Assert.Equal(OrigensDeContexto.Agente, nota.Origem);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/contexto", new GravacaoDeContextoV1("empresa", "nota.x", "y", Origem: "agente"))).StatusCode);

        var itens = (await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto"))!.Itens;
        Assert.Equal(new[] { ("agente", "compile só o projeto alterado"), ("usuario", "dotnet build do projeto") }, itens.Select(i => (i.Origem, i.Valor)));

        // Promover é ação do usuário: a sugestão vira a instrução.
        var promovida = await ana.PostAsync($"/api/contexto/{nota.Id}/promover", null);
        Assert.Equal(HttpStatusCode.OK, promovida.StatusCode);
        Assert.Equal(("usuario", "compile só o projeto alterado"),
            ((await promovida.Content.ReadFromJsonAsync<ContextoV1>())!.Origem, (await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto"))!.Itens.Single().Valor));
    }

    [Fact]
    public async Task A_edicao_sobre_a_versao_velha_e_409_com_o_item_atual()
    {
        var (_, _, ana, _, _) = await CenarioAsync();
        await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.testes", "xUnit"));
        await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.testes", "xUnit e Vitest", VersaoEsperada: 1));

        var conflito = await ana.PutAsJsonAsync("/api/contexto", new GravacaoDeContextoV1("usuario", "contexto.testes", "só xUnit", VersaoEsperada: 1));
        Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
        var corpo = (await conflito.Content.ReadFromJsonAsync<ConflitoDeContextoV1>())!;
        Assert.Equal(("xUnit e Vitest", 2), (corpo.Atual!.Valor, corpo.Atual.Versao));
    }

    /// <summary>A equipe declarada por quem não participa vira pedido PENDENTE, sem acesso, até o gestor aprovar.</summary>
    [Fact]
    public async Task A_equipe_alheia_declarada_fica_pendente_e_proibida_ate_o_gestor_aprovar()
    {
        var (admin, _, ana, _, gestor) = await CenarioAsync();
        await GravarAsync(gestor, new GravacaoDeContextoV1("equipe", "contexto.revisao", "dois revisores", "financeiro"));

        var composto = (await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto?equipe=financeiro"))!;
        Assert.Equal(("pendente", 0), (composto.Participacao, composto.Itens.Count));
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/contexto/equipes/financeiro")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/contexto", new GravacaoDeContextoV1("equipe", "contexto.x", "y", "financeiro"))).StatusCode);
        Assert.Equal(new[] { "financeiro" }, (await ana.GetFromJsonAsync<VisaoDoContextoV1>("/api/contexto/visao"))!.EquipesPendentes);

        // O dev não se aprova; o gestor de OUTRA empresa não encontra o dev (404).
        var pedido = (await gestor.GetFromJsonAsync<VisaoDoContextoV1>("/api/contexto/visao"))!.Participantes.Single();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/contexto/equipes/financeiro/participantes", new DecisaoDeParticipacaoV1(pedido.UsuarioId, "aprovado"))).StatusCode);
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var gestorDeB = await _api.GestorAsync(admin, b.Id, "gestor.b");
        Assert.Equal(HttpStatusCode.NotFound,
            (await gestorDeB.PutAsJsonAsync("/api/contexto/equipes/financeiro/participantes", new DecisaoDeParticipacaoV1(pedido.UsuarioId, "aprovado"))).StatusCode);

        Assert.Equal(HttpStatusCode.OK,
            (await gestor.PutAsJsonAsync("/api/contexto/equipes/financeiro/participantes", new DecisaoDeParticipacaoV1(pedido.UsuarioId, "aprovado"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/contexto/equipes/financeiro")).StatusCode);
        Assert.Single((await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto?equipe=financeiro"))!.Itens);
    }

    [Theory]
    [InlineData("ghp_1234567890abcdefghijABCDEFGHIJ")]
    [InlineData("password=Sup3rSecreta")]
    [InlineData("Server=db;Database=erp;User Id=sa;Pwd=Abc12345;")]
    public async Task Credencial_e_422_sem_o_valor_na_resposta_nem_no_log(string segredo)
    {
        var (_, _, ana, _, _) = await CenarioAsync();

        var resposta = await ana.PutAsJsonAsync("/api/contexto", new GravacaoDeContextoV1("usuario", "contexto.acesso", $"use {segredo} no build"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.DoesNotContain(segredo, await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.DoesNotContain(_api.Logs.Mensagens, m => m.Contains(segredo, StringComparison.Ordinal));

        // Nem a nota do agente, nem a rota antiga das convenções.
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await ana.PutAsJsonAsync("/api/contexto", new GravacaoDeContextoV1("usuario", "nota.acesso", segredo, Origem: "agente"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("usuario", "branch.padrao", segredo))).StatusCode);
        Assert.Empty((await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto"))!.Itens);
    }

    [Fact]
    public async Task Excluir_o_dono_exclui_o_colega_nao_e_a_outra_empresa_nao_encontra()
    {
        var (admin, _, ana, bia, _) = await CenarioAsync();
        var item = await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.ana", "testes pequenos"));
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await bia.DeleteAsync($"/api/contexto/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caio.DeleteAsync($"/api/contexto/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync($"/api/contexto/{item.Id}")).StatusCode);
        Assert.Empty((await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto"))!.Itens);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.DeleteAsync($"/api/contexto/{item.Id}")).StatusCode);
    }

    /// <summary>Descartar a sugestão do agente é excluí-la: a instrução da mesma chave continua lá.</summary>
    [Fact]
    public async Task Descartar_a_sugestao_deixa_a_instrucao_da_mesma_chave()
    {
        var (_, _, ana, _, _) = await CenarioAsync();
        await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.build", "dotnet build do projeto"));
        var nota = await GravarAsync(ana, new GravacaoDeContextoV1("usuario", "contexto.build", "rode a solução inteira", Origem: "agente"));

        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync($"/api/contexto/{nota.Id}")).StatusCode);
        var restou = Assert.Single((await ana.GetFromJsonAsync<ContextoCompostoV1>("/api/contexto"))!.Itens);
        Assert.Equal(("usuario", "dotnet build do projeto"), (restou.Origem, restou.Valor));
    }
}
