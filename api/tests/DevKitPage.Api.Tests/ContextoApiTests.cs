using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O contexto por nível (US #550) de ponta a ponta: o dev grava o próprio, o gestor grava o da empresa e o das equipes
/// (o dev não), o admin geral gerencia todos os níveis pelas rotas <c>/api/empresas/{id}/...</c> com o isolamento entre
/// empresas — e a rota que o dev.kit lê (<c>GET /api/empresa/convencoes?time=</c>) devolve os três níveis no contrato de
/// sempre.
/// </summary>
public sealed class ContextoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task O_dev_grava_o_proprio_e_o_gestor_o_da_empresa_e_das_equipes()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");

        // O dev: o próprio contexto sim; o da empresa e o da equipe, não.
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("usuario", "contexto.estilo", "Respostas curtas.\nSempre com testes."))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("empresa", "contexto.stack", ".NET 10"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("time", "contexto.stack", "Angular", "Vendas"))).StatusCode);

        // O gestor: empresa e equipe; o texto longo cabe até 4000.
        Assert.Equal(HttpStatusCode.OK, (await gestor.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("empresa", "contexto.stack", new string('x', 4000)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await gestor.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("empresa", "contexto.stack", new string('x', 4001)))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gestor.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("time", "contexto.stack", "Angular 19", "Vendas"))).StatusCode);
        Assert.Equal(new[] { "vendas" }, (await gestor.GetFromJsonAsync<string[]>("/api/empresa/equipes"))!);

        // A rota dos agentes: empresa + a equipe declarada + o próprio usuário, no ConvencaoV1 de sempre.
        var doDevKit = (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes?time=Vendas"))!;
        Assert.Equal(new[] { ("empresa", "contexto.stack"), ("time", "contexto.stack"), ("usuario", "contexto.estilo") },
            doDevKit.Select(c => (c.Nivel!, c.Chave!)));
        Assert.Equal("Respostas curtas.\nSempre com testes.", doDevKit.Single(c => c.Nivel == "usuario").Valor);

        // O gestor vê o do usuário na auditoria, mas não o edita (grava o próprio, não o da Ana).
        Assert.Contains((await gestor.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes/todas"))!, c => c.Nivel == "usuario");
        await gestor.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("usuario", "contexto.estilo", "do gestor", Usuario: "ana@a.com"));
        Assert.Equal("Respostas curtas.\nSempre com testes.", (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes"))!.Single(c => c.Nivel == "usuario").Valor);
    }

    [Fact]
    public async Task O_admin_gerencia_todos_os_niveis_de_uma_empresa_com_isolamento()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");

        var rota = $"/api/empresas/{a.Id}/convencoes";
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(rota, new ConvencaoV1("empresa", "contexto.stack", ".NET 10"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(rota, new ConvencaoV1("time", "contexto.stack", "Angular", "Vendas"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(rota, new ConvencaoV1("usuario", "contexto.estilo", "Do admin", Usuario: "ana@a.com"))).StatusCode);

        // Login de outra empresa e empresa inexistente: 404.
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync(rota, new ConvencaoV1("usuario", "contexto.estilo", "x", Usuario: "caio@b.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/empresas/9999/convencoes/todas")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync("/api/empresas/9999/convencoes", new ConvencaoV1("empresa", "contexto.stack", "x"))).StatusCode);

        var todas = (await admin.GetFromJsonAsync<ConvencaoV1[]>($"/api/empresas/{a.Id}/convencoes/todas"))!;
        Assert.Equal(new[] { "empresa", "time", "usuario" }, todas.Select(c => c.Nivel!));
        Assert.Empty((await admin.GetFromJsonAsync<ConvencaoV1[]>($"/api/empresas/{b.Id}/convencoes/todas"))!);
        Assert.Equal(new[] { "vendas" }, (await admin.GetFromJsonAsync<string[]>($"/api/empresas/{a.Id}/equipes"))!);

        // O que o admin gravou é o que o dev.kit da Ana lê.
        Assert.Equal("Do admin", (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes?time=vendas"))!.Single(c => c.Nivel == "usuario").Valor);

        // Não-admin nas rotas do admin: 403; o admin continua fora de /api/empresa/* (403).
        Assert.Equal(HttpStatusCode.Forbidden, (await gestor.GetAsync($"/api/empresas/{a.Id}/convencoes/todas")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PutAsJsonAsync(rota, new ConvencaoV1("empresa", "contexto.stack", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/empresa/equipes")).StatusCode);
    }

    /// <summary>
    /// US #663 — a sugestão do agente (com o token do dev) é SEMPRE origem agente, fica ao lado da regra e não aparece na
    /// rota antiga (o dev.kit instalado leria como regra); com <c>?origem=todas</c> vêm as duas, com id, origem e versão.
    /// </summary>
    [Fact]
    public async Task A_sugestao_do_agente_fica_ao_lado_da_regra_e_a_rota_antiga_continua_so_com_regras()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        await gestor.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("empresa", "contexto.stack", ".NET 10"));

        // O corpo não escolhe a origem: a rota de sugestão grava agente; a de gravação, regra.
        var sugerida = await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("empresa", "stack", "Use .NET 10 com xUnit."));
        Assert.Equal(HttpStatusCode.OK, sugerida.StatusCode);
        Assert.Equal(OrigensDeConvencao.Agente, (await sugerida.Content.ReadFromJsonAsync<ConvencaoV1>())!.Origem);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("time", "testes", "Teste por runner.", "Vendas"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("usuario", "estilo", "Respostas curtas."))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("empresa", "branch.bug", "bug/{id}"))).StatusCode);

        var antiga = (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes?time=Vendas"))!;
        Assert.Equal(".NET 10", Assert.Single(antiga).Valor);

        var todas = (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes?time=Vendas&origem=todas"))!;
        Assert.Equal(4, todas.Length);
        Assert.Equal(3, todas.Count(c => c.Origem == OrigensDeConvencao.Agente));
        Assert.All(todas, c => Assert.True(c.Id > 0 && c.Versao == 1));
        Assert.Equal("Nome de ana@a.com", todas.First(c => c.Origem == OrigensDeConvencao.Agente).Autor);
    }

    /// <summary>US #663 — promover: 403 sem o papel da camada, 409 com a versão velha, 200 e a regra nova na rota antiga.</summary>
    [Fact]
    public async Task Promover_e_descartar_respeitam_o_papel_e_a_versao()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");

        var daEmpresa = (await (await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("empresa", "stack", "Angular 19"))).Content.ReadFromJsonAsync<ConvencaoV1>())!;
        var rota = $"/api/empresa/contexto/{daEmpresa.Id}";

        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync($"{rota}/promover", new PromocaoDeContextoV1(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caio.PostAsJsonAsync($"{rota}/promover", new PromocaoDeContextoV1(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await gestor.PostAsJsonAsync($"{rota}/promover", new PromocaoDeContextoV1(7))).StatusCode);
        var promovida = await gestor.PostAsJsonAsync($"{rota}/promover", new PromocaoDeContextoV1(1));
        Assert.Equal(HttpStatusCode.OK, promovida.StatusCode);
        Assert.Equal(OrigensDeConvencao.Usuario, (await promovida.Content.ReadFromJsonAsync<ConvencaoV1>())!.Origem);
        Assert.Equal("Angular 19", Assert.Single((await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes"))!).Valor);

        // Descartar: a Bia não descarta a sugestão da Ana na empresa; a Ana (autora) sim.
        var outra = (await (await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("empresa", "revisao", "Lint antes do PR"))).Content.ReadFromJsonAsync<ConvencaoV1>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.DeleteAsync($"/api/empresa/contexto/{outra.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync($"/api/empresa/contexto/{outra.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.DeleteAsync($"/api/empresa/contexto/{outra.Id}")).StatusCode);
    }

    /// <summary>US #663 — as mesmas ações do admin geral em /api/empresas/{id}/contexto/*; não-admin recebe 403.</summary>
    [Fact]
    public async Task O_admin_promove_e_descarta_pelas_rotas_dele()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var sugestao = (await (await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("time", "stack", "Angular", "Vendas"))).Content.ReadFromJsonAsync<ConvencaoV1>())!;
        var descartavel = (await (await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("empresa", "stack", ".NET"))).Content.ReadFromJsonAsync<ConvencaoV1>())!;

        Assert.Equal(HttpStatusCode.Forbidden, (await gestor.PostAsJsonAsync($"/api/empresas/{a.Id}/contexto/{sugestao.Id}/promover", new PromocaoDeContextoV1(1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/empresas/9999/contexto/{sugestao.Id}/promover", new PromocaoDeContextoV1(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/empresas/{a.Id}/contexto/{sugestao.Id}/promover", new PromocaoDeContextoV1(1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync($"/api/empresas/{a.Id}/contexto/{descartavel.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/empresas/{a.Id}/contexto/sugestoes", new SugestaoDeContextoV1("empresa", "stack", "Do admin"))).StatusCode);

        var todas = (await admin.GetFromJsonAsync<ConvencaoV1[]>($"/api/empresas/{a.Id}/convencoes/todas"))!;
        Assert.Equal(new[] { ("empresa", "agente"), ("time", "usuario") }, todas.Select(c => (c.Nivel!, c.Origem!)).OrderBy(x => x));
    }

    /// <summary>US #663 — segredo no contexto (regra ou sugestão) é 422, e o texto não volta na resposta.</summary>
    [Fact]
    public async Task Segredo_no_contexto_e_422_sem_ecoar_o_valor()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        const string pat = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";

        var sugestao = await ana.PostAsJsonAsync("/api/empresa/contexto/sugestoes", new SugestaoDeContextoV1("usuario", "acesso", $"Clone com {pat}"));
        var regra = await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("usuario", "contexto.acesso", $"password={pat}"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, sugestao.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, regra.StatusCode);
        Assert.DoesNotContain(pat, await sugestao.Content.ReadAsStringAsync());
        Assert.DoesNotContain(pat, await regra.Content.ReadAsStringAsync());
        Assert.Empty((await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes?origem=todas"))!);
        // O branch não é texto livre: a regra dele não passa pela barreira.
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("usuario", "branch.feature", "feature/{equipe}/{id}"))).StatusCode);
    }
}
