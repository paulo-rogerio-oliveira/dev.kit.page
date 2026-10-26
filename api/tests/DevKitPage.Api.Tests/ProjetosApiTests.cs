using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// Os projetos compartilhados (US #693) de ponta a ponta, sob <c>/api/projetos</c>: a criação e a substituição com a
/// versão lida, a visibilidade por escopo (usuário × equipe × empresa × outra empresa, e quem não tem empresa), quem
/// apaga, a validação do plano e a rota sem login.
/// </summary>
public sealed class ProjetosApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static ItemDoProjetoV1 Item(int numero, params int[] dependencias)
        => new(numero, $"Item {numero}", "o que fazer", "planejar-e-executar", ["https://github.com/acme/portal"], ["Core/Data"],
            dependencias, 1, 0, ProjetosV1.ItemPlanejado);

    private static EnvioDeProjetoV1 Projeto(string slug, string? nivel = null, string? time = null, int? versao = null,
        IReadOnlyList<ItemDoProjetoV1>? itens = null)
        => new(slug, $"Projeto {slug}", nivel, time, ["https://github.com/acme/portal", "https://github.com/acme/api"],
            $"feature/dev/projeto-{slug}", false, "O que o dev quer.", ["executando v004"], true, 3, "feature/dev/semana5",
            ProjetosV1.AguardandoAprovacao, versao, itens ?? [Item(1), Item(2, 1)]);

    private async Task<(HttpClient Ana, HttpClient Bia, HttpClient Gestor, HttpClient Caio)> EmpresasAsync()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        return (ana, bia, gestor, caio);
    }

    private static async Task<ProjetoV1[]> ListarAsync(HttpClient cliente, string consulta = "")
        => (await cliente.GetFromJsonAsync<ProjetoV1[]>("/api/projetos" + consulta))!;

    [Fact]
    public async Task O_projeto_criado_volta_com_o_plano_e_a_versao_lida_substitui_e_a_velha_e_conflito()
    {
        var (ana, bia, _, _) = await EmpresasAsync();

        var criacao = await ana.PutAsJsonAsync("/api/projetos", Projeto("orquestrador"));
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        var criado = (await criacao.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(("empresa", 1, 2, "feature/dev/projeto-orquestrador"), (criado.Nivel, criado.Versao, criado.Itens.Count, criado.Branch));
        Assert.Equal([1], criado.Itens.Single(i => i.Numero == 2).Dependencias);
        Assert.Equal($"/api/projetos/{criado.Id}", criacao.Headers.Location!.ToString());

        // Bia, da mesma empresa, lê e regrava com a versão lida; Ana, com a velha, recebe conflito.
        var lido = Assert.Single(await ListarAsync(bia));
        var regravacao = await bia.PutAsJsonAsync("/api/projetos", Projeto("orquestrador", versao: lido.Versao, itens: [Item(1)]));
        Assert.Equal(HttpStatusCode.OK, regravacao.StatusCode);
        Assert.Equal(2, (await regravacao.Content.ReadFromJsonAsync<ProjetoV1>())!.Versao);

        var velha = await ana.PutAsJsonAsync("/api/projetos", Projeto("orquestrador", versao: 1));
        Assert.Equal(HttpStatusCode.Conflict, velha.StatusCode);

        // Criar de novo o mesmo slug, sem versão, também é conflito: o dev.kit relê antes.
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PutAsJsonAsync("/api/projetos", Projeto("orquestrador"))).StatusCode);
    }

    [Fact]
    public async Task A_visibilidade_segue_o_escopo_usuario_equipe_empresa_e_outra_empresa()
    {
        var (ana, bia, gestor, caio) = await EmpresasAsync();
        await ana.PutAsJsonAsync("/api/projetos", Projeto("da-empresa", nivel: "empresa"));
        await ana.PutAsJsonAsync("/api/projetos", Projeto("de-vendas", nivel: "time", time: "Vendas"));
        await ana.PutAsJsonAsync("/api/projetos", Projeto("meu", nivel: "usuario"));

        Assert.Equal(["da-empresa", "de-vendas", "meu"], (await ListarAsync(ana)).Select(p => p.Slug).Order());
        Assert.Equal(["da-empresa", "de-vendas"], (await ListarAsync(bia)).Select(p => p.Slug).Order());
        Assert.Equal(["da-empresa"], (await ListarAsync(bia, "?time=Suporte")).Select(p => p.Slug));
        Assert.Equal(["da-empresa", "de-vendas"], (await ListarAsync(gestor)).Select(p => p.Slug).Order());
        Assert.Empty(await ListarAsync(caio));

        // O projeto de outra empresa é 404, sem revelar que existe.
        var daEmpresa = (await ListarAsync(bia)).Single(p => p.Slug == "da-empresa");
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync($"/api/projetos/{daEmpresa.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bia.GetAsync($"/api/projetos/{daEmpresa.Id}")).StatusCode);
    }

    [Fact]
    public async Task Projeto_de_time_sem_time_e_recusado_e_quem_nao_tem_empresa_guarda_no_nivel_usuario()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (solo, _) = await _api.UsuarioAsync(admin, "solo", Papeis.Dev);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/api/projetos", Projeto("sem-time", nivel: "time"))).StatusCode);

        // Pediu empresa, mas não tem: fica no nível usuário, e ninguém da empresa o vê.
        var criado = await solo.PutAsJsonAsync("/api/projetos", Projeto("sozinho", nivel: "empresa"));
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        Assert.Equal("usuario", (await criado.Content.ReadFromJsonAsync<ProjetoV1>())!.Nivel);
        Assert.Empty(await ListarAsync(ana));
    }

    [Fact]
    public async Task So_o_autor_ou_o_gestor_apagam_o_projeto()
    {
        var (ana, bia, gestor, _) = await EmpresasAsync();
        var a = (await (await ana.PutAsJsonAsync("/api/projetos", Projeto("um"))).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var b = (await (await ana.PutAsJsonAsync("/api/projetos", Projeto("dois"))).Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.False((await ListarAsync(bia)).Single(p => p.Id == a.Id).PodeApagar);

        Assert.Equal(HttpStatusCode.Forbidden, (await bia.DeleteAsync($"/api/projetos/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync($"/api/projetos/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gestor.DeleteAsync($"/api/projetos/{b.Id}")).StatusCode);
        Assert.Empty(await ListarAsync(ana));
    }

    [Fact]
    public async Task O_plano_com_ciclo_ou_slug_invalido_e_recusado_e_sem_login_a_rota_nao_responde()
    {
        var (ana, _, _, _) = await EmpresasAsync();

        var ciclo = await ana.PutAsJsonAsync("/api/projetos", Projeto("ciclo", itens: [Item(1, 2), Item(2, 1)]));
        Assert.Equal(HttpStatusCode.BadRequest, ciclo.StatusCode);
        Assert.Contains("ciclo", await ciclo.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/api/projetos", Projeto("Com Espaco"))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/projetos")).StatusCode);
    }
}
