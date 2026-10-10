using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A licença e a compra (US #425, fases 4 e 5) de ponta a ponta: o trial assinado no registro da máquina, a
/// renovação que exige o login E a chave da máquina (e cada recusa), o 503 sem a chave privada; o aviso do gateway
/// verificado e idempotente, o cancelamento que corta a renovação e a empresa individual que publica para si.
/// </summary>
public sealed class LicencasApiTests : IDisposable
{
    private const string Impressao = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static ConteudoDaLicenca? Ler(LicencaV1? licenca) => LicencaAssinada.Ler(licenca?.Conteudo, Ed25519.ChavePublica(ApiDeTeste.SementeDaLicenca));

    private async Task<MachineRegistrationResponseV1> RegistrarAsync(string maquina, string? impressao = Impressao)
        => (await (await _api.CreateClient().PostAsJsonAsync("/api/maquinas/registrar",
            new MachineRegistrationV1(maquina, "151", ApiDeTeste.CodigoDeRegistro, Impressao: impressao))).Content.ReadFromJsonAsync<MachineRegistrationResponseV1>())!;

    [Fact]
    public async Task Registro_com_a_impressao_devolve_o_trial_assinado_e_reinstalar_nao_reinicia()
    {
        var primeiro = await RegistrarAsync("maquina-1");
        var reinstalada = await RegistrarAsync("maquina-reinstalada");
        var semImpressao = await RegistrarAsync("maquina-antiga", impressao: null);

        var lida = Ler(primeiro.Trial)!;
        Assert.Equal((TiposDeLicenca.Trial, "maquina-1"), (lida.Tipo, lida.Maquina));
        Assert.Equal(14, Math.Round((lida.ValidaAte - lida.EmitidaEm).TotalDays));
        Assert.Equal(primeiro.Trial!.ValidaAte, reinstalada.Trial!.ValidaAte); // conta do primeiro registro da impressão
        Assert.Equal("maquina-reinstalada", Ler(reinstalada.Trial)!.Maquina);
        Assert.Null(semImpressao.Trial); // o dev.kit antigo segue sem trial assinado
    }

    [Fact]
    public async Task Renovacao_exige_o_login_e_a_chave_da_maquina_e_emite_30_dias()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var maquina = await _api.MaquinaAsync("maquina-1");

        var semMaquina = await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1());
        var semLogin = await maquina.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1());
        ana.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["maquina-1"]);
        var renovada = await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1());
        var licenca = (await renovada.Content.ReadFromJsonAsync<LicencaV1>())!;
        var cedo = await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1(licenca.Conteudo));

        Assert.Equal(HttpStatusCode.Unauthorized, semMaquina.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, semLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renovada.StatusCode);
        var lida = Ler(licenca)!;
        Assert.Equal((TiposDeLicenca.Assinatura, "maquina-1", "ana@a.com"), (lida.Tipo, lida.Maquina, lida.Usuario));
        Assert.Equal(30, Math.Round((lida.ValidaAte - lida.EmitidaEm).TotalDays));
        Assert.Equal(HttpStatusCode.Conflict, cedo.StatusCode); // a janela abre no 7º dia
    }

    [Fact]
    public async Task Renovacao_recusa_o_bloqueado_e_quem_nao_tem_empresa()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (semEmpresa, _) = await _api.UsuarioAsync(admin, "solto", Papeis.Dev);
        await _api.MaquinaAsync("maquina-1");
        ana.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["maquina-1"]);
        semEmpresa.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["maquina-1"]);
        var id = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == "ana@a.com").Id;
        (await admin.PutAsJsonAsync($"/api/usuarios/{id}/bloqueio", new BloqueioDoUsuario(true))).EnsureSuccessStatusCode();

        var bloqueada = await ana.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1());
        var solta = await semEmpresa.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1());

        Assert.Equal(HttpStatusCode.Forbidden, bloqueada.StatusCode);
        Assert.Contains("bloqueado", await bloqueada.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, solta.StatusCode);
        Assert.Contains("empresa", await solta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Sem_a_chave_privada_a_renovacao_e_503_e_o_registro_segue_sem_trial()
    {
        using var semChave = new ApiDeTeste(comLicenca: false);
        var admin = await semChave.AdminAsync();
        await semChave.MaquinaAsync("maquina-1");
        admin.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, semChave.Chaves["maquina-1"]);

        var registro = (await (await semChave.CreateClient().PostAsJsonAsync("/api/maquinas/registrar",
            new MachineRegistrationV1("maquina-2", "151", ApiDeTeste.CodigoDeRegistro, Impressao: Impressao))).Content.ReadFromJsonAsync<MachineRegistrationResponseV1>())!;
        var renovar = await admin.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1());

        Assert.NotEmpty(registro.Chave);
        Assert.Null(registro.Trial);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, renovar.StatusCode);
    }

    private async Task<HttpResponseMessage> AvisarAsync(object aviso, string? segredo = ApiDeTeste.SegredoDoWebhook, DateTimeOffset? em = null)
    {
        var corpo = System.Text.Json.JsonSerializer.Serialize(aviso, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/pagamentos/webhook") { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        if (segredo is not null)
            pedido.Headers.Add(RegrasDoGateway.Cabecalho, RegrasDoGateway.Assinar(segredo, corpo, em ?? DateTimeOffset.UtcNow));
        return await _api.CreateClient().SendAsync(pedido);
    }

    [Fact]
    public async Task Webhook_recusa_a_assinatura_invalida_e_o_aviso_velho()
    {
        var aviso = new AvisoDoGatewayV1("evt_1", TiposDeAviso.PagamentoAprovado, new DadosDoAvisoV1("rui@solo.com", "Rui", "sub_1", "Individual"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await AvisarAsync(aviso, segredo: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AvisarAsync(aviso, segredo: "outro")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AvisarAsync(aviso, em: DateTimeOffset.UtcNow.AddHours(-1))).StatusCode);
        Assert.Empty(_api.Emails.Mensagens);
    }

    [Fact]
    public async Task Pagamento_repetido_nao_duplica_e_o_cancelamento_corta_a_renovacao()
    {
        var aviso = new AvisoDoGatewayV1("evt_1", TiposDeAviso.PagamentoAprovado, new DadosDoAvisoV1("rui@solo.com", "Rui", "sub_1", "Individual"));

        var primeiro = await AvisarAsync(aviso);
        var repetido = await AvisarAsync(aviso);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (primeiro.StatusCode, repetido.StatusCode));
        Assert.Contains("true", await primeiro.Content.ReadAsStringAsync());
        Assert.Contains("false", await repetido.Content.ReadAsStringAsync());
        Assert.Single(_api.Emails.Mensagens); // um link, uma conta

        // O comprador define a senha pelo link, entra, vê a conta individual e renova na máquina dele.
        var anonimo = _api.CreateClient();
        (await anonimo.PostAsJsonAsync("/api/conta/definir-senha", new DefinirSenhaV1(_api.Emails.TokenPara("rui@solo.com"), ApiDeTeste.NovaSenha))).EnsureSuccessStatusCode();
        var rui = _api.CreateClient();
        var login = (await (await rui.PostAsJsonAsync("/api/auth/login", new LoginRequest("rui@solo.com", ApiDeTeste.NovaSenha))).Content.ReadFromJsonAsync<LoginResponse>())!;
        rui.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        await _api.MaquinaAsync("maquina-do-rui");
        rui.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["maquina-do-rui"]);

        var conta = (await rui.GetFromJsonAsync<ContaV1>("/api/conta"))!;
        Assert.Equal((true, true, "https://gateway.test/portal"), (conta.Individual, conta.AssinaturaAtiva, conta.UrlGerenciarAssinatura));
        Assert.Equal(HttpStatusCode.OK, (await rui.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1())).StatusCode);

        (await AvisarAsync(new AvisoDoGatewayV1("evt_2", TiposDeAviso.AssinaturaCancelada, new DadosDoAvisoV1(null, null, "sub_1", null)))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.PaymentRequired, (await rui.PostAsJsonAsync("/api/licencas/renovar", new RenovacaoV1())).StatusCode);
        Assert.False((await rui.GetFromJsonAsync<ContaV1>("/api/conta"))!.AssinaturaAtiva);
    }

    [Fact]
    public async Task Individual_publica_assina_e_sincroniza_no_proprio_catalogo_e_nao_ve_o_de_outros()
    {
        var admin = await _api.AdminAsync();
        var outra = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(outra.CodigoDeAdesao, "ana@a.com");
        var daAna = (await (await ana.PostAsJsonAsync("/api/catalogo/empresa", new PublicacaoV1(TiposDeItem.Skill, "skill-da-ana", "", "{}"))).Content.ReadFromJsonAsync<PublicacaoFeitaV1>())!;
        (await AvisarAsync(new AvisoDoGatewayV1("evt_1", TiposDeAviso.PagamentoAprovado, new DadosDoAvisoV1("rui@solo.com", "Rui", "sub_1", "Individual")))).EnsureSuccessStatusCode();
        (await _api.CreateClient().PostAsJsonAsync("/api/conta/definir-senha", new DefinirSenhaV1(_api.Emails.TokenPara("rui@solo.com"), ApiDeTeste.NovaSenha))).EnsureSuccessStatusCode();
        var rui = _api.CreateClient();
        rui.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await (await rui.PostAsJsonAsync("/api/auth/login", new LoginRequest("rui@solo.com", ApiDeTeste.NovaSenha))).Content.ReadFromJsonAsync<LoginResponse>())!.Token);

        var publicada = (await (await rui.PostAsJsonAsync("/api/catalogo/empresa", new PublicacaoV1(TiposDeItem.Skill, "minha-skill", "Só minha", "{\"name\":\"minha-skill\"}"))).Content.ReadFromJsonAsync<PublicacaoFeitaV1>())!;
        (await rui.PostAsync($"/api/catalogo/empresa/{publicada.Id}/assinatura", null)).EnsureSuccessStatusCode();

        Assert.Equal("minha-skill", Assert.Single((await rui.GetFromJsonAsync<ItemAssinadoV1[]>("/api/catalogo/empresa/assinados"))!).Nome);
        Assert.Equal(new[] { "minha-skill" }, (await rui.GetFromJsonAsync<ItemDaEmpresaV1[]>("/api/catalogo/empresa"))!.Select(i => i.Nome));
        Assert.Equal(HttpStatusCode.Forbidden, (await rui.PostAsync($"/api/catalogo/empresa/{daAna.Id}/assinatura", null)).StatusCode);
    }

    [Fact]
    public async Task Checkout_sem_gateway_configurado_vai_para_o_simulado()
    {
        var checkout = await _api.CreateClient().PostAsJsonAsync("/api/pagamentos/checkout", new CheckoutV1("rui@solo.com", "Rui", "individual-mensal"));
        var torto = await _api.CreateClient().PostAsJsonAsync("/api/pagamentos/checkout", new CheckoutV1("nao-e-email", null, "individual-mensal"));

        var criado = (await checkout.Content.ReadFromJsonAsync<CheckoutCriadoV1>())!;
        Assert.Equal(("/compra-concluida?simulado=1&plano=individual-mensal", true), (criado.Url, criado.Simulado));
        Assert.Equal(HttpStatusCode.BadRequest, torto.StatusCode);
    }
}
