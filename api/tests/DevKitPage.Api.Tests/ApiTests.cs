using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A API de ponta a ponta no processo (WebApplicationFactory): o login e a troca obrigatória do
/// admin, as duas autenticações separadas, a ingestão idempotente, os limites e as consultas.
/// </summary>
public sealed class ApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static readonly DateTimeOffset Hoje = DateTimeOffset.UtcNow;

    private static TelemetryBatchV1 Lote(string maquina, params TelemetryEventV1[] eventos) => new("v1", maquina, "1.4.0", eventos);

    private static TelemetryEventV1 Evento(string id, string tipo, long? valor = null, string detalhe = "", int quantidade = 1)
        => new(id, tipo, "s1", quantidade, valor, detalhe, Hoje);

    [Fact]
    public async Task Login_valido_devolve_token_e_invalido_e_401()
    {
        var cliente = _api.CreateClient();

        var ok = await _api.LoginAsync(cliente);
        var errado = await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "nao-e-essa"));

        Assert.False(string.IsNullOrWhiteSpace(ok.Token));
        Assert.True(ok.DeveTrocarSenha);
        Assert.True(ok.ExpiraEm > DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, errado.StatusCode);
    }

    [Fact]
    public async Task Rota_sem_token_e_401()
    {
        var resposta = await _api.CreateClient().GetAsync("/api/dashboard/maquinas");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Admin_semeado_so_acessa_a_troca_de_senha_e_depois_o_dashboard()
    {
        var cliente = _api.CreateClient();
        var primeiro = await _api.LoginAsync(cliente);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primeiro.Token);

        Assert.Equal(HttpStatusCode.Forbidden, (await cliente.GetAsync("/api/dashboard/maquinas")).StatusCode);

        var fraca = await cliente.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(ApiDeTeste.SenhaInicial, "curta"));
        Assert.Equal(HttpStatusCode.BadRequest, fraca.StatusCode);

        var troca = await cliente.PostAsJsonAsync("/api/auth/trocar-senha", new TrocarSenhaRequest(ApiDeTeste.SenhaInicial, ApiDeTeste.NovaSenha));
        troca.EnsureSuccessStatusCode();
        var novo = (await troca.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.False(novo.DeveTrocarSenha);

        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", novo.Token);
        Assert.Equal(HttpStatusCode.OK, (await cliente.GetAsync("/api/dashboard/maquinas")).StatusCode);

        // A senha antiga não entra mais; a nova entra sem troca pendente.
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", ApiDeTeste.SenhaInicial))).StatusCode);
        Assert.False((await _api.LoginAsync(_api.CreateClient(), ApiDeTeste.NovaSenha)).DeveTrocarSenha);
    }

    [Fact]
    public async Task Token_expirado_e_401()
    {
        var opcoes = _api.Services.GetRequiredService<IOptions<OpcoesDeAutenticacao>>();
        var duasHorasAtras = new RelogioFixo(DateTimeOffset.UtcNow.AddHours(-2));
        var vencido = new EmissorDeToken(opcoes, duasHorasAtras).Emitir(new Usuario { Id = 1, Login = "admin", EhAdmin = true });
        var cliente = _api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", vencido.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.GetAsync("/api/dashboard/maquinas")).StatusCode);
    }

    [Fact]
    public async Task Maquina_registrada_envia_e_o_reenvio_do_mesmo_lote_nao_altera_os_totais()
    {
        var maquina = await _api.MaquinaAsync("maq-1");
        var lote = Lote("maq-1",
            Evento("e1", TiposDeEvento.SessaoIniciada, detalhe: "1.4.0"),
            Evento("e2", TiposDeEvento.TurnoExecutado, 6000, "claude"),
            Evento("e3", TiposDeEvento.TurnoExecutado, 2000, "claude"),
            Evento("e4", TiposDeEvento.TurnoFalhou, detalhe: "sem-autenticacao"),
            Evento("e5", TiposDeEvento.ArquivoAlterado, quantidade: 4),
            Evento("e6", TiposDeEvento.ObjetivoAvaliado, 92),
            Evento("e7", TiposDeEvento.ObjetivoCumprido),
            Evento("e8", "TipoQueEstaVersaoNaoConhece"));

        var primeiro = await maquina.PostAsJsonAsync("/api/telemetria/lote", lote);
        var segundo = await maquina.PostAsJsonAsync("/api/telemetria/lote", lote);

        Assert.Equal(HttpStatusCode.Accepted, primeiro.StatusCode);
        Assert.Equal(new BatchResultV1(8, 7, 0, 1), await primeiro.Content.ReadFromJsonAsync<BatchResultV1>());
        Assert.Equal(new BatchResultV1(8, 0, 7, 1), await segundo.Content.ReadFromJsonAsync<BatchResultV1>());

        var admin = await _api.AdminAsync();
        var quantidade = (await admin.GetFromJsonAsync<QuantidadeResposta>("/api/dashboard/quantidade"))!;
        var qualidade = (await admin.GetFromJsonAsync<QualidadeResposta>("/api/dashboard/qualidade"))!;
        var maquinas = (await admin.GetFromJsonAsync<MaquinaResumo[]>("/api/dashboard/maquinas"))!;
        var eventos = (await admin.GetFromJsonAsync<Pagina<EventoDoLog>>($"/api/dashboard/eventos?maquina={maquinas[0].Id}&tamanho=5"))!;

        Assert.Equal((1L, 2L, 4L), (quantidade.Sessoes, quantidade.Turnos, quantidade.ArquivosAlterados));
        Assert.Equal(0.5, qualidade.TaxaDeFalha);
        Assert.Equal(4000, qualidade.DuracaoMediaDoTurnoMs);
        Assert.Equal(92, qualidade.NotaMedia);
        Assert.Equal(new CausaDeFalha("sem-autenticacao", 1), Assert.Single(qualidade.FalhasPorCausa));
        Assert.Equal("maq-1", Assert.Single(maquinas).MaquinaId);
        Assert.Equal(7, eventos.Total);
        Assert.Equal(5, eventos.Itens.Count);
    }

    [Fact]
    public async Task Ingestao_recusa_chave_invalida_e_jwt_de_usuario()
    {
        var comChaveErrada = _api.CreateClient();
        comChaveErrada.DefaultRequestHeaders.Add(ContratoV1.CabecalhoDaChave, "chave-que-nao-existe");
        var admin = await _api.AdminAsync();
        var lote = Lote("maq-1", Evento("e1", TiposDeEvento.SessaoIniciada));

        Assert.Equal(HttpStatusCode.Unauthorized, (await comChaveErrada.PostAsJsonAsync("/api/telemetria/lote", lote)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.PostAsJsonAsync("/api/telemetria/lote", lote)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().PostAsJsonAsync("/api/telemetria/lote", lote)).StatusCode);
    }

    [Fact]
    public async Task Chave_de_maquina_nao_le_o_dashboard()
    {
        var maquina = await _api.MaquinaAsync("maq-1");

        Assert.Equal(HttpStatusCode.Unauthorized, (await maquina.GetAsync("/api/dashboard/maquinas")).StatusCode);
    }

    [Fact]
    public async Task Lote_grande_e_413_e_versao_desconhecida_e_400()
    {
        var maquina = await _api.MaquinaAsync("maq-1");
        var grande = Lote("maq-1", Enumerable.Range(1, 51).Select(i => Evento($"g{i}", TiposDeEvento.FerramentaAcionada, detalhe: "Bash")).ToArray());
        var v2 = new TelemetryBatchV1("v2", "maq-1", "1.4.0", new[] { Evento("x", TiposDeEvento.SessaoIniciada) });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await maquina.PostAsJsonAsync("/api/telemetria/lote", grande)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await maquina.PostAsJsonAsync("/api/telemetria/lote", v2)).StatusCode);
    }

    [Fact]
    public async Task Registro_sem_codigo_e_401_e_o_admin_registra_sem_codigo()
    {
        var anonimo = await _api.CreateClient().PostAsJsonAsync("/api/maquinas/registrar", new MachineRegistrationV1("maq-2", "1.4.0", "errado"));
        var admin = await _api.AdminAsync();
        var peloAdmin = await admin.PostAsJsonAsync("/api/maquinas/registrar", new MachineRegistrationV1("maq-2", "1.4.0", string.Empty));

        Assert.Equal(HttpStatusCode.Unauthorized, anonimo.StatusCode);
        Assert.Equal(HttpStatusCode.OK, peloAdmin.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace((await peloAdmin.Content.ReadFromJsonAsync<MachineRegistrationResponseV1>())!.Chave));
    }

    [Fact]
    public async Task Health_responde_com_o_banco()
    {
        var resposta = await _api.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("Healthy", await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Api_recusa_subir_em_production_sem_segredo_jwt()
    {
        using var semSegredo = new ApiDeTeste("Production", comSegredo: false);

        var erro = Assert.ThrowsAny<Exception>(() => semSegredo.CreateClient());

        Assert.Contains("Jwt:Segredo", erro.ToString());
    }

    /// <summary>Um relógio parado num instante.</summary>
    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}
