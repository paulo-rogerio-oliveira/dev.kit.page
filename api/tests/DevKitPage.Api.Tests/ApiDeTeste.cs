using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

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

    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), $"devkitpage-api-{Guid.NewGuid():N}.db");
    private readonly string _ambiente;
    private readonly bool _comSegredo;

    public ApiDeTeste(string ambiente = "Testing", bool comSegredo = true)
    {
        _ambiente = ambiente;
        _comSegredo = comSegredo;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_ambiente);
        builder.UseSetting("ConnectionStrings:DevKitPage", $"Data Source={_arquivo}");
        builder.UseSetting("Seed:AdminPassword", SenhaInicial);
        builder.UseSetting("Telemetria:CodigoDeRegistro", CodigoDeRegistro);
        builder.UseSetting("Telemetria:MaxEventosPorLote", "50");
        builder.UseSetting("Jwt:Segredo", _comSegredo ? Segredo : string.Empty);
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

    /// <summary>Registra uma máquina pelo código e devolve um cliente com a chave dela.</summary>
    public async Task<HttpClient> MaquinaAsync(string maquinaId)
    {
        var cliente = CreateClient();
        var resposta = await cliente.PostAsJsonAsync("/api/maquinas/registrar", new MachineRegistrationV1(maquinaId, "1.4.0", CodigoDeRegistro));
        resposta.EnsureSuccessStatusCode();
        var chave = (await resposta.Content.ReadFromJsonAsync<MachineRegistrationResponseV1>())!.Chave;
        cliente.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, chave);
        return cliente;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }
}
