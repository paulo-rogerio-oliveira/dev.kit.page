using System.Net;
using System.Net.Http.Json;
using DevKitPage.Api;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O pedido de demonstração pela API: o POST público (201/400), o limite por IP do cliente — certo
/// atrás do proxy, pelo <c>X-Forwarded-For</c> — e a leitura/exclusão só com o JWT do dashboard.
/// </summary>
public sealed class DemonstracoesApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static PedidoDeDemonstracaoV1 Pedido(string nome = "Ana Souza", bool consentimento = true)
        => new(nome, "ana@empresa.com.br", "Empresa", "Quero ver o fluxo com avaliadores.", consentimento);

    private static async Task<HttpStatusCode> EnviarDe(HttpClient cliente, string? ip)
    {
        using var mensagem = new HttpRequestMessage(HttpMethod.Post, "/api/demonstracoes") { Content = JsonContent.Create(Pedido()) };
        if (ip is not null)
            mensagem.Headers.Add("X-Forwarded-For", ip);
        return (await cliente.SendAsync(mensagem)).StatusCode;
    }

    [Fact]
    public async Task Pedido_valido_e_201_sem_login_e_aparece_no_dashboard()
    {
        var resposta = await _api.CreateClient().PostAsJsonAsync("/api/demonstracoes", Pedido());

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var criado = (await resposta.Content.ReadFromJsonAsync<PedidoDeDemonstracaoCriadoV1>())!;
        Assert.Equal($"/api/dashboard/demonstracoes/{criado.Id}", resposta.Headers.Location!.OriginalString);

        var admin = await _api.AdminAsync();
        var lista = (await admin.GetFromJsonAsync<Pagina<DemonstracaoResumo>>("/api/dashboard/demonstracoes"))!;
        var pedido = Assert.Single(lista.Itens);
        Assert.Equal(("Ana Souza", "ana@empresa.com.br"), (pedido.Nome, pedido.Email));
    }

    [Fact]
    public async Task Pedido_invalido_e_400_com_os_erros_por_campo()
    {
        var resposta = await _api.CreateClient().PostAsJsonAsync("/api/demonstracoes", new PedidoDeDemonstracaoV1("", "email-ruim", null, null, false));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var problema = (await resposta.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        Assert.Equal(["consentimento", "email", "nome"], problema.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Listar_e_excluir_sem_token_e_401()
    {
        var anonimo = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/dashboard/demonstracoes")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.DeleteAsync("/api/dashboard/demonstracoes/1")).StatusCode);
    }

    [Fact]
    public async Task Admin_exclui_o_pedido_e_o_inexistente_e_404()
    {
        var criado = (await (await _api.CreateClient().PostAsJsonAsync("/api/demonstracoes", Pedido())).Content.ReadFromJsonAsync<PedidoDeDemonstracaoCriadoV1>())!;
        var admin = await _api.AdminAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/dashboard/demonstracoes/{criado.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/dashboard/demonstracoes/{criado.Id}")).StatusCode);
        Assert.Equal(0, (await admin.GetFromJsonAsync<Pagina<DemonstracaoResumo>>("/api/dashboard/demonstracoes"))!.Total);
    }

    [Fact]
    public async Task Acima_do_limite_por_minuto_e_429_com_retry_after()
    {
        var cliente = _api.CreateClient();
        for (var i = 0; i < ApiDeTeste.LimitePorMinuto; i++)
            Assert.Equal(HttpStatusCode.Created, await EnviarDe(cliente, "198.51.100.7"));

        using var mensagem = new HttpRequestMessage(HttpMethod.Post, "/api/demonstracoes") { Content = JsonContent.Create(Pedido()) };
        mensagem.Headers.Add("X-Forwarded-For", "198.51.100.7");
        var recusada = await cliente.SendAsync(mensagem);

        Assert.Equal(HttpStatusCode.TooManyRequests, recusada.StatusCode);
        Assert.NotNull(recusada.Headers.RetryAfter);
        Assert.Contains("Tente de novo", (await recusada.Content.ReadFromJsonAsync<ProblemDetails>())!.Detail);
    }

    [Fact]
    public async Task Atras_do_proxy_confiavel_cada_ip_do_cliente_tem_a_sua_cota_e_o_mesmo_ip_divide()
    {
        // Toda conexão vem do proxy (10.1.2.3); o cliente real está no X-Forwarded-For.
        using var api = new ApiDeTeste(ipDaConexao: "10.1.2.3", redeConfiavel: "10.0.0.0/8");
        var cliente = api.CreateClient();

        for (var i = 0; i < ApiDeTeste.LimitePorMinuto; i++)
            Assert.Equal(HttpStatusCode.Created, await EnviarDe(cliente, "203.0.113.10"));

        Assert.Equal(HttpStatusCode.TooManyRequests, await EnviarDe(cliente, "203.0.113.10")); // o mesmo IP divide a cota
        Assert.Equal(HttpStatusCode.Created, await EnviarDe(cliente, "203.0.113.20"));         // outro IP não é afetado
    }

    [Fact]
    public async Task De_proxy_nao_confiavel_o_x_forwarded_for_e_ignorado_e_nao_foge_da_cota()
    {
        // A conexão vem de fora das redes confiáveis: trocar o X-Forwarded-For não dá cota nova.
        using var api = new ApiDeTeste(ipDaConexao: "192.0.2.50", redeConfiavel: "10.0.0.0/8");
        var cliente = api.CreateClient();

        for (var i = 0; i < ApiDeTeste.LimitePorMinuto; i++)
            Assert.Equal(HttpStatusCode.Created, await EnviarDe(cliente, $"203.0.113.{i + 1}"));

        Assert.Equal(HttpStatusCode.TooManyRequests, await EnviarDe(cliente, "203.0.113.99"));
    }

    [Fact]
    public async Task O_limite_do_formulario_nao_afeta_as_outras_rotas()
    {
        var cliente = _api.CreateClient();
        for (var i = 0; i <= ApiDeTeste.LimitePorMinuto; i++)
            await EnviarDe(cliente, null);

        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", ApiDeTeste.SenhaInicial))).StatusCode);
    }

    [Fact]
    public async Task Expurgo_diario_roda_o_expurgo_de_pedidos_mesmo_quando_o_de_eventos_falha()
    {
        await _api.CreateClient().PostAsJsonAsync("/api/demonstracoes", Pedido());
        var expurgo = new ExpurgoDiario(_api.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<ExpurgoDiario>.Instance);

        // A falha de um vai para o log e não derruba a volta; o seguinte roda com o serviço composto.
        await expurgo.ExpurgarAsync<IExpurgoDeEventos>("evento(s)", (_, _) => throw new InvalidOperationException("banco fora"), default);
        var apagados = -1;
        await expurgo.ExpurgarAsync<IExpurgoDePedidos>("pedido(s)", async (e, ct) => apagados = await e.ExpurgarAsync(ct), default);

        Assert.Equal(0, apagados); // o pedido de agora está dentro da retenção: fica
        var admin = await _api.AdminAsync();
        Assert.Equal(1, (await admin.GetFromJsonAsync<Pagina<DemonstracaoResumo>>("/api/dashboard/demonstracoes"))!.Total);
    }
}
