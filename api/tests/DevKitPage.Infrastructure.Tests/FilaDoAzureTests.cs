using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Azure.Core;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// A fila do Azure contra um ARM e um Storage de mentira (nenhum teste sai para a rede): a chave lida pelo <c>listKeys</c> com
/// o token da identidade da API e guardada só na memória; a mensagem posta com a SAS de ESCRITA e o TTL, sem token nem chave
/// no pedido do Storage; a SAS de CONSUMO só da fila e só com <c>p</c>, verificável com a chave; a chave trocada relida uma vez;
/// cada falha em português, sem a SAS. E a composição: a simulada não sobe em Production.
/// </summary>
public sealed class FilaDoAzureTests
{
    private static readonly string Chave = Convert.ToBase64String(Encoding.UTF8.GetBytes("chave-de-teste-da-conta-de-armazenamento-0001"));
    private static readonly DateTimeOffset Agora = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly FilaDoUsuario Fila = new(7, 42, "dkq16abcd1234", "comandos",
        "/subscriptions/3f2b9c0e-5d7a-4c1b-9e2f-1a2b3c4d5e6f/resourceGroups/rg-devkit-u42/providers/Microsoft.Storage/storageAccounts/dkq16abcd1234",
        "https://dkq16abcd1234.queue.core.windows.net/comandos");

