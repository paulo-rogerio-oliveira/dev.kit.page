using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// Os relatos dos usuários do dev.kit (US #602) de ponta a ponta: o dev relata (o usuário e a empresa saem do token),
/// sem login é 401, o dev não entra na gestão (403), o gestor só vê e mexe nos da empresa dele (o de outra é 404), o
/// admin geral vê todos, muda o estado e responde, o autor lê a resposta em <c>/meus</c>, o limite por usuário dá 429,
/// a captura acima do teto é recusada e o segredo é mascarado de novo no servidor.
/// </summary>
public sealed class RelatosApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];

    private static async Task<RelatoRecebidoV1> RelatarAsync(HttpClient cliente, string texto, string categoria = CategoriasDeRelato.Defeito, string? chave = null)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/relatos", new RelatoNovoV1(categoria, texto, "{\"versao\":\"208\"}", Convert.ToBase64String(Png), "208", chave));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Content.ReadFromJsonAsync<RelatoRecebidoV1>())!;
    }

    [Fact]
    public async Task Sem_login_e_401_e_o_dev_nao_entra_na_gestao()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var anonimo = _api.CreateClient();
        var relato = await RelatarAsync(ana, "A PR foi aberta no repositório errado.");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/relatos", new RelatoNovoV1("defeito", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/relatos/meus")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/relatos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/relatos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync($"/api/relatos/{relato.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PatchAsJsonAsync($"/api/relatos/{relato.Id}/estado", new AlterarEstadoDoRelatoV1("resolvido"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsJsonAsync($"/api/relatos/{relato.Id}/resposta", new RespostaAoRelatoV1("eu mesma"))).StatusCode);
    }

    [Fact]
    public async Task O_gestor_so_ve_e_administra_os_relatos_da_empresa_dele()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        var gestorB = await _api.GestorAsync(admin, b.Id, "gestor.b");
        var deA = await RelatarAsync(ana, "relato da empresa A");
        var deB = await RelatarAsync(caio, "relato da empresa B", CategoriasDeRelato.Sugestao);

        var doGestor = (await gestorB.GetFromJsonAsync<Pagina<RelatoResumoV1>>($"/api/relatos?empresa={a.Id}"))!;
        Assert.Equal(deB.Id, Assert.Single(doGestor.Itens).Id); // o filtro de empresa não tira o gestor da dele
        Assert.Equal(b.Id, doGestor.Itens[0].EmpresaId);
        Assert.Equal(HttpStatusCode.NotFound, (await gestorB.GetAsync($"/api/relatos/{deA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestorB.PatchAsJsonAsync($"/api/relatos/{deA.Id}/estado", new AlterarEstadoDoRelatoV1("resolvido"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestorB.PostAsJsonAsync($"/api/relatos/{deA.Id}/resposta", new RespostaAoRelatoV1("não é meu"))).StatusCode);

        var respondido = await gestorB.PostAsJsonAsync($"/api/relatos/{deB.Id}/resposta", new RespostaAoRelatoV1("Vamos avaliar."));
        Assert.Equal(HttpStatusCode.OK, respondido.StatusCode);
        Assert.Equal(EstadosDeRelato.EmAnalise, (await respondido.Content.ReadFromJsonAsync<RelatoDetalheV1>())!.Estado);
        Assert.Equal(EstadosDeRelato.Novo, (await admin.GetFromJsonAsync<RelatoDetalheV1>($"/api/relatos/{deA.Id}"))!.Estado);
    }

    [Fact]
    public async Task O_admin_ve_todos_filtra_muda_o_estado_responde_e_o_autor_le_em_meus()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        var deA = await RelatarAsync(ana, "A PR da task multirepo foi criada no repositório errado.", chave: "fila-1");
        await RelatarAsync(caio, "Como relato pelo devcli?", CategoriasDeRelato.Duvida);

        var todos = (await admin.GetFromJsonAsync<Pagina<RelatoResumoV1>>("/api/relatos"))!;
        Assert.Equal(2, todos.Total);
        Assert.True(todos.Itens.All(i => i.TemCaptura));
        Assert.Equal(deA.Id, Assert.Single((await admin.GetFromJsonAsync<Pagina<RelatoResumoV1>>($"/api/relatos?empresa={a.Id}&categoria=defeito&estado=novo"))!.Itens).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/relatos?estado=fechado")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/relatos/{deA.Id}/estado", new AlterarEstadoDoRelatoV1("fechado"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/relatos/{deA.Id}/resposta", new RespostaAoRelatoV1(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/relatos/999999")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/relatos/{deA.Id}/resposta", new RespostaAoRelatoV1("Corrigido na versão 209."))).StatusCode);
        var resolvido = await admin.PatchAsJsonAsync($"/api/relatos/{deA.Id}/estado", new AlterarEstadoDoRelatoV1("resolvido"));
        Assert.Equal(EstadosDeRelato.Resolvido, (await resolvido.Content.ReadFromJsonAsync<RelatoDetalheV1>())!.Estado);

        var meus = (await ana.GetFromJsonAsync<MeuRelatoV1[]>("/api/relatos/meus"))!;
        var meu = Assert.Single(meus);
        Assert.Equal((EstadosDeRelato.Resolvido, "Corrigido na versão 209.", "fila-1"), (meu.Estado, meu.Resposta, meu.ChaveDoCliente));
        Assert.DoesNotContain(meus, m => m.Texto.Contains("devcli", StringComparison.Ordinal));
    }

    [Fact]
    public async Task O_usuario_e_a_empresa_vem_do_token_e_o_reenvio_da_fila_nao_duplica()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");

        var primeiro = await RelatarAsync(ana, "relato", chave: "mesma-chave");
        var reenvio = await RelatarAsync(ana, "relato", chave: "mesma-chave");

        Assert.Equal(primeiro.Id, reenvio.Id);
        var detalhe = (await admin.GetFromJsonAsync<RelatoDetalheV1>($"/api/relatos/{primeiro.Id}"))!;
        Assert.Equal((a.Id, "Empresa A", "Nome de ana@a.com"), (detalhe.EmpresaId, detalhe.Empresa, detalhe.Autor));
        Assert.Single((await admin.GetFromJsonAsync<Pagina<RelatoResumoV1>>("/api/relatos"))!.Itens);
    }

    [Fact]
    public async Task O_segredo_que_escapou_do_dev_kit_e_mascarado_no_servidor()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");

        var resposta = await ana.PostAsJsonAsync("/api/relatos", new RelatoNovoV1(
            "defeito", "Falhou com o PAT ghp_1234567890abcdefghijABCDEFGHIJ1234 e password=Sup3r!",
            "{\"erro\":\"Server=db;Database=x;User Id=sa;Password=Sup3r!;\"}"));
        var id = (await resposta.Content.ReadFromJsonAsync<RelatoRecebidoV1>())!.Id;

        var detalhe = (await admin.GetFromJsonAsync<RelatoDetalheV1>($"/api/relatos/{id}"))!;
        Assert.DoesNotContain("ghp_1234567890", detalhe.Texto);
        Assert.DoesNotContain("Sup3r!", detalhe.Texto + detalhe.Contexto);
        Assert.Contains("<connection-string>", detalhe.Contexto);
        Assert.Null(detalhe.Captura);
    }

    [Fact]
    public async Task A_captura_acima_do_teto_e_o_relato_invalido_sao_recusados()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var grande = new byte[RelatosV1.TamanhoMaximoDaCaptura + 1];
        Png.CopyTo(grande, 0);

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/api/relatos", new RelatoNovoV1("defeito", "x", Captura: Convert.ToBase64String(grande)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/api/relatos", new RelatoNovoV1("elogio", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync("/api/relatos", new RelatoNovoV1("defeito", ""))).StatusCode);
        Assert.Empty((await ana.GetFromJsonAsync<MeuRelatoV1[]>("/api/relatos/meus"))!);
    }

    [Fact]
    public async Task O_limite_de_envios_por_usuario_da_429_com_retry_after()
    {
        using var api = new ApiDeTeste(relatosPorHora: 2);
        var admin = await api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");

        await RelatarAsync(ana, "1");
        await RelatarAsync(ana, "2");
        var terceiro = await ana.PostAsJsonAsync("/api/relatos", new RelatoNovoV1("defeito", "3"));

        Assert.Equal(HttpStatusCode.TooManyRequests, terceiro.StatusCode);
        Assert.True(terceiro.Headers.Contains("Retry-After"));
        await RelatarAsync(bia, "a cota é por usuário"); // a colega não divide a cota
    }
}
