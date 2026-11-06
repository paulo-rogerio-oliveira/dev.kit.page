using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DevKitPage.Api.Tests;

public sealed class PushApiTests
{
    private const string Rota = "/api/remoto/push";
    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public async Task Exige_login_e_sem_configuracao_nao_finge_assinatura()
    {
        using var api = new ApiDeTeste();
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(Rota)).StatusCode);
        var temporario = api.CreateClient(); var primeiro = await api.LoginAsync(temporario);
        temporario.DefaultRequestHeaders.Authorization = new("Bearer", primeiro.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await temporario.GetAsync(Rota)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await temporario.PostAsJsonAsync(Rota + "/assinaturas", Pedido())).StatusCode);
        var c = await api.AdminAsync(); var config = await c.GetFromJsonAsync<ConfiguracaoDePushV1>(Rota);
        Assert.False(config!.Disponivel); Assert.Null(config.ChavePublica);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await c.PostAsJsonAsync(Rota + "/assinaturas", Pedido())).StatusCode);
    }

    [Fact]
    public async Task Corpo_invalido_formato_tamanho_e_destino_arbitrario_sao_recusados()
    {
        using var api = new ApiDeTeste(); var c = await api.AdminAsync();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync(Rota + "/assinaturas", new StringContent("texto"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync(Rota + "/assinaturas", new StringContent("{", Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.PostAsync(Rota + "/assinaturas", new StringContent(new string('x', 4097), Encoding.UTF8, "application/json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Rota + "/assinaturas", Pedido() with { Endpoint = "https://127.0.0.1/segredo" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Rota + "/assinaturas", new { endpoint = "https://fcm.googleapis.com/teste", keys = new { p256dh = "invalido", auth = "invalido" } })).StatusCode);
    }

    [Fact]
    public async Task Assina_entrega_abre_decisao_e_cancela_pelo_http_real()
    {
        using var original = new ApiDeTeste(); using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256); var chave = ecdsa.ExportParameters(true);
        var transporte = new Transporte();
        using var api = original.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IHostedService>();
            s.Configure<OpcoesDoPush>(o => { o.ChavePublica = B64([4, .. chave.Q.X!, .. chave.Q.Y!]); o.ChavePrivada = B64(chave.D!); o.Assunto = "https://site.test"; });
            s.AddSingleton<ITransporteDePush>(transporte);
        }));
        var login = await original.AdminAsync(); var c = api.CreateClient(); c.DefaultRequestHeaders.Authorization = login.DefaultRequestHeaders.Authorization;
        var resposta = await c.PostAsJsonAsync(Rota + "/assinaturas", Pedido()); resposta.EnsureSuccessStatusCode();
        Assert.Equal("no-store", resposta.Headers.CacheControl!.ToString());
        var assinatura = (await resposta.Content.ReadFromJsonAsync<AssinaturaRegistradaV1>())!;
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(Rota + "/assinaturas/" + assinatura.Id)).StatusCode);
        (await c.PostAsJsonAsync("/api/remoto/heartbeat", new HeartbeatV1(1, "demo", "Demo", "108", "servico", true, "ativo", false))).EnsureSuccessStatusCode();
        (await c.PutAsJsonAsync("/api/remoto/retrato", new RetratoV1(1, "demo", DateTimeOffset.UtcNow, [], [],
            [new("pergunta", "pergunta", "Escolher", Pergunta: "Continuar?", Opcoes: ["Sim", "Não"], JaTentei: "Verificações concluídas")]))).EnsureSuccessStatusCode();
        using (var escopo = api.Services.CreateScope()) Assert.Equal(1, await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePush>().ProcessarAsync(default));
        var entrega = Assert.Single(transporte.Entregas);
        Assert.Equal("pergunta", (await c.GetFromJsonAsync<DestinoDoPushV1>(Rota + "/entregas/" + entrega))!.PendenciaId);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync(Rota + "/assinaturas/" + assinatura.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(Rota + "/entregas/" + entrega)).StatusCode);
        using (var escopo = api.Services.CreateScope()) Assert.Equal(0, await escopo.ServiceProvider.GetRequiredService<IProcessamentoDePush>().ProcessarAsync(default));
        Assert.Single(transporte.Entregas);
    }
    private static AssinaturaDePushV1 Pedido()
    {
        using var e = ECDsa.Create(ECCurve.NamedCurves.nistP256); var p = e.ExportParameters(false);
        return new("https://fcm.googleapis.com/fcm/send/teste", new(B64([4, .. p.Q.X!, .. p.Q.Y!]), B64(new byte[16])));
    }
    private sealed class Transporte : ITransporteDePush
    {
        public List<string> Entregas { get; } = [];
        public Task<ResultadoDoEnvioDePush> EnviarAsync(AssinaturaDePushV1 assinatura, string entrega, CancellationToken ct)
        { Entregas.Add(entrega); return Task.FromResult(ResultadoDoEnvioDePush.Aceito); }
    }
}
