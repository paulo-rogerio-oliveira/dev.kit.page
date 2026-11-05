using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A conversa e os comandos remotos pela API (<c>/api/remoto</c>, fase 2): o site manda o comando — gravado no SQL e na fila
/// com id, destino e TTL —, o dev.kit pede a SAS curta, confirma o recebimento e responde por HTTPS, e o site recebe pelo SSE
/// (retomado pelo <c>Last-Event-ID</c>). E as recusas: sem JWT, o comando e a máquina de outro usuário (404), sem fila, o
/// reinício sem confirmação, o dado sensível, o limite por usuário com o <c>Retry-After</c> e a fila fora do ar.
/// </summary>
public sealed class ConversaApiTests : IDisposable
{
    private const string Maquina = "3f2b9c0e5d7a";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiDeTeste _api;

    public ConversaApiTests() : this(new ApiDeTeste()) { }

    private ConversaApiTests(ApiDeTeste api) => _api = api;

    public void Dispose() => _api.Dispose();

    private static HeartbeatV1 Heartbeat(bool turno = false)
        => new(RemotoV1.Contrato, Maquina, "Notebook do trabalho", "75", RemotoV1.OrigemServico, true, RemotoV1.AgenteAtivo, turno);

    private static ComandoNovoV1 Mensagem(string id, string texto = "Como está o projeto controle-remoto-v2?", int? ttl = null)
        => new(id, Maquina, ConversaV1.ComandoMensagem, texto, ttl, ConversaV1.DispositivoCelular);

    private static RespostaDoDevKitV1 Resposta(string id, string? comando, string tipo = ConversaV1.RespostaMensagem, string texto = "O item 5 está em implementação.")
        => new(RemotoV1.Contrato, Maquina, id, comando, tipo, texto, DateTimeOffset.UtcNow);

