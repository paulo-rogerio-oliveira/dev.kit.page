using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O design compartilhado (US #669) de ponta a ponta, sob <c>/api/design</c>: o envio multipart (PNG + metadados), a
/// visibilidade por escopo (usuário × equipe × empresa × outra empresa, e quem não tem empresa), o link estável da
/// imagem, a idempotência do reenvio e o conflito, quem apaga, o teto do plano e a sincronização incremental.
/// </summary>
public sealed class DesignApiTests : IDisposable
{
    private const string Repo = "https://github.com/acme/portal";

    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    /// <summary>Um PNG mínimo (a assinatura e o IHDR) com as dimensões pedidas e uma "semente" que muda o hash.</summary>
    public static byte[] Png(int largura = 600, int altura = 400, byte semente = 1)
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        png.AddRange(BitConverter.GetBytes(largura).Reverse());
        png.AddRange(BitConverter.GetBytes(altura).Reverse());
        png.AddRange([8, 6, 0, 0, 0, 0, 0, 0, 0, semente]);
        return png.ToArray();
    }

    /// <summary>O multipart do envio: os metadados como campos e o PNG em <c>imagem</c>.</summary>
    public static MultipartFormDataContent Envio(string tela, byte[] png, int? numero = null, string tipo = "captura", int? basis = null,
        string? nivel = null, string? time = null, string repositorio = Repo, string branch = "feature/dev/semana5")
    {
        var corpo = new MultipartFormDataContent
        {
            { new StringContent(repositorio), "repositorio" },
            { new StringContent(tela), "tela" },
            { new StringContent($"Tela {tela}"), "nome" },
            { new StringContent(tipo), "tipo" },
            { new StringContent(tipo == "proposta" ? "proposta" : "web"), "origem" },
            { new StringContent("http://localhost/" + tela), "alvo" },
            { new StringContent("light"), "tema" },
            { new StringContent(branch), "branch" },
            { new StringContent("3f9c2a1"), "commit" },
            { new StringContent("agente"), "autorTipo" },
            { new StringContent("claude"), "runner" },
        };
        if (numero is { } n) corpo.Add(new StringContent(n.ToString(System.Globalization.CultureInfo.InvariantCulture)), "numero");
        if (basis is { } b) corpo.Add(new StringContent(b.ToString(System.Globalization.CultureInfo.InvariantCulture)), "base");
        if (nivel is not null) corpo.Add(new StringContent(nivel), "nivel");
        if (time is not null) corpo.Add(new StringContent(time), "time");
        var imagem = new ByteArrayContent(png);
        imagem.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        corpo.Add(imagem, "imagem", $"{tela}.png");
        return corpo;
    }

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

    private static async Task<ProjetoDeDesignV1[]> ProjetosAsync(HttpClient cliente, string consulta = "")
        => (await cliente.GetFromJsonAsync<ProjetoDeDesignV1[]>("/api/design/projetos" + consulta))!;

    [Fact]
    public async Task A_versao_enviada_fica_no_projeto_do_repositorio_com_o_branch_como_atributo_e_o_link_estavel()
    {
        var (ana, bia, _, _) = await EmpresasAsync();
        var png = Png(1440, 900);

        var resposta = await ana.PostAsync("/api/design/versoes", Envio("checkout", png, numero: 1));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var enviada = (await resposta.Content.ReadFromJsonAsync<VersaoEnviadaV1>())!;
        Assert.True(enviada.Nova);
        Assert.Equal((1440, 900, "feature/dev/semana5", "3f9c2a1", "agente"), (enviada.Versao.Largura, enviada.Versao.Altura, enviada.Versao.Branch, enviada.Versao.Commit, enviada.Versao.AutorTipo));
        Assert.Equal($"/api/design/{enviada.Projeto}/checkout/v1", enviada.Versao.Link);
        Assert.Equal(resposta.Headers.Location!.ToString(), enviada.Versao.Link);

        // Outro branch do MESMO repositório (escrito de outro jeito) cai no mesmo projeto: o branch é atributo.
        var outroBranch = await ana.PostAsync("/api/design/versoes", Envio("checkout", Png(semente: 2), tipo: "proposta", basis: 1,
            repositorio: "git@github.com:Acme/Portal.git", branch: "feature/dev/semana4"));
        Assert.Equal(HttpStatusCode.Created, outroBranch.StatusCode);
        var projeto = Assert.Single(await ProjetosAsync(bia));
        Assert.Equal(("github.com/acme/portal", "empresa"), (projeto.Chave, projeto.Nivel));
        Assert.Equal(new[] { ("feature/dev/semana5", 1), ("feature/dev/semana4", 2) },
            Assert.Single(projeto.Telas).Versoes.Select(v => (v.Branch, v.Numero)));

        // O link estável devolve o PNG para quem vê o projeto.
        var imagem = await bia.GetAsync(enviada.Versao.Link);
        Assert.Equal(HttpStatusCode.OK, imagem.StatusCode);
        Assert.Equal("image/png", imagem.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await imagem.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task A_visibilidade_segue_o_escopo_do_contexto_usuario_equipe_empresa_e_outra_empresa()
    {
        var (ana, bia, gestor, caio) = await EmpresasAsync();
        await ana.PostAsync("/api/design/versoes", Envio("empresa", Png(semente: 1), nivel: "empresa"));
        await ana.PostAsync("/api/design/versoes", Envio("vendas", Png(semente: 2), nivel: "time", time: "Vendas"));
        await ana.PostAsync("/api/design/versoes", Envio("minha", Png(semente: 3), nivel: "usuario"));

        static string[] Niveis(ProjetoDeDesignV1[] projetos) => projetos.Select(p => p.Nivel).Order().ToArray();

        Assert.Equal(new[] { "empresa", "time", "usuario" }, Niveis(await ProjetosAsync(ana)));
        // A colega vê o da empresa e o do time (sem time declarado, todos — o site); o da Ana, não.
        Assert.Equal(new[] { "empresa", "time" }, Niveis(await ProjetosAsync(bia)));
        Assert.Equal(new[] { "empresa", "time" }, Niveis(await ProjetosAsync(bia, "?time=vendas")));
        Assert.Equal(new[] { "empresa" }, Niveis(await ProjetosAsync(bia, "?time=suporte")));
        Assert.Equal(new[] { "empresa", "time" }, Niveis(await ProjetosAsync(gestor)));
        // A outra empresa não vê nada — nem pelo link.
        Assert.Empty(await ProjetosAsync(caio));
        var daAna = (await ProjetosAsync(ana)).Single(p => p.Nivel == "empresa");
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync(daAna.Telas[0].Versoes[0].Link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync($"/api/design/projetos/{daAna.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/design/projetos")).StatusCode);
        // O time sem nome é recusado.
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsync("/api/design/versoes", Envio("x", Png(semente: 4), nivel: "time"))).StatusCode);
    }

    [Fact]
    public async Task Quem_nao_tem_empresa_guarda_o_design_no_nivel_usuario()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (solo, _) = await _api.UsuarioAsync(admin, "solo", Papeis.Dev);

        // Pediu empresa, mas não tem: fica no nível usuário.
        Assert.Equal(HttpStatusCode.Created, (await solo.PostAsync("/api/design/versoes", Envio("home", Png(), nivel: "empresa"))).StatusCode);

        var dele = Assert.Single(await ProjetosAsync(solo));
        Assert.Equal("usuario", dele.Nivel);
        Assert.Empty(await ProjetosAsync(ana));
        var uso = (await solo.GetFromJsonAsync<UsoDoDesignV1>("/api/design/uso"))!;
        Assert.Equal((1, "individual"), (uso.Versoes, uso.Plano));
    }

    [Fact]
    public async Task O_reenvio_do_mesmo_png_e_idempotente_e_outro_png_no_mesmo_numero_e_conflito()
    {
        var (ana, _, _, _) = await EmpresasAsync();
        var png = Png(semente: 7);

        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync("/api/design/versoes", Envio("login", png, numero: 3))).StatusCode);
        var reenvio = await ana.PostAsync("/api/design/versoes", Envio("login", png, numero: 3));
        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        Assert.False((await reenvio.Content.ReadFromJsonAsync<VersaoEnviadaV1>())!.Nova);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PostAsync("/api/design/versoes", Envio("login", Png(semente: 8), numero: 3))).StatusCode);

        // Sem número, a próxima da tela.
        var seguinte = (await (await ana.PostAsync("/api/design/versoes", Envio("login", Png(semente: 9)))).Content.ReadFromJsonAsync<VersaoEnviadaV1>())!;
        Assert.Equal(4, seguinte.Versao.Numero);
        Assert.Single(Assert.Single(await ProjetosAsync(ana)).Telas[0].Versoes, v => v.Numero == 3);

        // O que não é PNG nem chega ao banco.
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsync("/api/design/versoes", Envio("login", [1, 2, 3]))).StatusCode);
    }

    [Fact]
    public async Task Apaga_a_versao_o_autor_ou_o_gestor_e_o_terceiro_recebe_403()
    {
        var (ana, bia, gestor, _) = await EmpresasAsync();
        var v1 = (await (await ana.PostAsync("/api/design/versoes", Envio("perfil", Png(semente: 1)))).Content.ReadFromJsonAsync<VersaoEnviadaV1>())!;
        var v2 = (await (await ana.PostAsync("/api/design/versoes", Envio("perfil", Png(semente: 2)))).Content.ReadFromJsonAsync<VersaoEnviadaV1>())!;

        var daBia = Assert.Single(await ProjetosAsync(bia)).Telas[0].Versoes;
        Assert.All(daBia, v => Assert.False(v.PodeApagar));
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.DeleteAsync(v1.Versao.Link)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync(v1.Versao.Link)).StatusCode);
        Assert.True(Assert.Single(Assert.Single(await ProjetosAsync(gestor)).Telas[0].Versoes).PodeApagar);
        Assert.Equal(HttpStatusCode.OK, (await gestor.DeleteAsync(v2.Versao.Link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestor.DeleteAsync(v2.Versao.Link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync(v2.Versao.Link)).StatusCode);

        // A tela sem versão nenhuma sai da lista.
        Assert.Empty(Assert.Single(await ProjetosAsync(ana)).Telas);
    }

    [Fact]
    public async Task A_sincronizacao_incremental_traz_so_os_projetos_alterados_desde_a_ultima()
    {
        var (ana, bia, _, _) = await EmpresasAsync();
        await ana.PostAsync("/api/design/versoes", Envio("a", Png(semente: 1), repositorio: "https://github.com/acme/um"));
        var marco = Assert.Single(await ProjetosAsync(bia)).AtualizadoEm;

        Assert.Empty(await ProjetosAsync(bia, $"?desde={Uri.EscapeDataString(marco.ToString("O"))}"));
        await ana.PostAsync("/api/design/versoes", Envio("b", Png(semente: 2), repositorio: "https://github.com/acme/dois"));
        var novos = await ProjetosAsync(bia, $"?desde={Uri.EscapeDataString(marco.ToString("O"))}");
        Assert.Equal("github.com/acme/dois", Assert.Single(novos).Chave);
    }

    [Fact]
    public async Task O_teto_do_plano_recusa_a_versao_que_passa_com_413()
    {
        using var api = new ApiDeTeste(tetoDoDesign: 2);
        var admin = await api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");

        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync("/api/design/versoes", Envio("t", Png(semente: 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync("/api/design/versoes", Envio("t", Png(semente: 2)))).StatusCode);
        var estouro = await ana.PostAsync("/api/design/versoes", Envio("t", Png(semente: 3)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, estouro.StatusCode);
        Assert.Contains("teto do plano Empresarial", await estouro.Content.ReadAsStringAsync());

        var uso = (await ana.GetFromJsonAsync<UsoDoDesignV1>("/api/design/uso"))!;
        Assert.Equal((2, 2, "Empresarial"), (uso.Versoes, uso.TetoDeVersoes, uso.Plano));
    }
}
