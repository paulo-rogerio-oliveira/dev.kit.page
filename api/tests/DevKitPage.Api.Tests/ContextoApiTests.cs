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
}