    /// <summary>
    /// Um dev com a fila PRONTA, pelo caminho de verdade: o admin libera, o dev pede a fila e o motor do worker a conclui — e a
    /// máquina já mandou o heartbeat.
    /// </summary>
    private async Task<(HttpClient Admin, HttpClient Dev)> DevComFilaAsync(string login = "ana", HttpClient? admin = null)
    {
        admin ??= await _api.AdminAsync();
        var (dev, _) = await _api.UsuarioAsync(admin, login, Papeis.Dev);
        var id = (await admin.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == login).Id;
        (await admin.PutAsJsonAsync($"/api/usuarios/{id}/recursos/{ProvisaoV1.RecursoProvisao}", new LiberacaoDeRecursoV1(true))).EnsureSuccessStatusCode();
        var pedido = (await (await dev.PostAsJsonAsync("/api/provisao/filas", new PedidoDeFilaV1(null))).Content.ReadFromJsonAsync<RecursoProvisionadoV1>())!;
        using (var escopo = _api.Services.CreateScope())
        {
            var motor = escopo.ServiceProvider.GetRequiredService<IMotorDeProvisao>();
            var reservado = (await motor.ReservarAsync("worker-de-teste", default))!;
            Assert.Equal(pedido.Id, reservado.Id);
            Assert.True(await motor.ConcluirAsync(reservado.Id, "worker-de-teste", new RecursoNaNuvem(
                $"/subscriptions/3f2b9c0e-5d7a-4c1b-9e2f-1a2b3c4d5e6f/resourceGroups/{pedido.GrupoDeRecursos}/providers/Microsoft.Storage/storageAccounts/{pedido.Nome}",
                $"https://{pedido.Nome}.queue.core.windows.net/comandos"), default));
        }
        (await dev.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).EnsureSuccessStatusCode();
        return (admin, dev);
    }

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;
    }

    /// <summary>Um evento lido do SSE: o <c>id:</c>, o <c>event:</c> e o <c>data:</c>.</summary>
    private sealed record Sse(string? Id, string Evento, string Dados);

    /// <summary>
    /// Lê o SSE até <paramref name="basta"/> dizer que chegou o que o teste espera, o fluxo fechar ou o tempo acabar (o que veio
    /// até ali volta).
    /// </summary>
    private static async Task<List<Sse>> LerSseAsync(HttpResponseMessage resposta, Func<List<Sse>, bool> basta, CancellationToken ct)
    {
        var lidos = new List<Sse>();
        using var leitor = new StreamReader(await resposta.Content.ReadAsStreamAsync(ct));
        string? id = null, evento = null;
        var dados = new StringBuilder();
        while (!basta(lidos))
        {
            string? linha;
            try
            {
                linha = await leitor.ReadLineAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException) when (ct.IsCancellationRequested)
            {
                // O TestServer diz o cancelamento do cliente assim ("The client aborted the request").
                break;
            }
            if (linha is null)
                break;
            if (linha.Length == 0)
            {
                if (evento is not null || dados.Length > 0)
                    lidos.Add(new Sse(id, evento ?? "message", dados.ToString()));
                (id, evento) = (null, null);
                dados.Clear();
            }
            else if (linha.StartsWith("id: ", StringComparison.Ordinal))
                id = linha[4..];
            else if (linha.StartsWith("event: ", StringComparison.Ordinal))
                evento = linha[7..];
            else if (linha.StartsWith("data: ", StringComparison.Ordinal))
                dados.Append(linha[6..]);
        }
        return lidos;
    }

    private static Task<HttpResponseMessage> AbrirSseAsync(HttpClient cliente, string url, long? ultimoEvento, CancellationToken ct)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Get, url);
        pedido.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (ultimoEvento is { } id)
            pedido.Headers.Add("Last-Event-ID", id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return cliente.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    [Fact]
    public async Task Sem_jwt_ou_com_troca_pendente_nada_entra()
    {
        var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/remoto/comandos/c-1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/remoto/maquinas/{Maquina}/conversa")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/remoto/conversa/eventos")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/remoto/fila/sas", new PedidoDeSasV1(RemotoV1.Contrato, Maquina))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/remoto/comandos/c-1/recebido", new RecebimentoV1(RemotoV1.Contrato, Maquina))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/remoto/respostas", Resposta("r-1", null))).StatusCode);

        var pendente = _api.CreateClient();
        pendente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await _api.LoginAsync(pendente)).Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendente.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendente.GetAsync("/api/remoto/conversa/eventos")).StatusCode);
    }

    [Fact]
    public async Task Ida_e_volta_o_site_manda_o_dev_kit_recebe_e_responde_e_o_historico_mostra()
    {
        var (_, ana) = await DevComFilaAsync();

        var envio = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1", ttl: 120));
        Assert.Equal(HttpStatusCode.Accepted, envio.StatusCode);
        Assert.Equal("/api/remoto/comandos/c-1", envio.Headers.Location!.OriginalString);
        var comando = (await envio.Content.ReadFromJsonAsync<ComandoRemotoV1>())!;
        Assert.Equal((ConversaV1.EstadoNaFila, Maquina, ConversaV1.DispositivoCelular), (comando.Estado, comando.MaquinaId, comando.Dispositivo));
        Assert.Equal(TimeSpan.FromSeconds(120), comando.ExpiraEm - comando.CriadoEm);

        // Na fila: o id, o destino, o TTL e a origem remota.
        var (_, texto, ttl) = Assert.Single(_api.Fila.Enviadas);
        var mensagem = JsonSerializer.Deserialize<MensagemDaFilaV1>(texto, Json)!;
        Assert.Equal(("c-1", Maquina, ConversaV1.OrigemRemoto, comando.ExpiraEm), (mensagem.Id, mensagem.MaquinaId, mensagem.Origem, mensagem.ExpiraEm));
        // O TTL da fila é o que FALTA do comando (os milissegundos da gravação já correram).
        Assert.InRange(ttl, TimeSpan.FromSeconds(115), TimeSpan.FromSeconds(120));

        // O reenvio do site (o duplo toque no celular) devolve o mesmo comando, sem outra mensagem.
        var reenvio = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1", ttl: 120));
        Assert.Equal(HttpStatusCode.OK, reenvio.StatusCode);
        Assert.Single(_api.Fila.Enviadas);

        // O dev.kit: a SAS curta só de consumo, o recebimento (o primeiro executa, o repetido não) e a resposta.
        var sas = (await (await ana.PostAsJsonAsync("/api/remoto/fila/sas", new PedidoDeSasV1(RemotoV1.Contrato, Maquina))).Content.ReadFromJsonAsync<SasDaFilaV1>())!;
        Assert.Equal(ConversaV1.PermissaoDaSas, sas.Permissoes);
        Assert.InRange(sas.ExpiraEm - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
        Assert.EndsWith(".queue.core.windows.net/comandos", sas.Endpoint, StringComparison.Ordinal);

        var recebido = (await (await ana.PostAsJsonAsync("/api/remoto/comandos/c-1/recebido", new RecebimentoV1(RemotoV1.Contrato, Maquina))).Content.ReadFromJsonAsync<RecebimentoAceitoV1>())!;
        var repetido = (await (await ana.PostAsJsonAsync("/api/remoto/comandos/c-1/recebido", new RecebimentoV1(RemotoV1.Contrato, Maquina))).Content.ReadFromJsonAsync<RecebimentoAceitoV1>())!;
        Assert.Equal((ConversaV1.EstadoEntregue, true, false), (recebido.Estado, recebido.Executar, recebido.Repetido));
        Assert.Equal((false, true), (repetido.Executar, repetido.Repetido));

        var resposta = await ana.PostAsJsonAsync("/api/remoto/respostas", Resposta("r-1", "c-1"));
        var reenviada = await ana.PostAsJsonAsync("/api/remoto/respostas", Resposta("r-1", "c-1"));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reenviada.StatusCode);
        Assert.True((await reenviada.Content.ReadFromJsonAsync<RespostaRecebidaV1>())!.Repetida);

        Assert.Equal(ConversaV1.EstadoRespondido, (await ana.GetFromJsonAsync<ComandoRemotoV1>("/api/remoto/comandos/c-1"))!.Estado);
        var conversa = (await ana.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!;
        Assert.Equal(["c-1"], conversa.Comandos.Select(c => c.Id));
        Assert.Equal(("r-1", "c-1", "O item 5 está em implementação."), (conversa.Respostas.Single().Id, conversa.Respostas.Single().ComandoId, conversa.Respostas.Single().Texto));
        Assert.True(conversa.UltimoEvento > 0);
    }

    [Fact]
    public async Task O_sse_empurra_o_que_muda_na_hora_e_o_last_event_id_retoma_sem_repetir()
    {
        var (_, ana) = await DevComFilaAsync();
        (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).EnsureSuccessStatusCode();
        var antes = (await ana.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!.UltimoEvento;

        using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var fluxo = await AbrirSseAsync(ana, $"/api/remoto/conversa/eventos?maquinaId={Maquina}", antes, tempo.Token);
        Assert.Equal(HttpStatusCode.OK, fluxo.StatusCode);
        Assert.Equal("text/event-stream", fluxo.Content.Headers.ContentType!.MediaType);

        // Com o SSE aberto, o dev.kit recebe e responde: o site vê as duas mudanças sem perguntar.
        var leitura = LerSseAsync(fluxo, l => l.Count(e => e.Evento != "ping") >= 3, tempo.Token);
        (await ana.PostAsJsonAsync("/api/remoto/comandos/c-1/recebido", new RecebimentoV1(RemotoV1.Contrato, Maquina))).EnsureSuccessStatusCode();
        (await ana.PostAsJsonAsync("/api/remoto/respostas", Resposta("r-1", "c-1"))).EnsureSuccessStatusCode();
        var eventos = (await leitura).Where(e => e.Evento != "ping").ToList();

        // O c-1 "na-fila" de antes do cursor não volta.
        Assert.Equal([ConversaV1.EventoComando, ConversaV1.EventoResposta, ConversaV1.EventoComando], eventos.Select(e => e.Evento));
        var lidos = eventos.Select(e => JsonSerializer.Deserialize<EventoDaConversaV1>(e.Dados, Json)!).ToList();
        Assert.Equal([ConversaV1.EstadoEntregue, null, ConversaV1.EstadoRespondido], lidos.Select(e => e.Comando?.Estado));
        Assert.Equal("r-1", lidos[1].Resposta!.Id);
        Assert.Equal(eventos.Select(e => e.Id), lidos.Select(e => e.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.All(lidos, e => Assert.True(e.Id > antes));
        Assert.Equal(lidos.Select(e => e.Id).Order(), lidos.Select(e => e.Id));

        // Reconectou depois do segundo: só o terceiro vem.
        using var tempo2 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var reconexao = await AbrirSseAsync(ana, "/api/remoto/conversa/eventos", lidos[1].Id, tempo2.Token);
        var retomados = await LerSseAsync(reconexao, l => l.Any(e => e.Evento != "ping"), tempo2.Token);
        Assert.Equal(lidos[2].Id.ToString(System.Globalization.CultureInfo.InvariantCulture), retomados.First(e => e.Evento != "ping").Id);
    }

    [Fact]
    public async Task O_comando_vencido_volta_expirado_pelo_sse_e_nao_executa()
    {
        var (_, ana) = await DevComFilaAsync();
        using (var escopo = _api.Services.CreateScope())
        {
            // O TTL mínimo é de 60 s: o comando nasce já vencido direto no banco, como se a máquina tivesse ficado desligada.
            (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-vence", ttl: 60))).EnsureSuccessStatusCode();
            var db = escopo.ServiceProvider.GetRequiredService<DevKitPage.Infrastructure.DevKitPageDb>();
            var comando = db.ComandosRemotos.Single(c => c.ComandoId == "c-vence");
            comando.ExpiraEmUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }

        using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var fluxo = await AbrirSseAsync(ana, "/api/remoto/conversa/eventos", null, tempo.Token);
        var eventos = await LerSseAsync(fluxo, l => l.Any(e => e.Dados.Contains(ConversaV1.EstadoExpirado, StringComparison.Ordinal)), tempo.Token);

        var expirado = JsonSerializer.Deserialize<EventoDaConversaV1>(eventos.Last().Dados, Json)!;
        Assert.Equal(("c-vence", ConversaV1.EstadoExpirado), (expirado.Comando!.Id, expirado.Comando.Estado));
        var tardio = (await (await ana.PostAsJsonAsync("/api/remoto/comandos/c-vence/recebido", new RecebimentoV1(RemotoV1.Contrato, Maquina))).Content.ReadFromJsonAsync<RecebimentoAceitoV1>())!;
        Assert.Equal((ConversaV1.EstadoExpirado, false), (tardio.Estado, tardio.Executar));
    }

    [Fact]
    public async Task Outro_usuario_nao_ve_o_comando_a_conversa_nem_os_eventos_e_nao_recebe_por_ele()
    {
        var (admin, ana) = await DevComFilaAsync();
        (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).EnsureSuccessStatusCode();
        var (bia, _) = await _api.UsuarioAsync(admin, "bia", Papeis.Dev);

        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync("/api/remoto/comandos/c-1")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync($"/api/remoto/maquinas/{Maquina}/conversa")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PostAsJsonAsync("/api/remoto/comandos/c-1/recebido", new RecebimentoV1(RemotoV1.Contrato, Maquina))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-2"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PostAsJsonAsync("/api/remoto/fila/sas", new PedidoDeSasV1(RemotoV1.Contrato, Maquina))).StatusCode);

        // A bia com a mesma id anônima de máquina, mas sem fila: 409 sem-fila — a fila da ana não serve para ela.
        (await bia.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).EnsureSuccessStatusCode();
        var semFila = await bia.PostAsJsonAsync("/api/remoto/fila/sas", new PedidoDeSasV1(RemotoV1.Contrato, Maquina));
        Assert.Equal((HttpStatusCode.Conflict, ConversaV1.CodigoSemFila), (semFila.StatusCode, await CodigoAsync(semFila)));
        var envio = await bia.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"));
        Assert.Equal((HttpStatusCode.Conflict, ConversaV1.CodigoSemFila), (envio.StatusCode, await CodigoAsync(envio)));

        // O SSE da bia não traz nada da ana.
        using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        using var fluxo = await AbrirSseAsync(bia, "/api/remoto/conversa/eventos", null, tempo.Token);
        Assert.Equal(HttpStatusCode.OK, fluxo.StatusCode);
        Assert.DoesNotContain(await LerSseAsync(fluxo, _ => false, tempo.Token), e => e.Evento != "ping");
        // E o mesmo SSE da ana traz o comando dela.
        using var tempoDaAna = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var fluxoDaAna = await AbrirSseAsync(ana, "/api/remoto/conversa/eventos", null, tempoDaAna.Token);
        Assert.Contains(await LerSseAsync(fluxoDaAna, l => l.Any(e => e.Evento != "ping"), tempoDaAna.Token), e => e.Evento == ConversaV1.EventoComando);
        Assert.Single(_api.Fila.Enviadas);
    }

    [Fact]
    public async Task As_recusas_da_forma_do_dado_sensivel_do_reinicio_e_do_contrato()
    {
        var (_, ana) = await DevComFilaAsync();

        var semTexto = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1", texto: " "));
        Assert.Equal(HttpStatusCode.BadRequest, semTexto.StatusCode);
        var ttl = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1", ttl: 5));
        Assert.Equal(HttpStatusCode.BadRequest, ttl.StatusCode);
        Assert.Contains("ttlSegundos", await ttl.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var sensivel = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1", texto: "use o token ghp_abcdefghijklmnopqrstuvwxyz0123456789"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, RemotoV1.CodigoDadoSensivel), (sensivel.StatusCode, await CodigoAsync(sensivel)));
        Assert.DoesNotContain("ghp_", await sensivel.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var resposta = await ana.PostAsJsonAsync("/api/remoto/respostas", Resposta("r-1", null, texto: "O cliente 11.222.333/0001-81 pediu"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, RemotoV1.CodigoDadoSensivel), (resposta.StatusCode, await CodigoAsync(resposta)));
        var contrato = await ana.PostAsJsonAsync("/api/remoto/respostas", Resposta("r-1", null) with { Contrato = 2 });
        Assert.Equal((HttpStatusCode.BadRequest, RemotoV1.CodigoContratoDesconhecido), (contrato.StatusCode, await CodigoAsync(contrato)));
        var naoJson = await ana.PostAsync("/api/remoto/comandos", new StringContent("oi", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, naoJson.StatusCode);

        // O reinício seguro: com o turno em andamento, só com a confirmação.
        (await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat(turno: true))).EnsureSuccessStatusCode();
        var reiniciar = await ana.PostAsJsonAsync("/api/remoto/comandos", new ComandoNovoV1("c-r", Maquina, ConversaV1.ComandoReiniciar));
        Assert.Equal((HttpStatusCode.Conflict, ConversaV1.CodigoTurnoEmAndamento), (reiniciar.StatusCode, await CodigoAsync(reiniciar)));
        var confirmado = await ana.PostAsJsonAsync("/api/remoto/comandos", new ComandoNovoV1("c-r", Maquina, ConversaV1.ComandoReiniciar, ConfirmarReinicio: true));
        Assert.Equal(HttpStatusCode.Accepted, confirmado.StatusCode);

        // O mesmo id com outro conteúdo.
        var emUso = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-r"));
        Assert.Equal((HttpStatusCode.Conflict, ConversaV1.CodigoIdEmUso), (emUso.StatusCode, await CodigoAsync(emUso)));
        Assert.Single(_api.Fila.Enviadas);
    }

    [Fact]
    public async Task A_fila_fora_do_ar_e_503_com_o_comando_falhou_e_o_reenvio_entrega()
    {
        var (_, ana) = await DevComFilaAsync();
        _api.Fila.Falhar = true;

        var falhou = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"));

        Assert.Equal((HttpStatusCode.ServiceUnavailable, ConversaV1.CodigoFilaIndisponivel), (falhou.StatusCode, await CodigoAsync(falhou)));
        using (var json = JsonDocument.Parse(await falhou.Content.ReadAsStringAsync()))
            Assert.Equal(ConversaV1.EstadoFalhou, json.RootElement.GetProperty("comando").GetProperty("estado").GetString());

        _api.Fila.Falhar = false;
        var deNovo = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"));
        Assert.Equal(HttpStatusCode.OK, deNovo.StatusCode);
        Assert.Equal(ConversaV1.EstadoNaFila, (await deNovo.Content.ReadFromJsonAsync<ComandoRemotoV1>())!.Estado);
        Assert.Single(_api.Fila.Enviadas);
    }

    [Fact]
    public async Task O_limite_por_usuario_e_429_com_o_codigo_e_o_retry_after_e_nao_pega_o_outro_usuario()
    {
        using var teste = new ConversaApiTests(new ApiDeTeste(comandosPorHora: 2));
        var api = teste._api;
        var (admin, ana) = await teste.DevComFilaAsync("ana");
        var (_, bia) = await teste.DevComFilaAsync("bia", admin);

        Assert.Equal(HttpStatusCode.Accepted, (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-2"))).StatusCode);
        var acima = await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-3"));

        Assert.Equal((HttpStatusCode.TooManyRequests, ConversaV1.CodigoLimiteDeEnvio), (acima.StatusCode, await CodigoAsync(acima)));
        Assert.InRange(int.Parse(acima.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture), 3500, 3600);
        // O reenvio não conta, e a cota é de cada usuário.
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await bia.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).StatusCode);
        Assert.Equal(3, api.Fila.Enviadas.Count);
    }

    private static FalaDoDevKitV1 Fala(string id, string autor = ConversaV1.AutorDev, string origem = ConversaV1.OrigemDesktop,
        string texto = "Como está o item 13?", DateTimeOffset? ditaEm = null, string? comando = null)
        => new(RemotoV1.Contrato, Maquina, id, autor, origem, texto, ditaEm ?? DateTimeOffset.UtcNow, comando);

    [Fact]
    public async Task O_heartbeat_publica_o_recurso_das_falas_e_o_envio_do_dev_kit_antigo_segue_aceito()
    {
        var (_, ana) = await DevComFilaAsync();

        // O heartbeat do dev.kit antigo, sem campo novo nenhum: 200, e a resposta traz o sinal de suporte.
        var antigo = await ana.PostAsync("/api/remoto/heartbeat", new StringContent(
            $$"""{"contrato":1,"maquinaId":"{{Maquina}}","nome":"Notebook","versaoDevKit":"75","origem":"servico","appAberto":true,"estadoDoAgente":"ativo","turnoEmAndamento":false}""",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, antigo.StatusCode);
        using (var json = JsonDocument.Parse(await antigo.Content.ReadAsStringAsync()))
        {
            Assert.Equal(60, json.RootElement.GetProperty("intervaloSegundos").GetInt32());
            Assert.Equal([RemotoV1.RecursoFalasDoDesktop], json.RootElement.GetProperty("recursos").EnumerateArray().Select(r => r.GetString()));
        }

        // A resposta do dev.kit antigo (sem autor nem origem) continua 201, e o comando vai a respondido como antes.
        (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).EnsureSuccessStatusCode();
        var resposta = await ana.PostAsync("/api/remoto/respostas", new StringContent(
            $$"""{"contrato":1,"maquinaId":"{{Maquina}}","id":"r-1","comandoId":"c-1","tipo":"mensagem","texto":"O item 13 está em implementação.","geradaEm":"2026-11-03T12:01:00Z"}""",
            Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal(ConversaV1.EstadoRespondido, (await ana.GetFromJsonAsync<ComandoRemotoV1>("/api/remoto/comandos/c-1"))!.Estado);

        // O histórico guarda o formato de antes e só ACRESCENTA as falas (vazias sem o dev.kit novo).
        using var historico = JsonDocument.Parse(await ana.GetStringAsync($"/api/remoto/maquinas/{Maquina}/conversa"));
        var raiz = historico.RootElement;
        Assert.Equal(["comandos", "respostas", "ultimoEvento", "falas"], raiz.EnumerateObject().Select(p => p.Name));
        Assert.Equal("r-1", raiz.GetProperty("respostas")[0].GetProperty("id").GetString());
        Assert.Equal(0, raiz.GetProperty("falas").GetArrayLength());
    }

    [Fact]
    public async Task As_falas_do_desktop_voltam_no_historico_e_no_sse_com_autor_e_origem_sem_mexer_no_comando()
    {
        var (_, ana) = await DevComFilaAsync();
        (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).EnsureSuccessStatusCode();
        var antes = (await ana.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!.UltimoEvento;
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);

        using var tempo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var fluxo = await AbrirSseAsync(ana, $"/api/remoto/conversa/eventos?maquinaId={Maquina}", antes, tempo.Token);
        var leitura = LerSseAsync(fluxo, l => l.Count(e => e.Evento == ConversaV1.EventoFala) >= 3, tempo.Token);

        // O dev no desktop, o Orquestrador respondendo no desktop e a mensagem do site espelhada pelo dev.kit.
        var dev = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", ditaEm: t0));
        var orquestrador = await ana.PostAsJsonAsync("/api/remoto/conversa/falas",
            Fala("f-2", ConversaV1.AutorOrquestrador, texto: "O item 13 está em implementação.", ditaEm: t0.AddSeconds(5)));
        var remota = await ana.PostAsJsonAsync("/api/remoto/conversa/falas",
            Fala("f-3", origem: ConversaV1.OrigemRemoto, texto: "Como está o projeto controle-remoto-v2?", ditaEm: t0.AddSeconds(10), comando: "c-1"));
        Assert.All(new[] { dev, orquestrador, remota }, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var recebida = (await dev.Content.ReadFromJsonAsync<FalaRecebidaV1>())!;
        Assert.Equal(("f-1", false), (recebida.Id, recebida.Repetida));

        var eventos = (await leitura).Where(e => e.Evento != "ping").ToList();
        Assert.All(eventos, e => Assert.Equal(ConversaV1.EventoFala, e.Evento));
        var lidos = eventos.Select(e => JsonSerializer.Deserialize<EventoDaConversaV1>(e.Dados, Json)!).ToList();
        Assert.Equal(
            [("f-1", ConversaV1.AutorDev, ConversaV1.OrigemDesktop), ("f-2", ConversaV1.AutorOrquestrador, ConversaV1.OrigemDesktop), ("f-3", ConversaV1.AutorDev, ConversaV1.OrigemRemoto)],
            lidos.Select(e => (e.Fala!.Id, e.Fala.Autor, e.Fala.Origem)));
        Assert.All(lidos, e => Assert.Null(e.Comando));
        Assert.Equal("c-1", lidos[2].Fala!.ComandoId);
        // O JSON do SSE leva os campos pelo nome do contrato.
        Assert.Contains("\"fala\":{\"id\":\"f-1\",\"maquinaId\":", eventos[0].Dados, StringComparison.Ordinal);
        Assert.Contains("\"autor\":\"dev\",\"origem\":\"desktop\"", eventos[0].Dados, StringComparison.Ordinal);

        var conversa = (await ana.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!;
        Assert.Equal(["f-1", "f-2", "f-3"], conversa.Falas.Select(f => f.Id));
        Assert.Equal((ConversaV1.AutorOrquestrador, ConversaV1.OrigemDesktop, "O item 13 está em implementação.", t0.AddSeconds(5)),
            (conversa.Falas[1].Autor, conversa.Falas[1].Origem, conversa.Falas[1].Texto, conversa.Falas[1].DitaEm));
        Assert.Null(conversa.Falas[0].ComandoId);
        // A fala não é resposta: o comando segue na fila e nenhuma resposta nasce dela.
        Assert.Empty(conversa.Respostas);
        Assert.Equal(ConversaV1.EstadoNaFila, (await ana.GetFromJsonAsync<ComandoRemotoV1>("/api/remoto/comandos/c-1"))!.Estado);
        Assert.Equal(lidos.Max(e => e.Id), conversa.UltimoEvento);
    }

    [Fact]
    public async Task A_fala_reenviada_com_o_mesmo_id_nao_duplica_e_com_outro_conteudo_e_409()
    {
        var (_, ana) = await DevComFilaAsync();
        var ditaEm = DateTimeOffset.UtcNow;

        var primeira = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", ditaEm: ditaEm));
        var reenviada = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", ditaEm: ditaEm));
        var outroAutor = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", ConversaV1.AutorOrquestrador, ditaEm: ditaEm));
        var outroTexto = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", texto: "Outra coisa", ditaEm: ditaEm));

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reenviada.StatusCode);
        var repetida = (await reenviada.Content.ReadFromJsonAsync<FalaRecebidaV1>())!;
        Assert.Equal(("f-1", true, (await primeira.Content.ReadFromJsonAsync<FalaRecebidaV1>())!.RecebidaEm), (repetida.Id, repetida.Repetida, repetida.RecebidaEm));
        Assert.Equal((HttpStatusCode.Conflict, ConversaV1.CodigoIdEmUso), (outroAutor.StatusCode, await CodigoAsync(outroAutor)));
        Assert.Equal((HttpStatusCode.Conflict, ConversaV1.CodigoIdEmUso), (outroTexto.StatusCode, await CodigoAsync(outroTexto)));

        var falas = (await ana.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!.Falas;
        Assert.Equal((ConversaV1.AutorDev, "Como está o item 13?"), (Assert.Single(falas).Autor, falas[0].Texto));
    }

    [Fact]
    public async Task As_recusas_da_fala_forma_dado_sensivel_contrato_e_isolamento()
    {
        var (admin, ana) = await DevComFilaAsync();
        (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1"))).StatusCode);
        foreach (var (fala, campo) in new[]
        {
            (Fala("f-1", autor: "cliente"), "autor"),
            (Fala("f-1", origem: "email"), "origem"),
            (Fala("f-1", origem: ConversaV1.OrigemRemoto), "comandoId"),
            (Fala("f-1", texto: "  "), "Escreva"),
            (Fala("f-1", texto: new string('a', ConversaV1.TamanhoMaximoDoTexto + 1)), "4000"),
            (Fala("../f-1"), "id da fala"),
        })
        {
            var recusa = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", fala);
            Assert.Equal(HttpStatusCode.BadRequest, recusa.StatusCode);
            Assert.Contains(campo, await recusa.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var sensivel = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", texto: "O cliente 11.222.333/0001-81 pediu o token ghp_abcdefghijklmnopqrstuvwxyz0123456789"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, RemotoV1.CodigoDadoSensivel), (sensivel.StatusCode, await CodigoAsync(sensivel)));
        var corpo = await sensivel.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ghp_", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("11.222.333", corpo, StringComparison.Ordinal);
        var contrato = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1") with { Contrato = 2 });
        Assert.Equal((HttpStatusCode.BadRequest, RemotoV1.CodigoContratoDesconhecido), (contrato.StatusCode, await CodigoAsync(contrato)));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await ana.PostAsync("/api/remoto/conversa/falas", new StringContent("oi", Encoding.UTF8, "text/plain"))).StatusCode);

        // O comando que não existe é 404; o de outra máquina do mesmo usuário, 409 outra-maquina.
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", origem: ConversaV1.OrigemRemoto, comando: "c-nao-existe"))).StatusCode);
        (await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat() with { MaquinaId = "9a8b7c6d5e4f" })).EnsureSuccessStatusCode();
        var outraMaquina = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", origem: ConversaV1.OrigemRemoto, comando: "c-1") with { MaquinaId = "9a8b7c6d5e4f" });
        Assert.Equal((HttpStatusCode.Conflict, ConversaV1.CodigoOutraMaquina), (outraMaquina.StatusCode, await CodigoAsync(outraMaquina)));

        // A bia não fala pela máquina da ana; com a mesma id anônima, a dela é outra máquina e a conversa não se mistura.
        var (bia, _) = await _api.UsuarioAsync(admin, "bia", Papeis.Dev);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-bia"))).StatusCode);
        (await bia.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-bia", origem: ConversaV1.OrigemRemoto, comando: "c-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await bia.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1", texto: "Da bia"))).StatusCode);
        Assert.Empty((await ana.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!.Falas);
        Assert.Equal("Da bia", Assert.Single((await bia.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!.Falas).Texto);
    }

    [Fact]
    public async Task O_limite_por_minuto_das_falas_e_429_e_nao_tira_a_vez_da_resposta()
    {
        using var teste = new ConversaApiTests(new ApiDeTeste(falasPorMinuto: 2));
        var (_, ana) = await teste.DevComFilaAsync();

        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-2"))).StatusCode);
        var acima = await ana.PostAsJsonAsync("/api/remoto/conversa/falas", Fala("f-3"));

        Assert.Equal(HttpStatusCode.TooManyRequests, acima.StatusCode);
        Assert.True(acima.Headers.Contains("Retry-After"));
        // A política é só das falas: a resposta do dev.kit e o comando do site seguem.
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsJsonAsync("/api/remoto/respostas", Resposta("r-1", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await ana.PostAsJsonAsync("/api/remoto/comandos", Mensagem("c-1"))).StatusCode);
        Assert.Equal(2, (await ana.GetFromJsonAsync<ConversaDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/conversa"))!.Falas.Count);
    }

    [Fact]
    public void A_api_nao_sobe_em_production_com_a_fila_simulada()
    {
        using var api = new ApiDeTeste(ambiente: "Production");
        using var simulada = api.WithWebHostBuilder(b => b.UseSetting("Remoto:Fila", OpcoesDoRemoto.FilaSimulada));

        var erro = Assert.ThrowsAny<Exception>(() => simulada.CreateClient());
        Assert.Contains("fila simulada não sobe em Production", erro.ToString(), StringComparison.Ordinal);
    }
}