    private sealed class CredencialFalsa : TokenCredential
    {
        public int Pedidos { get; private set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Assert.Equal(["https://management.azure.com/.default"], requestContext.Scopes);
            Pedidos++;
            return new AccessToken("token-da-identidade-da-api", Agora.AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>O Azure de mentira: guarda cada pedido (com o corpo) e responde pela função do teste.</summary>
    private sealed class AzureFalso(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler, IHttpClientFactory
    {
        public List<(HttpRequestMessage Pedido, string Corpo)> Pedidos { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var corpo = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Pedidos.Add((request, corpo));
            return responder(request, corpo);
        }

        public HttpClient CreateClient(string name)
        {
            Assert.Equal(FilaDoAzure.NomeDoCliente, name);
            return new HttpClient(this, disposeHandler: false);
        }
    }

    private static HttpResponseMessage Chaves()
        => new(HttpStatusCode.OK) { Content = new StringContent($$"""{"keys":[{"keyName":"key1","value":"{{Chave}}","permissions":"FULL"}]}""") };

    /// <summary>O Azure que funciona: o listKeys devolve a chave e o Storage aceita a mensagem (201).</summary>
    private static HttpResponseMessage Funciona(HttpRequestMessage pedido, string corpo)
        => pedido.RequestUri!.Host == "management.azure.com" ? Chaves() : new HttpResponseMessage(HttpStatusCode.Created);

    private static (FilaDoAzure Fila, AzureFalso Azure, CredencialFalsa Credencial) Criar(Func<HttpRequestMessage, string, HttpResponseMessage>? responder = null)
    {
        var azure = new AzureFalso(responder ?? Funciona);
        var credencial = new CredencialFalsa();
        var fila = new FilaDoAzure(azure, credencial, new MemoryCache(new MemoryCacheOptions()), new RelogioDeTeste(Agora), Options.Create(new OpcoesDoRemoto()));
        return (fila, azure, credencial);
    }

    private static Dictionary<string, string> Query(string query)
        => query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public async Task A_mensagem_vai_com_a_sas_de_escrita_e_o_ttl_e_a_chave_sai_do_listkeys_uma_vez()
    {
        var (fila, azure, credencial) = Criar();

        await fila.EnviarAsync(Fila, """{"id":"c-1"}""", TimeSpan.FromSeconds(900), default);
        await fila.EnviarAsync(Fila, """{"id":"c-2"}""", TimeSpan.FromSeconds(59.2), default);

        var (listKeys, _) = azure.Pedidos[0];
        Assert.Equal(HttpMethod.Post, listKeys.Method);
        Assert.Equal($"https://management.azure.com{Fila.IdNoAzure}/listKeys?api-version={FilaDoAzure.VersaoDoArm}", listKeys.RequestUri!.ToString());
        Assert.Equal(("Bearer", "token-da-identidade-da-api"), (listKeys.Headers.Authorization!.Scheme, listKeys.Headers.Authorization.Parameter));
        // A chave na memória: o segundo envio não volta ao ARM.
        Assert.Equal(3, azure.Pedidos.Count);
        Assert.Equal(1, credencial.Pedidos);

        var (mensagem, corpo) = azure.Pedidos[1];
        Assert.Equal(HttpMethod.Post, mensagem.Method);
        Assert.Equal("https://dkq16abcd1234.queue.core.windows.net/comandos/messages", mensagem.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Null(mensagem.Headers.Authorization);
        Assert.Equal(RegrasDaConversa.VersaoDoStorage, mensagem.Headers.GetValues("x-ms-version").Single());
        var query = Query(mensagem.RequestUri.Query);
        Assert.Equal(("900", FilaDoAzure.PermissaoDeEscrita, "https"), (query["messagettl"], query["sp"], query["spr"]));
        Assert.Equal("60", Query(azure.Pedidos[2].Pedido.RequestUri!.Query)["messagettl"]);
        var base64 = Regex.Match(corpo, "<MessageText>(.*)</MessageText>").Groups[1].Value;
        Assert.Equal("""{"id":"c-1"}""", Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
        Assert.DoesNotContain(Chave, mensagem.RequestUri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sas_de_consumo_e_so_da_fila_so_com_p_e_confere_com_a_chave()
    {
        var (fila, _, _) = Criar();
        var ate = Agora.AddMinutes(15);

        var acesso = await fila.EmitirSasAsync(Fila, ate, default);

        Assert.Equal(("https://dkq16abcd1234.queue.core.windows.net/comandos", ate), (acesso.Endpoint, acesso.ExpiraEm));
        var query = Query(acesso.Sas);
        Assert.Equal((ConversaV1.PermissaoDaSas, "https", "2026-11-03T12:15:00Z", "2026-11-03T11:58:00Z"), (query["sp"], query["spr"], query["se"], query["st"]));
        using var hmac = new HMACSHA256(Convert.FromBase64String(Chave));
        var texto = RegrasDaConversa.TextoDaSas(Fila.Conta, Fila.Fila, "p", DateTimeOffset.Parse(query["st"], System.Globalization.CultureInfo.InvariantCulture), ate);
        Assert.Equal(Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(texto))), query["sig"]);
        Assert.DoesNotContain(Chave, acesso.Sas, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chave_trocada_no_portal_e_relida_uma_vez()
    {
        var mensagens = 0;
        var (fila, azure, _) = Criar((pedido, corpo) => pedido.RequestUri!.Host == "management.azure.com"
            ? Chaves()
            : ++mensagens == 1 ? new HttpResponseMessage(HttpStatusCode.Forbidden) : new HttpResponseMessage(HttpStatusCode.Created));

        await fila.EnviarAsync(Fila, "{}", TimeSpan.FromMinutes(15), default);

        Assert.Equal(["management.azure.com", "dkq16abcd1234.queue.core.windows.net", "management.azure.com", "dkq16abcd1234.queue.core.windows.net"],
            azure.Pedidos.Select(p => p.Pedido.RequestUri!.Host));
    }

    [Fact]
    public async Task Cada_falha_sai_em_portugues_sem_a_sas_nem_a_chave()
    {
        var recusa = Criar((pedido, _) => pedido.RequestUri!.Host == "management.azure.com"
            ? Chaves()
            : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Headers = { { "x-ms-error-code", "ServerBusy" } } }).Fila;
        var falha = await Assert.ThrowsAsync<FalhaNaFila>(() => recusa.EnviarAsync(Fila, "{}", TimeSpan.FromMinutes(1), default));
        Assert.Equal("A fila recusou o comando (503 ServerBusy).", falha.Message);

        var semPapel = Criar((pedido, _) => new HttpResponseMessage(HttpStatusCode.Forbidden)).Fila;
        Assert.Contains("listKeys", (await Assert.ThrowsAsync<FalhaNaFila>(() => semPapel.EmitirSasAsync(Fila, Agora.AddMinutes(5), default))).Message);

        var semChave = Criar((pedido, _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"keys":[]}""") }).Fila;
        Assert.Contains("sem chave", (await Assert.ThrowsAsync<FalhaNaFila>(() => semChave.EmitirSasAsync(Fila, Agora.AddMinutes(5), default))).Message);

        var foraDoAr = Criar((_, _) => throw new HttpRequestException("conexão recusada")).Fila;
        Assert.Contains("Sem resposta do Azure", (await Assert.ThrowsAsync<FalhaNaFila>(() => foraDoAr.EmitirSasAsync(Fila, Agora.AddMinutes(5), default))).Message);

        var (semId, azure, _) = Criar();
        await Assert.ThrowsAsync<FalhaNaFila>(() => semId.EmitirSasAsync(Fila with { IdNoAzure = "" }, Agora.AddMinutes(5), default));
        Assert.Empty(azure.Pedidos);
    }

    [Fact]
    public void A_fila_simulada_nao_sobe_em_production_e_a_desconhecida_nao_sobe_em_lugar_nenhum()
    {
        var producao = new HostingEnvironment { EnvironmentName = "Production" };
        var desenvolvimento = new HostingEnvironment { EnvironmentName = "Development" };

        Assert.Throws<InvalidOperationException>(() => FilaDeComandos.Validar(new OpcoesDoRemoto { Fila = OpcoesDoRemoto.FilaSimulada }, producao));
        Assert.Throws<InvalidOperationException>(() => FilaDeComandos.Validar(new OpcoesDoRemoto { Fila = "redis" }, desenvolvimento));
        FilaDeComandos.Validar(new OpcoesDoRemoto { Fila = OpcoesDoRemoto.FilaSimulada }, desenvolvimento);
        FilaDeComandos.Validar(new OpcoesDoRemoto(), producao);
        Assert.IsType<Azure.Identity.ManagedIdentityCredential>(CredencialDoAzure.Criar(null, producao: true));
        Assert.IsType<Azure.Identity.ManagedIdentityCredential>(CredencialDoAzure.Criar("8d1f4c2e-0000-4000-8000-000000000001", producao: false));
    }

    [Fact]
    public async Task A_fila_simulada_guarda_a_mensagem_ate_o_ttl()
    {
        var relogio = new RelogioDeTeste(Agora);
        var simulada = new FilaSimulada(relogio);

        await simulada.EnviarAsync(Fila, "{}", TimeSpan.FromSeconds(60), default);
        Assert.Single(simulada.Mensagens(Fila.Conta));
        Assert.Contains("sp=p", (await simulada.EmitirSasAsync(Fila, Agora.AddMinutes(15), default)).Sas, StringComparison.Ordinal);

        relogio.Agora = Agora.AddSeconds(61);
        Assert.Empty(simulada.Mensagens(Fila.Conta));
    }
}
