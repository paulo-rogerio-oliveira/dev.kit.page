using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A conta do dev (US #425, fase 3) de ponta a ponta: a adesão por código e e-mail (código inválido, e-mail de outra
/// empresa, sem assento), o link de definir a senha, o login com a recusa própria de cada caso, a política de login
/// por empresa, a <c>/api/conta</c> e o gestor administrando os usuários da empresa dele (403 na de outra).
/// </summary>
public sealed class ContaApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Adesao_cria_o_dev_manda_o_link_e_ele_entra_com_a_senha_que_definiu()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var anonimo = _api.CreateClient();

        var adesao = await anonimo.PostAsJsonAsync("/api/conta/adesao", new AdesaoV1(a.CodigoDeAdesao.ToLowerInvariant(), "Ana@A.com", "Ana"));

        Assert.Equal(HttpStatusCode.Accepted, adesao.StatusCode);
        var email = Assert.Single(_api.Emails.Mensagens);
        Assert.Equal("ana@a.com", email.Para);
        Assert.Contains($"{ApiDeTeste.UrlDoSite}/definir-senha?token=", email.Texto);
        Assert.DoesNotContain(_api.Emails.TokenPara("ana@a.com"), await adesao.Content.ReadAsStringAsync()); // o link só vai por e-mail

        var definida = await anonimo.PostAsJsonAsync("/api/conta/definir-senha", new DefinirSenhaV1(_api.Emails.TokenPara("ana@a.com"), ApiDeTeste.NovaSenha));
        var reuso = await anonimo.PostAsJsonAsync("/api/conta/definir-senha", new DefinirSenhaV1(_api.Emails.TokenPara("ana@a.com"), "outraSenha2026"));
        var login = (await (await anonimo.PostAsJsonAsync("/api/auth/login", new LoginRequest("ana@a.com", ApiDeTeste.NovaSenha))).Content.ReadFromJsonAsync<LoginResponse>())!;

        Assert.Equal(HttpStatusCode.NoContent, definida.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reuso.StatusCode);
        Assert.Equal((Papeis.Dev, "Empresa A", false), (login.Papel, login.Empresa, login.DeveTrocarSenha));
    }

    [Fact]
    public async Task Adesao_recusa_codigo_invalido_email_de_outra_empresa_e_empresa_sem_assento()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A", assentos: 1);
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var anonimo = _api.CreateClient();

        var codigo = await anonimo.PostAsJsonAsync("/api/conta/adesao", new AdesaoV1("DK-NADA-NADA", "bia@a.com"));
        var outra = await anonimo.PostAsJsonAsync("/api/conta/adesao", new AdesaoV1(b.CodigoDeAdesao, "ana@a.com"));
        var semAssento = await anonimo.PostAsJsonAsync("/api/conta/adesao", new AdesaoV1(a.CodigoDeAdesao, "bia@a.com"));
        var torta = await anonimo.PostAsJsonAsync("/api/conta/adesao", new AdesaoV1("", "nao-e-email"));

        Assert.Equal(HttpStatusCode.NotFound, codigo.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, outra.StatusCode);
        Assert.Contains("outra empresa", await outra.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, semAssento.StatusCode);
        Assert.Contains("assento", await semAssento.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, torta.StatusCode);
    }

    [Fact]
    public async Task Pedido_de_link_responde_igual_exista_ou_nao_o_email()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var antes = _api.Emails.Mensagens.Count;

        var existe = await _api.CreateClient().PostAsJsonAsync("/api/conta/link", new PedidoDeLinkV1("ana@a.com"));
        var naoExiste = await _api.CreateClient().PostAsJsonAsync("/api/conta/link", new PedidoDeLinkV1("ninguem@a.com"));

        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Accepted), (existe.StatusCode, naoExiste.StatusCode));
        Assert.Equal(await existe.Content.ReadAsStringAsync(), await naoExiste.Content.ReadAsStringAsync());
        Assert.Equal(antes + 1, _api.Emails.Mensagens.Count);
    }

    [Fact]
    public async Task Login_traz_o_codigo_proprio_de_cada_recusa()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A", assentos: 1);
        await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        // A bia passou do teto (criada pelo admin, que não conta assento): entra com a senha certa e é recusada.
        var bia = (await (await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("bia", "Bia", Papeis.Dev, a.Id))).Content.ReadFromJsonAsync<UsuarioComSenha>())!;
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var ana = (await gestor.GetFromJsonAsync<UsuarioDaEmpresaV1[]>("/api/empresa/usuarios"))!.Single(u => u.Login == "ana@a.com");
        (await gestor.PutAsJsonAsync($"/api/empresa/usuarios/{ana.Id}/bloqueio", new BloqueioDoUsuario(true))).EnsureSuccessStatusCode();

        var semAssento = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("bia", bia.SenhaTemporaria));
        var bloqueada = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("ana@a.com", ApiDeTeste.NovaSenha));

        Assert.Equal(HttpStatusCode.OK, semAssento.StatusCode); // bloquear a Ana liberou o assento para a Bia
        Assert.Equal(HttpStatusCode.Locked, bloqueada.StatusCode);
        Assert.Equal(CodigosDeRecusa.BloqueadoPeloAdmin, await CodigoAsync(bloqueada));

        // Com a Ana desbloqueada não há assento: o gestor recebe a recusa e a Bia segue entrando (ela entrou primeiro no assento livre).
        Assert.Equal(HttpStatusCode.Conflict, (await gestor.PutAsJsonAsync($"/api/empresa/usuarios/{ana.Id}/bloqueio", new BloqueioDoUsuario(false))).StatusCode);
    }

    /// <summary>
    /// A renovação do token do app (US #425): com o token válido sai um novo, sem senha; o bloqueio do gestor corta a
    /// renovação com o mesmo código do login; e sem token é 401.
    /// </summary>
    [Fact]
    public async Task Renovar_o_token_da_um_novo_e_o_bloqueio_do_gestor_corta()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, login) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");

        var renovado = await ana.PostAsync("/api/auth/renovar", null);
        var novo = (await renovado.Content.ReadFromJsonAsync<LoginResponse>())!;
        var anaId = (await gestor.GetFromJsonAsync<UsuarioDaEmpresaV1[]>("/api/empresa/usuarios"))!.Single(u => u.Login == "ana@a.com").Id;
        (await gestor.PutAsJsonAsync($"/api/empresa/usuarios/{anaId}/bloqueio", new BloqueioDoUsuario(true))).EnsureSuccessStatusCode();
        var cortada = await ana.PostAsync("/api/auth/renovar", null);
        var anonima = await _api.CreateClient().PostAsync("/api/auth/renovar", null);

        Assert.Equal(HttpStatusCode.OK, renovado.StatusCode);
        Assert.False(string.IsNullOrEmpty(novo.Token));
        Assert.Equal(login.Login, novo.Login);
        Assert.Equal(HttpStatusCode.Locked, cortada.StatusCode);
        Assert.Equal(CodigosDeRecusa.BloqueadoPeloAdmin, await CodigoAsync(cortada));
        Assert.Equal(HttpStatusCode.Unauthorized, anonima.StatusCode);
    }

    [Fact]
    public async Task Dev_alem_do_assento_recebe_403_com_o_codigo_sem_assento()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A", assentos: 1);
        await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var bia = (await (await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo("bia", "Bia", Papeis.Dev, a.Id))).Content.ReadFromJsonAsync<UsuarioComSenha>())!;

        var resposta = await _api.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("bia", bia.SenhaTemporaria));

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal(CodigosDeRecusa.SemAssento, await CodigoAsync(resposta));
    }

    [Fact]
    public async Task Politica_de_login_e_por_empresa_e_a_global_e_so_o_padrao()
    {
        using var obrigatoria = new ApiDeTeste(loginObrigatorio: true);
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var gestorA = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var maquinaDeA = await _api.MaquinaAsync("maquina-a", a.CodigoDeAdesao, "Ana");
        var maquinaDeB = await _api.MaquinaAsync("maquina-b", b.CodigoDeAdesao, "Bia");

        var antes = (await maquinaDeA.GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!;
        var definida = (await (await gestorA.PutAsJsonAsync("/api/empresa/politica", new PoliticaDaEmpresaV1(true))).Content.ReadFromJsonAsync<EmpresaDoGestorV1>())!;
        var daA = (await maquinaDeA.GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!;
        var daB = (await maquinaDeB.GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!;
        var anonima = (await _api.CreateClient().GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!;
        var versaoDaA = (await maquinaDeA.GetFromJsonAsync<VersaoV1>("/api/versoes/ultima"))!;

        Assert.False(antes.LoginObrigatorio);
        Assert.Equal((true, true), (definida.LoginObrigatorio, definida.LoginObrigatorioEfetivo));
        Assert.True(daA.LoginObrigatorio);
        Assert.False(daB.LoginObrigatorio); // a B segue o padrão global (desligado)
        Assert.False(anonima.LoginObrigatorio);
        Assert.True(versaoDaA.LoginObrigatorio);

        // Com o global LIGADO, a empresa que escolheu "não obrigatório" o sobrepõe.
        var adminObrigatoria = await obrigatoria.AdminAsync();
        var c = await ApiDeTeste.EmpresaAsync(adminObrigatoria, "Empresa C");
        var gestorC = await obrigatoria.GestorAsync(adminObrigatoria, c.Id, "gestor.c");
        var maquinaDeC = await obrigatoria.MaquinaAsync("maquina-c", c.CodigoDeAdesao, "Caio");
        Assert.True((await maquinaDeC.GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!.LoginObrigatorio);
        (await gestorC.PutAsJsonAsync("/api/empresa/politica", new PoliticaDaEmpresaV1(false))).EnsureSuccessStatusCode();
        Assert.False((await maquinaDeC.GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!.LoginObrigatorio);
        (await gestorC.PutAsJsonAsync("/api/empresa/politica", new PoliticaDaEmpresaV1(null))).EnsureSuccessStatusCode();
        Assert.True((await maquinaDeC.GetFromJsonAsync<PoliticaDeLoginV1>("/api/auth/politica"))!.LoginObrigatorio);
    }

    [Fact]
    public async Task Conta_mostra_quem_esta_logado_e_a_politica_que_vale()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");

        var conta = (await ana.GetFromJsonAsync<ContaV1>("/api/conta"))!;

        Assert.Equal(("ana@a.com", "ana@a.com", Papeis.Dev, "Empresa A", false, true, (string?)null), (conta.Login, conta.Email, conta.Papel, conta.Empresa, conta.Individual, conta.AssinaturaAtiva, conta.UrlGerenciarAssinatura));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/conta")).StatusCode);
    }

    [Fact]
    public async Task Gestor_administra_so_a_empresa_dele_e_o_dev_nao_administra()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var gestorA = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        var caio = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == "caio@b.com");

        var usuarios = (await gestorA.GetFromJsonAsync<UsuarioDaEmpresaV1[]>("/api/empresa/usuarios"))!;
        var empresa = (await gestorA.GetFromJsonAsync<EmpresaDoGestorV1>("/api/empresa"))!;

        Assert.Equal(new[] { "ana@a.com", "gestor.a" }, usuarios.Select(u => u.Login));
        Assert.Equal((10, 1), (empresa.Assentos, empresa.AssentosOcupados)); // o gestor não ocupa assento de dev
        Assert.Equal(HttpStatusCode.Forbidden, (await gestorA.PutAsJsonAsync($"/api/empresa/usuarios/{caio.Id}/bloqueio", new BloqueioDoUsuario(true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/empresa/usuarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.PostAsync("/api/empresa/codigo", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/empresa")).StatusCode);

        // O código novo vale na hora, e o antigo deixa de valer.
        var novo = (await (await gestorA.PostAsync("/api/empresa/codigo", null)).Content.ReadFromJsonAsync<CodigoDeAdesaoV1>())!;
        Assert.NotEqual(a.CodigoDeAdesao, novo.CodigoDeAdesao);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.CreateClient().PostAsJsonAsync("/api/conta/adesao", new AdesaoV1(a.CodigoDeAdesao, "bia@a.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await _api.CreateClient().PostAsJsonAsync("/api/conta/adesao", new AdesaoV1(novo.CodigoDeAdesao, "bia@a.com"))).StatusCode);
    }

    [Fact]
    public async Task Token_do_dev_leva_a_empresa_mas_o_painel_continua_fechado()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, login) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");

        var claims = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(login.Token);

        Assert.Equal(a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), claims.GetClaim(Seguranca.ClaimEmpresa).Value);
        Assert.Equal(HttpStatusCode.Forbidden, (await ana.GetAsync("/api/dashboard/maquinas")).StatusCode);
    }

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;
    }
}
