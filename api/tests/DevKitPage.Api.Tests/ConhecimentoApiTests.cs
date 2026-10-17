using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O que a empresa compartilha com o dev.kit (US #491) de ponta a ponta: o catálogo de repositórios sob
/// <c>/api/empresa</c>, aberto ao DEV (e não só ao gestor), no escopo da empresa do token — sem token 401, o admin e a
/// outra empresa sem acesso. E as rotas das convenções, que desde a US #556 são o ADAPTADOR do contexto para o dev.kit já
/// instalado: continuam respondendo <see cref="ConvencaoV1"/> (time = equipe, só as instruções), com as permissões do
/// contexto — a da empresa é do gestor, a da equipe de quem participa dela aprovado, a do usuário dele mesmo.
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

    /// <summary>
    /// O dev.kit antigo continua: a convenção da empresa é do gestor; a do time (a equipe) só depois de o gestor aprovar a
    /// participação que a leitura do dev.kit pediu; a do usuário é dele; vazio apaga.
    /// </summary>
    [Fact]
    public async Task As_rotas_das_convencoes_continuam_respondendo_como_adaptador_do_contexto()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("empresa", "branch.padrao", "x/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await gestor.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("empresa", "branch.padrao", "feature/{equipe}/{id}"))).StatusCode);

        // Sem a participação aprovada, a do time é recusada; a leitura do dev.kit pede, o gestor aprova, e aí grava.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("time", "branch.bug", "bug/{id}", "Vendas"))).StatusCode);
        await ApiDeTeste.AprovarNaEquipeAsync(gestor, ana, "ana@a.com", "Vendas");
        var gravada = await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("time", "branch.bug", "bug/{id}", "Vendas"));
        Assert.Equal(HttpStatusCode.OK, gravada.StatusCode);
        var convencao = (await gravada.Content.ReadFromJsonAsync<ConvencaoV1>())!;
        Assert.Equal(("time", "vendas"), (convencao.Nivel, convencao.Time));

        Assert.Equal(HttpStatusCode.OK,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("usuario", "branch.padrao", "eu/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("projeto", "branch.padrao", "x/{id}"))).StatusCode);

        // A nota de um agente (sugestão) não existe para o dev.kit antigo.
        (await ana.PutAsJsonAsync("/api/contexto", new GravacaoDeContextoV1("usuario", "branch.padrao", "sugestao/{id}", Origem: "agente"))).EnsureSuccessStatusCode();

        // O que o dev.kit lê: a da empresa, a do time declarado e a do próprio usuário.
        var deVendas = (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes?time=vendas"))!;
        Assert.Equal(new[] { ("empresa", "branch.padrao", "feature/{equipe}/{id}"), ("time", "branch.bug", "bug/{id}"), ("usuario", "branch.padrao", "eu/{id}") },
            deVendas.Select(c => (c.Nivel!, c.Chave!, c.Valor!)));
        Assert.Single((await gestor.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes"))!); // sem time e sem a sua, só a da empresa

        // Vazio apaga a do time: volta a valer a da empresa.
        Assert.Equal(HttpStatusCode.OK,
            (await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("time", "branch.bug", "", "vendas"))).StatusCode);
        Assert.Equal(2, (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes?time=vendas"))!.Length);
    }

    /// <summary>
    /// A auditoria antiga (<c>/convencoes/todas</c>) segue as permissões do contexto: o gestor vê a da empresa e as das
    /// equipes, com o autor e a data; o contexto pessoal de cada dev é só dele. E o catálogo: remove o gestor ou quem gravou.
    /// </summary>
    [Fact]
    public async Task A_auditoria_segue_a_visao_do_contexto_e_o_gestor_remove_do_catalogo()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        await ApiDeTeste.AprovarNaEquipeAsync(gestor, bia, "bia@a.com", "vendas");

        await ana.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("usuario", "branch.padrao", "ana/{id}"));
        await bia.PutAsJsonAsync("/api/empresa/convencoes", new ConvencaoV1("time", "branch.padrao", "vendas/{id}", "vendas"));
        await ana.PostAsJsonAsync("/api/empresa/repositorios", new GravacaoDeRepositorioV1("https://github.com/acme/gbl-2000", "ERP"));

        var doGestor = Assert.Single((await gestor.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes/todas"))!);
        Assert.Equal(("time", "Nome de bia@a.com"), (doGestor.Nivel, doGestor.Autor));
        Assert.NotNull(doGestor.AtualizadoEm);
        Assert.Equal(new[] { "usuario" }, (await ana.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes/todas"))!.Select(c => c.Nivel!));
        Assert.Equal(new[] { "time" }, (await bia.GetFromJsonAsync<ConvencaoV1[]>("/api/empresa/convencoes/todas"))!.Select(c => c.Nivel!));

        const string Rota = "/api/empresa/repositorios?origem=https%3A%2F%2Fgithub.com%2Facme%2Fgbl-2000";
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.DeleteAsync(Rota)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gestor.DeleteAsync(Rota)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestor.DeleteAsync(Rota)).StatusCode);
    }
}
