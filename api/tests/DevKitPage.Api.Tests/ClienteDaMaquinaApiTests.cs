using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DevKitPage.Contracts.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O CLIENT CREDENTIALS da máquina (remote-control-v3 item 9): o app logado vincula a máquina com a chave pública; o serviço
/// troca uma asserção ES256 curta e de uso único pelo token, que vale só no remoto daquela máquina e daquele usuário. O
/// "Desvincular", a troca de senha e o bloqueio derrubam o vínculo — e o token já emitido — na hora.
/// </summary>
public sealed class ClienteDaMaquinaApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    private readonly List<ECDsa> _chaves = [];

    public void Dispose()
    {
        foreach (var chave in _chaves)
            chave.Dispose();
        _api.Dispose();
    }

    /// <summary>A máquina vinculada: o usuário logado, a chave privada que só ela tem e o anúncio da API.</summary>
    private sealed record Vinculo(HttpClient Usuario, ECDsa Chave, ClienteDaMaquinaRegistradoV1 Anuncio);

    private async Task<HttpResponseMessage> RegistrarAsync(HttpClient usuario, string maquina, string? chavePublica)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "/" + ClientCredentialsV1.RotaDoRegistro) { Content = JsonContent.Create(new ClienteDaMaquinaV1(chavePublica)) };
        pedido.Headers.Add(ContratoV1.CabecalhoDaChave, _api.Chaves[maquina]);
        return await usuario.SendAsync(pedido);
    }

    private async Task<Vinculo> VincularAsync(HttpClient admin, string login, string maquina)
    {
        var (usuario, _) = await _api.UsuarioAsync(admin, login, Papeis.Dev);
        await _api.MaquinaAsync(maquina);
        var chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _chaves.Add(chave);
        var resposta = await RegistrarAsync(usuario, maquina, Convert.ToBase64String(chave.ExportSubjectPublicKeyInfo()));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return new Vinculo(usuario, chave, (await resposta.Content.ReadFromJsonAsync<ClienteDaMaquinaRegistradoV1>())!);
    }

    /// <summary>A asserção do <c>private_key_jwt</c>, como o serviço do dev.kit a monta.</summary>
    private static string Assercao(Vinculo vinculo, DateTime? emitida = null, TimeSpan? validade = null, string? jti = null, ECDsa? outraChave = null)
    {
        var agora = emitida ?? DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = vinculo.Anuncio.ClientId,
            Audience = vinculo.Anuncio.Audiencia,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = agora + (validade ?? TimeSpan.FromSeconds(60)),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = vinculo.Anuncio.ClientId,
                [JwtRegisteredClaimNames.Jti] = jti ?? Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(new ECDsaSecurityKey(outraChave ?? vinculo.Chave), SecurityAlgorithms.EcdsaSha256),
        });
    }

    private Task<HttpResponseMessage> TokenAsync(string assercao, string grant = ClientCredentialsV1.GrantType)
        => _api.CreateClient().PostAsync("/" + ClientCredentialsV1.RotaDoToken, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = grant,
            ["client_assertion_type"] = ClientCredentialsV1.TipoDaAssercao,
            ["client_assertion"] = assercao,
        }));

    private async Task<HttpClient> ComTokenAsync(Vinculo vinculo)
    {
        var resposta = await TokenAsync(Assercao(vinculo));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var token = (await resposta.Content.ReadFromJsonAsync<TokenDoClienteV1>())!;
        var cliente = _api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return cliente;
    }

    private static HeartbeatV1 Heartbeat(string maquina) => new(RemotoV1.Contrato, maquina, "PC", "75", RemotoV1.OrigemServico, false, null, false);

    private static async Task<string> ErroAsync(HttpResponseMessage resposta)
        => (await resposta.Content.ReadFromJsonAsync<RecusaDoTokenV1>())!.Error;

    [Fact]
    public async Task O_vinculo_anuncia_o_client_credentials_e_a_asercao_vira_um_token_que_abre_o_remoto_da_maquina()
    {
        var vinculo = await VincularAsync(await _api.AdminAsync(), "ana.dev", "m-ana");

        var resposta = await TokenAsync(Assercao(vinculo));
        var token = (await resposta.Content.ReadFromJsonAsync<TokenDoClienteV1>())!;
        var servico = _api.CreateClient();
        servico.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        Assert.Equal(("m-ana", ClientCredentialsV1.Escopo, ClientCredentialsV1.RotaDoToken, ClientCredentialsV1.Algoritmo),
            (vinculo.Anuncio.ClientId, vinculo.Anuncio.Escopo, vinculo.Anuncio.RotaDoToken, vinculo.Anuncio.Algoritmo));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("no-store", resposta.Headers.CacheControl?.ToString());
        Assert.Equal(("Bearer", ClientCredentialsV1.Escopo, 15 * 60), (token.TokenType, token.Scope, token.ExpiresIn));
        // O heartbeat, o retrato, a SAS da fila, as falas, as respostas e o handshake entram com o token da máquina.
        Assert.Equal(HttpStatusCode.OK, (await servico.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat("m-ana"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await servico.PutAsJsonAsync("/api/remoto/retrato",
            new RetratoV1(RemotoV1.Contrato, "m-ana", DateTimeOffset.UtcNow, [], [], []))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await servico.PutAsJsonAsync("/api/remoto/catalogo",
            new CatalogoRemotoV1(RemotoV1.Contrato, "m-ana", DateTimeOffset.UtcNow, null, [], [], [], [], []))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await servico.PostAsJsonAsync("/api/remoto/conversa/falas",
            new FalaDoDevKitV1(RemotoV1.Contrato, "m-ana", "fala-1", ConversaV1.AutorDev, ConversaV1.OrigemDesktop, "Olá", DateTimeOffset.UtcNow))).StatusCode);
        // A resposta, o recebimento e a SAS passam da autorização (o desfecho de cada um é do contrato da conversa).
        foreach (var chamada in new[]
        {
            await servico.PostAsJsonAsync("/api/remoto/respostas",
                new RespostaDoDevKitV1(RemotoV1.Contrato, "m-ana", "resposta-1", "c-1", ConversaV1.RespostaConcluido, "Feito", DateTimeOffset.UtcNow)),
            await servico.PostAsJsonAsync("/api/remoto/comandos/c-1/recebido", new RecebimentoV1(RemotoV1.Contrato, "m-ana")),
            await servico.PostAsJsonAsync("/api/remoto/fila/sas", new PedidoDeSasV1(RemotoV1.Contrato, "m-ana")),
        })
            Assert.DoesNotContain(chamada.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        // E o site do dono vê a máquina vinculada, com o heartbeat do serviço.
        var maquina = Assert.Single((await vinculo.Usuario.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!);
        Assert.Equal(("m-ana", true, true), (maquina.MaquinaId, maquina.Vinculada, maquina.Conectada));
    }

    [Fact]
    public async Task O_token_da_maquina_e_recusado_fora_do_remoto_e_em_outra_maquina()
    {
        var admin = await _api.AdminAsync();
        var ana = await VincularAsync(admin, "ana.dev", "m-ana");
        var servico = await ComTokenAsync(ana);
        await _api.MaquinaAsync("m-outra");

        // Fora do remoto: o esquema padrão não aceita a audiência do token da máquina.
        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.GetAsync("/api/remoto/maquinas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.PostAsync("/api/auth/renovar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.PostAsync("/api/auth/sessao-da-maquina", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.GetAsync("/api/dashboard/maquinas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.GetAsync("/api/catalogo/empresa")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.DeleteAsync("/api/remoto/maquinas/m-ana/vinculo")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.PostAsJsonAsync("/api/remoto/comandos",
            new ComandoNovoV1("c-1", "m-ana", ConversaV1.ComandoMensagem, "oi"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.GetAsync("/api/remoto/maquinas/m-ana/decisoes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RegistrarAsync(servico, "m-ana", Convert.ToBase64String(ana.Chave.ExportSubjectPublicKeyInfo()))).StatusCode);
        // No remoto, só a máquina dela.
        Assert.Equal(HttpStatusCode.Forbidden, (await servico.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat("m-outra"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await servico.PostAsJsonAsync("/api/remoto/fila/sas", new PedidoDeSasV1(RemotoV1.Contrato, "m-outra"))).StatusCode);
        // O JWT do usuário segue valendo nas mesmas rotas (a transição da t_194).
        Assert.Equal(HttpStatusCode.OK, (await ana.Usuario.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat("m-outra"))).StatusCode);
    }

    [Fact]
    public async Task A_assercao_vencida_repetida_longa_ou_de_outra_chave_nao_vira_token()
    {
        var vinculo = await VincularAsync(await _api.AdminAsync(), "ana.dev", "m-ana");
        using var intrusa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var repetida = Assercao(vinculo, jti: "jti-unico");

        var vencida = await TokenAsync(Assercao(vinculo, emitida: DateTime.UtcNow.AddMinutes(-10)));
        var primeira = await TokenAsync(repetida);
        var segunda = await TokenAsync(repetida);
        var longa = await TokenAsync(Assercao(vinculo, validade: TimeSpan.FromHours(1)));
        var deOutraChave = await TokenAsync(Assercao(vinculo, outraChave: intrusa));
        var outroGrant = await TokenAsync(Assercao(vinculo), grant: "password");
        var semFormulario = await _api.CreateClient().PostAsJsonAsync("/" + ClientCredentialsV1.RotaDoToken, new { grant_type = ClientCredentialsV1.GrantType });

        Assert.Equal(HttpStatusCode.Unauthorized, vencida.StatusCode);
        Assert.Equal(ClientCredentialsV1.ErroClienteInvalido, await ErroAsync(vencida));
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, segunda.StatusCode);
        Assert.Contains("já foi usada", (await segunda.Content.ReadFromJsonAsync<RecusaDoTokenV1>())!.ErrorDescription);
        Assert.Equal(HttpStatusCode.Unauthorized, longa.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deOutraChave.StatusCode);
        Assert.Equal((HttpStatusCode.BadRequest, ClientCredentialsV1.ErroConcessaoNaoSuportada), (outroGrant.StatusCode, await ErroAsync(outroGrant)));
        Assert.Equal((HttpStatusCode.BadRequest, ClientCredentialsV1.ErroPedidoInvalido), (semFormulario.StatusCode, await ErroAsync(semFormulario)));
    }

    [Fact]
    public async Task O_vinculo_exige_o_usuario_a_chave_da_maquina_e_uma_chave_publica_p256()
    {
        var admin = await _api.AdminAsync();
        var (ana, _) = await _api.UsuarioAsync(admin, "ana.dev", Papeis.Dev);
        await _api.MaquinaAsync("m-ana");
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publica = Convert.ToBase64String(p256.ExportSubjectPublicKeyInfo());

        var semChaveDaMaquina = await ana.PostAsJsonAsync("/" + ClientCredentialsV1.RotaDoRegistro, new ClienteDaMaquinaV1(publica));
        using var anonimo = new HttpRequestMessage(HttpMethod.Post, "/" + ClientCredentialsV1.RotaDoRegistro) { Content = JsonContent.Create(new ClienteDaMaquinaV1(publica)) };
        anonimo.Headers.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["m-ana"]);

        Assert.Equal(HttpStatusCode.Unauthorized, semChaveDaMaquina.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().SendAsync(anonimo)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RegistrarAsync(ana, "m-ana", Convert.ToBase64String(p384.ExportSubjectPublicKeyInfo()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RegistrarAsync(ana, "m-ana", "não é chave")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegistrarAsync(ana, "m-ana", publica)).StatusCode);
    }

    [Fact]
    public async Task Desvincular_no_site_derruba_o_token_emitido_e_o_proximo_pedido()
    {
        var admin = await _api.AdminAsync();
        var ana = await VincularAsync(admin, "ana.dev", "m-ana");
        var bia = await VincularAsync(admin, "bia.dev", "m-bia");
        var servico = await ComTokenAsync(ana);
        Assert.Equal(HttpStatusCode.OK, (await servico.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat("m-ana"))).StatusCode);
        var renovacao = (await (await SessaoDaMaquinaAsync(ana.Usuario, "m-ana")).Content.ReadFromJsonAsync<SessaoDaMaquinaV1>())!.Renovacao;

        // A outra usuária não desvincula a máquina da Ana.
        Assert.Equal(HttpStatusCode.NotFound, (await bia.Usuario.DeleteAsync("/api/remoto/maquinas/m-ana/vinculo")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ana.Usuario.DeleteAsync("/api/remoto/maquinas/m-ana/vinculo")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await servico.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat("m-ana"))).StatusCode);
        var novo = await TokenAsync(Assercao(ana));
        Assert.Equal((HttpStatusCode.Unauthorized, ClientCredentialsV1.ErroClienteInvalido), (novo.StatusCode, await ErroAsync(novo)));
        Assert.False(Assert.Single((await ana.Usuario.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!).Vinculada);
        // Nem a renovação da t_194 segue: o serviço desvinculado não cai nela.
        Assert.Equal(HttpStatusCode.Unauthorized, (await SessaoDaMaquinaAsync(ana.Usuario, "m-ana")).StatusCode);
        var pelaMaquina = _api.CreateClient();
        pelaMaquina.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves["m-ana"]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await pelaMaquina.PostAsJsonAsync("/api/auth/renovar-pela-maquina", new RenovacaoPelaMaquinaV1(renovacao))).StatusCode);
        // A máquina da Bia segue vinculada; o login seguinte da Ana vincula de novo.
        Assert.Equal(HttpStatusCode.OK, (await TokenAsync(Assercao(bia))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RegistrarAsync(ana.Usuario, "m-ana", Convert.ToBase64String(ana.Chave.ExportSubjectPublicKeyInfo()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TokenAsync(Assercao(ana))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SessaoDaMaquinaAsync(ana.Usuario, "m-ana")).StatusCode);
    }

    private async Task<HttpResponseMessage> SessaoDaMaquinaAsync(HttpClient usuario, string maquina)
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sessao-da-maquina");
        pedido.Headers.Add(ContratoV1.CabecalhoDaChave, _api.Chaves[maquina]);
        return await usuario.SendAsync(pedido);
    }

    [Fact]
    public async Task A_troca_de_senha_e_o_bloqueio_revogam_todas_as_maquinas_do_usuario()
    {
        var admin = await _api.AdminAsync();
        var (ana, _) = await _api.UsuarioAsync(admin, "ana.dev", Papeis.Dev);
        await _api.MaquinaAsync("m-casa");
        await _api.MaquinaAsync("m-trabalho");
        async Task<Vinculo> VincularDaAnaAsync(string maquina)
        {
            var chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _chaves.Add(chave);
            var resposta = await RegistrarAsync(ana, maquina, Convert.ToBase64String(chave.ExportSubjectPublicKeyInfo()));
            return new Vinculo(ana, chave, (await resposta.Content.ReadFromJsonAsync<ClienteDaMaquinaRegistradoV1>())!);
        }
        var casa = await VincularDaAnaAsync("m-casa");
        var trabalho = await VincularDaAnaAsync("m-trabalho");
        var servicoDaCasa = await ComTokenAsync(casa);

        (await ana.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(ApiDeTeste.NovaSenha, "outraSenha2026x"))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await servicoDaCasa.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat("m-casa"))).StatusCode);
        foreach (var vinculo in new[] { casa, trabalho })
        {
            var recusa = await TokenAsync(Assercao(vinculo));
            Assert.Equal((HttpStatusCode.BadRequest, ClientCredentialsV1.ErroConcessaoInvalida), (recusa.StatusCode, await ErroAsync(recusa)));
        }

        // O bloqueio do admin: a Bia vinculada perde o token emitido e o próximo.
        var bia = await VincularAsync(admin, "bia.dev", "m-bia");
        var servicoDaBia = await ComTokenAsync(bia);
        var biaId = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == "bia.dev").Id;
        (await admin.PutAsJsonAsync($"/api/usuarios/{biaId}/bloqueio", new BloqueioDoUsuario(true))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await servicoDaBia.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat("m-bia"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await TokenAsync(Assercao(bia))).StatusCode);
    }

    [Fact]
    public async Task Sem_o_client_credentials_habilitado_o_vinculo_e_o_token_somem_e_a_renovacao_da_t194_segue()
    {
        using var desligada = _api.WithWebHostBuilder(b => b.UseSetting("ClienteDaMaquina:Habilitado", "false"));
        var admin = desligada.CreateClient();
        var primeiro = await _api.LoginAsync(admin);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primeiro.Token);
        var troca = await admin.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(ApiDeTeste.SenhaInicial, ApiDeTeste.NovaSenha));
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await troca.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        var registro = await desligada.CreateClient().PostAsJsonAsync("/api/maquinas/registrar", new MachineRegistrationV1("m-ana", "1.4.0", ApiDeTeste.CodigoDeRegistro, null, null));
        var chave = (await registro.Content.ReadFromJsonAsync<MachineRegistrationResponseV1>())!.Chave;
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        using var vinculo = new HttpRequestMessage(HttpMethod.Post, "/" + ClientCredentialsV1.RotaDoRegistro)
        { Content = JsonContent.Create(new ClienteDaMaquinaV1(Convert.ToBase64String(p256.ExportSubjectPublicKeyInfo()))) };
        vinculo.Headers.Add(ContratoV1.CabecalhoDaChave, chave);
        using var sessao = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sessao-da-maquina");
        sessao.Headers.Add(ContratoV1.CabecalhoDaChave, chave);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(vinculo)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desligada.CreateClient().PostAsync("/" + ClientCredentialsV1.RotaDoToken,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = ClientCredentialsV1.GrantType }))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(sessao)).StatusCode);
    }
}
