using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O que a empresa compartilha com o dev.kit (US #491) de ponta a ponta: o catálogo de repositórios e as convenções sob
/// <c>/api/empresa</c>, abertos ao DEV (e não só ao gestor), no escopo da empresa do token — sem token 401, o admin e a
/// outra empresa sem acesso, a convenção da empresa só do gestor e a do time de quem trabalha nele.
/// </summary>
public sealed class ConhecimentoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task O_dev_grava_e_consulta_o_catalogo_de_repositorios_da_empresa()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");

        var gravado = await ana.PostAsJsonAsync("/api/empresa/repositorios", new GravacaoDeRepositorioV1("https://github.com/acme/gbl-2000", "ERP de vendas (.NET 8)"));
        Assert.Equal(HttpStatusCode.OK, gravado.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/api/empresa/repositorios", new GravacaoDeRepositorioV1("https://github.com/acme/uau", ""))).StatusCode);

        var lista = (await ana.GetFromJsonAsync<RepositorioDaEmpresaV1[]>("/api/empresa/repositorios?filtro=vendas"))!;
        Assert.Equal(("https://github.com/acme/gbl-2000", "ERP de vendas (.NET 8)"), (Assert.Single(lista).Origem, lista[0].Descricao));

        // A outra empresa não vê; o admin (sem empresa no token) e o anônimo não entram.
        Assert.Empty((await caio.GetFromJsonAsync<RepositorioDaEmpresaV1[]>("/api/empresa/repositorios"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/empresa/repositorios")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/empresa/repositorios")).StatusCode);
    }

    [Fact]
    public async Task A_convencao_da_empresa_e_do_gestor_e_a_do_time_de_quem_trabalha_nele()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("empresa", "branch.padrao", "x/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await gestor.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("empresa", "branch.padrao", "feature/{equipe}/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("time", "branch.bug", "bug/{id}", "Vendas"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("usuario", "branch.padrao", "eu/{id}"))).StatusCode);

        var deVendas = (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes?time=vendas"))!;
        Assert.Equal(new[] { ("empresa", "branch.padrao"), ("time", "branch.bug") }, deVendas.Select(c => (c.Nivel!, c.Chave!)));
        Assert.Single((await gestor.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes"))!); // sem time, só a da empresa
    }
}
