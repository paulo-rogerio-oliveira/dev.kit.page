using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

    /// <summary>O PAT de mentira das releases (o <c>REPO_KEY</c> dos testes): nunca pode sair da API.</summary>
    public const string TokenDoGitHub = "ghp_token-de-teste-que-nao-pode-vazar";

    /// <summary>A semente Ed25519 da licença nos testes (US #425) — o vetor 1 do RFC 8032, e nunca a de produção.</summary>
    public static readonly byte[] SementeDaLicenca = Convert.FromHexString("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");

    /// <summary>O segredo do webhook do gateway nos testes (US #425).</summary>
    public const string SegredoDoWebhook = "segredo-do-webhook-de-teste";

    /// <summary>A raiz do site de onde sai o link de definir a senha nos testes (US #425).</summary>
    public const string UrlDoSite = "https://site.test";

    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), $"devkitpage-api-{Guid.NewGuid():N}.db");
    private readonly string _ambiente;
    private readonly bool _comSegredo;
    private readonly string? _ipDaConexao;
    private readonly string? _redeConfiavel;
    private readonly string? _repoKey;
    private readonly bool _loginObrigatorio;
    private readonly bool _comLicenca;
    private readonly int _downloadsPorHora;

    /// <param name="ipDaConexao">O IP de quem abre a conexão (o proxy, atrás do Container Apps); nulo é o do TestServer, sem IP.</param>
    /// <param name="redeConfiavel">O <c>Proxy:RedesConfiaveis</c> — de quem o <c>X-Forwarded-For</c> é aceito.</param>
    /// <param name="repoKey">O <c>REPO_KEY</c> (US #405); nulo é a API sem ele — a atualização responde 503.</param>
    /// <param name="loginObrigatorio">O <c>DEVKIT_LOGIN_OBRIGATORIO</c> (US #405).</param>
    /// <param name="comLicenca">A chave privada da licença configurada (US #425); sem ela, as rotas de licença respondem 503.</param>
    /// <param name="downloadsPorHora">O limite de downloads anônimos por IP (US #425).</param>
    public ApiDeTeste(
        string ambiente = "Testing", bool comSegredo = true, string? ipDaConexao = null, string? redeConfiavel = null,
        string? repoKey = TokenDoGitHub, bool loginObrigatorio = false, bool comLicenca = true, int downloadsPorHora = 100)
    {
        _ambiente = ambiente;
        _comSegredo = comSegredo;
        _ipDaConexao = ipDaConexao;
        _redeConfiavel = redeConfiavel;
        _repoKey = repoKey;
        _loginObrigatorio = loginObrigatorio;
        _comLicenca = comLicenca;
        _downloadsPorHora = downloadsPorHora;
    }

    /// <summary>O GitHub de mentira por trás do HttpClient das releases: nenhum teste sai para a rede.</summary>
    public GitHubDeMentira GitHub { get; } = new();

    /// <summary>Os e-mails que a API "enviou" (US #425): o falso guarda as mensagens, e o teste lê o link.</summary>
    public EmailsDeTeste Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_ambiente);
        // As variáveis de ambiente da US #405, como chegam no Azure — vazias quando o teste não as quer
        // (uma REPO_KEY na máquina de quem roda o teste não muda o resultado).
        builder.UseSetting("REPO_KEY", _repoKey ?? string.Empty);
        builder.UseSetting("DEVKIT_LOGIN_OBRIGATORIO", _loginObrigatorio ? "true" : string.Empty);
        builder.ConfigureTestServices(s =>
        {
            s.AddHttpClient(ReleasesDoDevKit.NomeDoCliente).ConfigurePrimaryHttpMessageHandler(() => GitHub);
            s.AddSingleton<DevKitPage.Core.IEnvioDeEmail>(Emails);
        });
        // A US #425: a licença assinada com a semente de teste, o webhook do gateway, o site do link e os limites
        // largos (os testes do limite usam os seus).
        builder.UseSetting("Licencas:ChavePrivada", _comLicenca ? Convert.ToBase64String(SementeDaLicenca) : string.Empty);
        builder.UseSetting("Pagamentos:SegredoDoWebhook", SegredoDoWebhook);
        builder.UseSetting("Pagamentos:UrlDoPortal", "https://gateway.test/portal");
        builder.UseSetting("Conta:UrlDoSite", UrlDoSite);
        builder.UseSetting("Conta:LimitePorMinuto", "100");
        builder.UseSetting("Atualizacao:DownloadsPorHora", _downloadsPorHora.ToString(System.Globalization.CultureInfo.InvariantCulture));
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

    /// <summary>
    /// O admin cria um usuário (US #405) pela gestão; o usuário entra com a senha temporária, faz a troca
    /// obrigatória e volta como um cliente autenticado (com o login devolvido pela troca).
    /// </summary>
    public async Task<(HttpClient Cliente, LoginResponse Login)> UsuarioAsync(HttpClient admin, string login, string papel, int? empresa = null)
    {
        var criacao = await admin.PostAsJsonAsync("/api/usuarios", new UsuarioNovo(login, $"Nome de {login}", papel, empresa));
        criacao.EnsureSuccessStatusCode();
        var criado = (await criacao.Content.ReadFromJsonAsync<UsuarioComSenha>())!;

        var cliente = CreateClient();
        var primeiro = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, criado.SenhaTemporaria));
        primeiro.EnsureSuccessStatusCode();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await primeiro.Content.ReadFromJsonAsync<LoginResponse>())!.Token);
        var troca = await cliente.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(criado.SenhaTemporaria, NovaSenha));
        troca.EnsureSuccessStatusCode();
        var novo = (await troca.Content.ReadFromJsonAsync<LoginResponse>())!;
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", novo.Token);
        return (cliente, novo);
    }

    /// <summary>
    /// O dev que ADERE pelo código e e-mail (US #425): a adesão, o link do e-mail, a senha definida e o login — o
    /// cliente volta autenticado (sem troca pendente: a senha é dele desde o início).
    /// </summary>
    public async Task<(HttpClient Cliente, LoginResponse Login)> DevPorAdesaoAsync(string codigo, string email)
    {
        var anonimo = CreateClient();
        (await anonimo.PostAsJsonAsync("/api/conta/adesao", new AdesaoV1(codigo, email, $"Nome de {email}"))).EnsureSuccessStatusCode();
        (await anonimo.PostAsJsonAsync("/api/conta/definir-senha", new DefinirSenhaV1(Emails.TokenPara(email), NovaSenha))).EnsureSuccessStatusCode();

        var cliente = CreateClient();
        var login = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, NovaSenha));
        login.EnsureSuccessStatusCode();
        var resposta = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", resposta.Token);
        return (cliente, resposta);
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
