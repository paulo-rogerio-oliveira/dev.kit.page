using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A renovação do login PELA MÁQUINA (o serviço do dev.kit com o app fechado): o app logado a pede com o JWT e a chave da
/// máquina; o serviço a troca depois por um token novo só com a chave — sem a senha —, e ela não vale em outra máquina, como
/// Bearer, depois da troca de senha nem para o usuário bloqueado.
/// </summary>
public sealed class RenovacaoPelaMaquinaApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private async Task<(HttpClient Usuario, UsuarioComSenha Criado)> DevAsync(HttpClient admin, string login)
    {
        var criado = (await (await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo(login, $"Nome de {login}", Papeis.Dev, null))).Content.ReadFromJsonAsync<UsuarioComSenha>())!;
        var cliente = _api.CreateClient();
        var primeiro = (await (await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, criado.SenhaTemporaria))).Content.ReadFromJsonAsync<LoginResponse>())!;
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primeiro.Token);
        var troca = await cliente.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(criado.SenhaTemporaria, ApiDeTeste.NovaSenha));
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await troca.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        return (cliente, criado);
    }

    /// <summary>O app logado pede a renovação desta máquina (o JWT e a X-Machine-Key).</summary>
    private async Task<HttpResponseMessage> PedirAsync(HttpClient usuario, string maquina)
    {
        await _api.MaquinaAsync(maquina);
        using var pedido = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sessao-da-maquina");
        pedido.Headers.Authorization = usuario.DefaultRequestHeaders.Authorization;
        pedido.Headers.Add(ContratoV1.CabecalhoDaChave, _api.Chaves[maquina]);
        return await usuario.SendAsync(pedido);
    }

    /// <summary>O serviço troca a renovação pelo token, só com a chave da máquina — sem Bearer.</summary>
    private Task<HttpResponseMessage> RenovarAsync(string maquina, string renovacao)
    {
        var servico = _api.CreateClient();
        servico.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, _api.Chaves[maquina]);
        return servico.PostAsJsonAsync("/api/auth/renovar-pela-maquina", new RenovacaoPelaMaquinaV1(renovacao));
    }

    [Fact]
    public async Task O_servico_troca_a_renovacao_por_um_token_novo_e_a_seguinte_sem_a_senha()
    {
        var (ana, _) = await DevAsync(await _api.AdminAsync(), "ana.dev");
        var sessao = (await (await PedirAsync(ana, "m-ana")).Content.ReadFromJsonAsync<SessaoDaMaquinaV1>())!;

        var renovada = await RenovarAsync("m-ana", sessao.Renovacao);
        var corpo = (await renovada.Content.ReadFromJsonAsync<SessaoRenovadaPelaMaquinaV1>())!;
        var comNovo = _api.CreateClient();
        comNovo.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpo.Sessao.Token);

        Assert.Equal(HttpStatusCode.OK, renovada.StatusCode);
        Assert.Equal("ana.dev", corpo.Sessao.Login);
        Assert.True(sessao.ExpiraEm > DateTimeOffset.UtcNow.AddDays(29));
        Assert.Equal(HttpStatusCode.OK, (await comNovo.PostAsync("/api/auth/renovar", null)).StatusCode);
        // A seguinte também vale: o serviço encadeia as renovações enquanto a máquina estiver de pé.
        Assert.Equal(HttpStatusCode.OK, (await RenovarAsync("m-ana", corpo.Maquina.Renovacao)).StatusCode);
    }

    [Fact]
    public async Task A_renovacao_exige_a_chave_da_maquina_e_nao_vale_em_outra_nem_como_bearer()
    {
        var (ana, _) = await DevAsync(await _api.AdminAsync(), "ana.dev");
        var semChave = await ana.PostAsync("/api/auth/sessao-da-maquina", null);
        var sessao = (await (await PedirAsync(ana, "m-ana")).Content.ReadFromJsonAsync<SessaoDaMaquinaV1>())!;
        await _api.MaquinaAsync("m-outra");
        var comoBearer = _api.CreateClient();
        comoBearer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessao.Renovacao);
        var anonimo = await _api.CreateClient().PostAsJsonAsync("/api/auth/renovar-pela-maquina", new RenovacaoPelaMaquinaV1(sessao.Renovacao));

        Assert.Equal(HttpStatusCode.Unauthorized, semChave.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenovarAsync("m-outra", sessao.Renovacao)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenovarAsync("m-ana", sessao.Renovacao + "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await comoBearer.PostAsync("/api/auth/renovar", null)).StatusCode);
    }

    [Fact]
    public async Task A_troca_de_senha_invalida_a_renovacao_e_o_bloqueio_do_admin_a_recusa()
    {
        var admin = await _api.AdminAsync();
        var (ana, _) = await DevAsync(admin, "ana.dev");
        var (bia, biaCriada) = await DevAsync(admin, "bia.dev");
        var daAna = (await (await PedirAsync(ana, "m-ana")).Content.ReadFromJsonAsync<SessaoDaMaquinaV1>())!;
        var daBia = (await (await PedirAsync(bia, "m-bia")).Content.ReadFromJsonAsync<SessaoDaMaquinaV1>())!;

        (await ana.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(ApiDeTeste.NovaSenha, "outraSenha2026x"))).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/usuarios/{biaCriada.Usuario.Id}/bloqueio", new BloqueioDoUsuario(true))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await RenovarAsync("m-ana", daAna.Renovacao)).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await RenovarAsync("m-bia", daBia.Renovacao)).StatusCode);
    }
}
