using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A API inteira sobre uma base SQLite NOVA (arquivo temporário), com a configuração dos testes
/// passada como setting do host — o mesmo caminho das variáveis de ambiente no Azure.
/// </summary>
public sealed class ApiDeTeste : WebApplicationFactory<Program>
{
    public const string SenhaInicial = "senhaInicial2026";
    public const string NovaSenha = "senhaNova2026x";
    public const string CodigoDeRegistro = "convite-de-teste";
    public const string Segredo = "segredo-dos-testes-com-mais-de-32-caracteres";
    public const int LimitePorMinuto = 3;

    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), $"devkitpage-api-{Guid.NewGuid():N}.db");
    private readonly string _ambiente;
    private readonly bool _comSegredo;
    private readonly string? _ipDaConexao;
    private readonly string? _redeConfiavel;

    /// <param name="ipDaConexao">O IP de quem abre a conexão (o proxy, atrás do Container Apps); nulo é o do TestServer, sem IP.</param>
    /// <param name="redeConfiavel">O <c>Proxy:RedesConfiaveis</c> — de quem o <c>X-Forwarded-For</c> é aceito.</param>
    public ApiDeTeste(string ambiente = "Testing", bool comSegredo = true, string? ipDaConexao = null, string? redeConfiavel = null)
    {
        _ambiente = ambiente;
        _comSegredo = comSegredo;
        _ipDaConexao = ipDaConexao;
        _redeConfiavel = redeConfiavel;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_ambiente);
        if (_redeConfiavel is not null)
            builder.UseSetting("Proxy:RedesConfiaveis:0", _redeConfiavel);
        if (_ipDaConexao is not null)
            builder.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new ConexaoDe(IPAddress.Parse(_ipDaConexao))));
        builder.UseSetting("ConnectionStrings:DevKitPage", $"Data Source={_arquivo}");
        builder.UseSetting("Seed:AdminPassword", SenhaInicial);
        builder.UseSetting("Telemetria:CodigoDeRegistro", CodigoDeRegistro);
        builder.UseSetting("Telemetria:MaxEventosPorLote", "50");
        builder.UseSetting("Jwt:Segredo", _comSegredo ? Segredo : string.Empty);
        builder.UseSetting("Demonstracoes:LimitePorMinuto", LimitePorMinuto.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public async Task<LoginResponse> LoginAsync(HttpClient cliente, string senha = SenhaInicial)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", senha));
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    /// <summary>O admin já com a senha trocada: um cliente que acessa o dashboard.</summary>
    public async Task<HttpClient> AdminAsync()
    {
        var cliente = CreateClient();
        var primeiro = await LoginAsync(cliente);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primeiro.Token);
        var troca = await cliente.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(SenhaInicial, NovaSenha));
        troca.EnsureSuccessStatusCode();
        var novo = (await troca.Content.ReadFromJsonAsync<LoginResponse>())!;
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", novo.Token);
        return cliente;
    }

    /// <summary>Registra uma máquina pelo código (e, com <paramref name="codigoEmpresa"/>, adere à empresa) e devolve um cliente com a chave dela.</summary>
    public async Task<HttpClient> MaquinaAsync(string maquinaId, string? codigoEmpresa = null, string? colaborador = null)
    {
        // A máquina que já foi registrada aqui se registra de novo com a chave atual, como o dev.kit.
        var cliente = CreateClient();
        if (Chaves.TryGetValue(maquinaId, out var atual))
            cliente.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, atual);
        var resposta = await cliente.PostAsJsonAsync("/api/maquinas/registrar", new MachineRegistrationV1(maquinaId, "1.4.0", CodigoDeRegistro, codigoEmpresa, colaborador));
        resposta.EnsureSuccessStatusCode();
        var chave = (await resposta.Content.ReadFromJsonAsync<MachineRegistrationResponseV1>())!.Chave;
        Chaves[maquinaId] = chave;
        cliente.DefaultRequestHeaders.Remove(ContratoV1.CabecalhoDaChave);
        cliente.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, chave);
        return cliente;
    }

    /// <summary>A chave atual de cada máquina registrada por <see cref="MaquinaAsync"/>.</summary>
    public Dictionary<string, string> Chaves { get; } = new(StringComparer.Ordinal);

    /// <summary>O admin cria uma empresa (US #381) e devolve o resumo, com o código de adesão.</summary>
    public static async Task<EmpresaResumo> EmpresaAsync(HttpClient admin, string nome, int assentos = 10)
    {
        var resposta = await admin.PostAsJsonAsync("/api/empresas", new EmpresaNova(nome, "Empresarial", assentos));
        resposta.EnsureSuccessStatusCode();
        return (await resposta.Content.ReadFromJsonAsync<EmpresaResumo>())!;
    }

    /// <summary>
    /// O admin convida o gestor da empresa; o gestor entra com a senha inicial, faz a troca obrigatória
    /// e devolve um cliente que acessa o dashboard da empresa dele.
    /// </summary>
    public async Task<HttpClient> GestorAsync(HttpClient admin, int empresa, string login)
    {
        var convite = await admin.PostAsJsonAsync($"/api/empresas/{empresa}/gestores", new GestorNovo(login));
        convite.EnsureSuccessStatusCode();
        var criado = (await convite.Content.ReadFromJsonAsync<GestorCriado>())!;

        var cliente = CreateClient();
        var primeiro = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, criado.SenhaInicial));
        primeiro.EnsureSuccessStatusCode();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await primeiro.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        var troca = await cliente.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(criado.SenhaInicial, NovaSenha));
        troca.EnsureSuccessStatusCode();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await troca.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        return cliente;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }

    /// <summary>Faz toda requisição chegar deste IP, ANTES do pipeline da API — como o proxy na frente dela.</summary>
    private sealed class ConexaoDe(IPAddress ip) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> proximo) => app =>
        {
            app.Use((http, seguir) =>
            {
                http.Connection.RemoteIpAddress = ip;
                return seguir(http);
            });
            proximo(app);
        };
    }
}
