using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O controle remoto, fase 1 (<c>/api/remoto</c>): o dev.kit publica heartbeat e retrato com o JWT do usuário e o site lê
/// só o dono. Autenticação, isolamento entre usuários, contrato v1, a sanitização (sem falso positivo no carimbo e no
/// slug) e as recusas certas — a do conteúdo nunca vira 415.
/// </summary>
public sealed class RemotoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static readonly DateTimeOffset Gerado = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static HeartbeatV1 Heartbeat(string maquina = "3f2b9c0e5d7a", string nome = "Notebook do trabalho")
        => new(RemotoV1.Contrato, maquina, nome, "75", RemotoV1.OrigemServico, true, RemotoV1.AgenteAtivo, true, Gerado);

    private static RetratoV1 Retrato(string maquina = "3f2b9c0e5d7a", DateTimeOffset? gerado = null, string titulo = "Qual fila usar?")
        => new(RemotoV1.Contrato, maquina, gerado ?? Gerado,
            [new ProjetoNoRetratoV1("sprint-11-2026-2027", "Sprint 11", ProjetosV1.Aprovado, 1,
                [new ItemNoRetratoV1(1, "API recebe heartbeat", ProjetosV1.ItemRodando, 1)])],
            [new FluxoNoRetratoV1("exec-20261102-095333", "Fluxo 6", "Implementação", "executando", "sprint-11-2026-2027", 1, "claude", Gerado)],
            [new PendenciaNoRetratoV1("pergunta-1", RemotoV1.PendenciaPergunta, titulo, "Storage Queue ou Service Bus?",
                ["Storage Queue", "Service Bus"], "Li a proposta.", "sprint-11-2026-2027", 4, "exec-20261102-095333", Gerado)]);

    private async Task<(HttpClient Cliente, string Token)> DevAsync(string login)
    {
        var admin = await _api.AdminAsync();
        var (cliente, resposta) = await _api.UsuarioAsync(admin, login, Papeis.Dev);
        return (cliente, resposta.Token);
    }

    private static StringContent Json(string json, string tipo = "application/json") => new(json, Encoding.UTF8, tipo);

    [Fact]
    public async Task Retrato_aceita_cinco_pedidos_completos_e_previsao_sem_campos_internos_no_HTTP()
    {
        var (ana, _) = await DevAsync("pacote-remoto");
        await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat());
        var origem = Retrato();
        var resultados = Enumerable.Range(0, 5).SelectMany(i => Enumerable.Range(1, 3).Select(n =>
            new ArtefatoNoRetratoV1(i * 3 + n, "pacote", $"github.com/acme/repo-{n}", new string('a', 40), "falhou", "compilacao",
                "SDK ausente", 0, Gerado.AddMinutes(i), $"pedido{i}", "feature/demo", new("pacote-x", "skill")))).ToArray();
        var projeto = origem.Projetos![0] with { Artefatos = resultados,
            PrevisaoDoPacote = [new("github.com/acme/repo-1", new("pacote-x", "skill"), "Comando direto", true, "pacote")] };
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(origem with { Projetos = [projeto] },
            new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        foreach (var a in json["projetos"]![0]!["artefatos"]!.AsArray())
        {
            a!["caminhoLocal"] = @"C:\SENTINELA_LOCAL\zip";
            a["produtor"]!["comando"] = "SENTINELA_COMANDO";
            a["produtor"]!["conteudo"] = "SENTINELA_CONTEUDO";
            a["produtor"]!["argumentos"] = "SENTINELA_ARGUMENTOS";
        }
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsync("/api/remoto/retrato", Json(json.ToJsonString()))).StatusCode);
        var resposta = await ana.GetAsync("/api/remoto/maquinas/3f2b9c0e5d7a/retrato");
        var texto = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SENTINELA", texto, StringComparison.Ordinal);
        var lido = (await resposta.Content.ReadFromJsonAsync<RetratoDaMaquinaV1>())!.Retrato.Projetos![0];
        Assert.Equal(15, lido.Artefatos!.Count);
        Assert.All(lido.Artefatos.GroupBy(a => a.PedidoId), g => Assert.Equal(3, g.Count()));
        Assert.Equal(projeto.PrevisaoDoPacote, lido.PrevisaoDoPacote);
        var sexto = resultados[0] with { Id = 99, PedidoId = "sexto" };
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/api/remoto/retrato",
            origem with { GeradoEm = Gerado.AddHours(1), Projetos = [projeto with { Artefatos = [.. resultados, sexto] }] })).StatusCode);
        var local = projeto with { PrevisaoDoPacote = [new("github.com/acme/repo-1", new(@"C:\privado\skill", "skill"), "Motivo", true, "pacote")] };
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/api/remoto/retrato", origem with { Projetos = [local] })).StatusCode);
    }

    [Fact]
    public async Task Estados_do_artefato_chegam_ao_polling_do_board_depois_do_aceite_sem_expor_arquivo_local()
    {
        var admin = await _api.AdminAsync();
        var (dono, _) = await _api.UsuarioAsync(admin, "dono-artefato", Papeis.Dev);
        var (outro, _) = await _api.UsuarioAsync(admin, "outro-artefato", Papeis.Dev);
        Assert.Equal(HttpStatusCode.OK, (await dono.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).StatusCode);
        var origem = Retrato();
        var numero = 0;
        foreach (var estado in new[] { "enfileirado", "gerando", "envio-pendente", "falhou" })
        {
            var artefato = new ArtefatoNoRetratoV1(17, "pacote", "github.com/acme/app", new string('a', 40), estado,
                "upload", estado == "falhou" ? "Host sem capacidade para concluir o envio." : "Resultado em acompanhamento", 0, Gerado);
            var projeto = origem.Projetos![0] with { Repositorios = ["github.com/acme/app"], Branch = "feature/projeto", Artefatos = [artefato], PodeGerarArtefato = true };
            var enviado = origem with { GeradoEm = Gerado.AddSeconds(++numero), Projetos = [projeto] };
            Assert.Equal(HttpStatusCode.OK, (await dono.PutAsJsonAsync("/api/remoto/retrato", enviado)).StatusCode);
            var lido = (await dono.GetFromJsonAsync<RetratoDaMaquinaV1>("/api/remoto/maquinas/3f2b9c0e5d7a/retrato"))!;
            var resultado = Assert.Single(Assert.Single(lido.Retrato.Projetos!).Artefatos!);
            Assert.Equal(artefato, resultado);
            Assert.DoesNotContain("caminhoLocal", JsonSerializer.Serialize(resultado), StringComparison.OrdinalIgnoreCase);
        }
        // O envio malformado não apaga o último desfecho que o Board recebeu.
        var invalido = origem with { GeradoEm = Gerado.AddHours(1), Projetos = [origem.Projetos![0] with {
            Artefatos = [new(17, "pacote", "github.com/acme/app", new string('a', 40), "falhou", "upload", @"Arquivo C:\privado\pacote.zip", 0, Gerado)] }] };
        Assert.Equal(HttpStatusCode.BadRequest, (await dono.PutAsJsonAsync("/api/remoto/retrato", invalido)).StatusCode);
        var preservado = (await dono.GetFromJsonAsync<RetratoDaMaquinaV1>("/api/remoto/maquinas/3f2b9c0e5d7a/retrato"))!;
        Assert.Equal("falhou", preservado.Retrato.Projetos![0].Artefatos![0].Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync("/api/remoto/maquinas/3f2b9c0e5d7a/retrato")).StatusCode);
    }

    [Fact]
    public async Task Sem_jwt_com_chave_de_maquina_ou_com_troca_pendente_nada_entra()
    {
        var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync("/api/remoto/retrato", Retrato())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/remoto/maquinas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/remoto/maquinas/3f2b9c0e5d7a/retrato")).StatusCode);

        // A chave da telemetria não é o JWT do usuário.
        var maquina = await _api.MaquinaAsync("3f2b9c0e5d7a");
        Assert.Equal(HttpStatusCode.Unauthorized, (await maquina.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).StatusCode);

        // O token assinado com outra chave não vale.
        var forjado = _api.CreateClient();
        forjado.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwibmFtZSI6ImFkbWluIn0.c2VtLWFzc2luYXR1cmEtdmFsaWRhLWRlLXRlc3Rl");
        Assert.Equal(HttpStatusCode.Unauthorized, (await forjado.GetAsync("/api/remoto/maquinas")).StatusCode);

        // O admin que ainda não trocou a senha está autenticado, mas não publica nem lê.
        var pendente = _api.CreateClient();
        pendente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await _api.LoginAsync(pendente)).Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendente.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pendente.GetAsync("/api/remoto/maquinas")).StatusCode);
    }

    [Fact]
    public async Task O_dono_publica_heartbeat_e_retrato_e_le_a_maquina_conectada_com_o_retrato()
    {
        var (ana, _) = await DevAsync("ana");

        var hb = await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat());
        Assert.Equal(HttpStatusCode.OK, hb.StatusCode);
        var recebido = (await hb.Content.ReadFromJsonAsync<HeartbeatRecebidoV1>())!;
        Assert.Equal(("3f2b9c0e5d7a", RemotoV1.IntervaloDoHeartbeatSegundos), (recebido.MaquinaId, recebido.IntervaloSegundos));

        var rt = await ana.PutAsJsonAsync("/api/remoto/retrato", Retrato());
        Assert.Equal(HttpStatusCode.OK, rt.StatusCode);
        Assert.True((await rt.Content.ReadFromJsonAsync<RetratoRecebidoV1>())!.Aplicado);

        var maquina = Assert.Single((await ana.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!);
        Assert.Equal(("3f2b9c0e5d7a", "Notebook do trabalho", "75", "servico", true, "ativo", true, true),
            (maquina.MaquinaId, maquina.Nome, maquina.VersaoDevKit, maquina.Origem, maquina.AppAberto, maquina.EstadoDoAgente,
             maquina.TurnoEmAndamento, maquina.Conectada));
        Assert.Equal(Gerado, maquina.RetratoGeradoEm);

        var lido = (await ana.GetFromJsonAsync<RetratoDaMaquinaV1>("/api/remoto/maquinas/3f2b9c0e5d7a/retrato"))!;
        Assert.Equal(JsonSerializer.Serialize(Retrato()), JsonSerializer.Serialize(lido.Retrato));
        Assert.Equal("3f2b9c0e5d7a", lido.Maquina.MaquinaId);
    }

    [Fact]
    public async Task Um_usuario_nao_le_nem_sobrescreve_a_maquina_de_outro_nem_o_admin()
    {
        var admin = await _api.AdminAsync();
        var (ana, _) = await _api.UsuarioAsync(admin, "ana", Papeis.Dev);
        var (bia, _) = await _api.UsuarioAsync(admin, "bia", Papeis.Dev);
        await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat(nome: "Máquina da Ana"));
        await ana.PutAsJsonAsync("/api/remoto/retrato", Retrato(titulo: "Pendência da Ana"));

        Assert.Empty((await bia.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync("/api/remoto/maquinas/3f2b9c0e5d7a/retrato")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/remoto/maquinas/3f2b9c0e5d7a/retrato")).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!);

        // Bia publica com o MESMO id anônimo: vira a máquina dela, e a da Ana fica como estava.
        Assert.Equal(HttpStatusCode.OK, (await bia.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat(nome: "Máquina da Bia"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bia.PutAsJsonAsync("/api/remoto/retrato", Retrato(gerado: Gerado.AddHours(1), titulo: "Pendência da Bia"))).StatusCode);

        Assert.Equal("Máquina da Ana", Assert.Single((await ana.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!).Nome);
        Assert.Equal("Pendência da Ana", (await ana.GetFromJsonAsync<RetratoDaMaquinaV1>("/api/remoto/maquinas/3f2b9c0e5d7a/retrato"))!.Retrato.Pendencias![0].Titulo);
        Assert.Equal("Máquina da Bia", Assert.Single((await bia.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!).Nome);
        Assert.Equal("Pendência da Bia", (await bia.GetFromJsonAsync<RetratoDaMaquinaV1>("/api/remoto/maquinas/3f2b9c0e5d7a/retrato"))!.Retrato.Pendencias![0].Titulo);
    }

    [Fact]
    public async Task O_contrato_v1_tem_os_nomes_documentados_e_recusa_outro_contrato_com_codigo()
    {
        var (ana, _) = await DevAsync("ana");

        // O envio é o JSON do contrato documentado, escrito à mão como o dev.kit o escreve.
        var hb = await ana.PostAsync("/api/remoto/heartbeat", Json(
            """{"contrato":1,"maquinaId":"m-1","nome":"PC","versaoDevKit":"75","origem":"app","appAberto":true,"estadoDoAgente":"pausado","turnoEmAndamento":false,"campoNovo":"ignorado"}"""));
        Assert.Equal(HttpStatusCode.OK, hb.StatusCode);

        var lista = await ana.GetFromJsonAsync<JsonElement>("/api/remoto/maquinas");
        var maquina = lista.EnumerateArray().Single();
        foreach (var campo in new[] { "maquinaId", "nome", "versaoDevKit", "origem", "appAberto", "estadoDoAgente", "turnoEmAndamento", "conectada", "ultimoHeartbeatEm", "retratoGeradoEm" })
            Assert.True(maquina.TryGetProperty(campo, out _), $"Falta {campo} no contrato.");
        Assert.Equal("pausado", maquina.GetProperty("estadoDoAgente").GetString());

        var futuro = await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat() with { Contrato = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, futuro.StatusCode);
        var problema = await futuro.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(RemotoV1.CodigoContratoDesconhecido, problema.GetProperty("codigo").GetString());
        Assert.Equal(RemotoV1.Contrato, problema.GetProperty("contratoAceito").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/api/remoto/retrato", Retrato() with { Contrato = 0 })).StatusCode);

        // A forma fora do contrato é 400 com a mensagem do campo.
        var torto = await ana.PutAsJsonAsync("/api/remoto/retrato", Retrato() with { MaquinaId = "../outra" });
        Assert.Equal(HttpStatusCode.BadRequest, torto.StatusCode);
        Assert.Contains("maquinaId", await torto.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task O_retrato_mais_velho_que_o_gravado_e_aceito_mas_nao_volta_a_foto_para_tras()
    {
        var (ana, _) = await DevAsync("ana");
        await ana.PutAsJsonAsync("/api/remoto/retrato", Retrato(titulo: "Novo"));

        var velho = await ana.PutAsJsonAsync("/api/remoto/retrato", Retrato(gerado: Gerado.AddMinutes(-5), titulo: "Velho"));
        Assert.Equal(HttpStatusCode.OK, velho.StatusCode);
        Assert.False((await velho.Content.ReadFromJsonAsync<RetratoRecebidoV1>())!.Aplicado);
        Assert.Equal("Novo", (await ana.GetFromJsonAsync<RetratoDaMaquinaV1>("/api/remoto/maquinas/3f2b9c0e5d7a/retrato"))!.Retrato.Pendencias![0].Titulo);

        // Só com retrato a máquina existe, mas não está conectada: falta o heartbeat.
        var maquina = Assert.Single((await ana.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!);
        Assert.False(maquina.Conectada);
        Assert.Null(maquina.UltimoHeartbeatEm);
    }

    [Theory]
    [InlineData("Ligar para o cliente 11.222.333/0001-81", "cnpj")]
    [InlineData("Avisar (11) 98765-4321", "telefone")]
    [InlineData("CPF 111.444.777-35", "cpf")]
    [InlineData("Responder a ana.souza@cliente.com.br", "email")]
    [InlineData("Use o PAT ghp_abcdefghijklmnopqrstuvwxyz0123456789", "segredo")]
    public async Task O_retrato_com_token_ou_dado_de_cliente_e_422_com_o_campo_e_sem_o_valor(string titulo, string tipo)
    {
        var (ana, _) = await DevAsync("ana");

        var resposta = await ana.PutAsJsonAsync("/api/remoto/retrato", Retrato(titulo: titulo));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        var corpo = await resposta.Content.ReadAsStringAsync();
        var problema = JsonSerializer.Deserialize<JsonElement>(corpo);
        Assert.Equal(RemotoV1.CodigoDadoSensivel, problema.GetProperty("codigo").GetString());
        var campo = Assert.Single(problema.GetProperty("campos").EnumerateArray());
        Assert.Equal(("pendencias[0].titulo", tipo), (campo.GetProperty("campo").GetString(), campo.GetProperty("tipo").GetString()));
        Assert.DoesNotContain(titulo, corpo, StringComparison.Ordinal);

        // Nada foi gravado.
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync("/api/remoto/maquinas/3f2b9c0e5d7a/retrato")).StatusCode);
    }

    [Fact]
    public async Task Carimbo_de_data_e_slug_com_anos_passam_sem_falso_positivo()
    {
        var (ana, _) = await DevAsync("ana");
        var retrato = Retrato(titulo: "Captura 20261102-095333 da sprint-11-2026-2027 (Sprint 11 2026-2027)");

        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync("/api/remoto/retrato", retrato)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat(nome: "pc-20261102-095333"))).StatusCode);
    }

    [Fact]
    public async Task O_heartbeat_com_dado_de_cliente_tambem_e_422()
    {
        var (ana, _) = await DevAsync("ana");

        var resposta = await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat(nome: "PC do cliente 11.222.333/0001-81"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Empty((await ana.GetFromJsonAsync<MaquinaRemotaV1[]>("/api/remoto/maquinas"))!);
    }

    [Fact]
    public async Task So_o_formato_do_corpo_e_415_o_json_invalido_e_400_e_o_corpo_grande_e_413()
    {
        var (ana, _) = await DevAsync("ana");
        var valido = JsonSerializer.Serialize(Retrato(), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await ana.PutAsync("/api/remoto/retrato", Json(valido, "text/plain"))).StatusCode);

        var utf7 = new ByteArrayContent(Encoding.UTF8.GetBytes(valido));
        utf7.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-7");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await ana.PutAsync("/api/remoto/retrato", utf7)).StatusCode);

        var desconhecido = new ByteArrayContent(Encoding.UTF8.GetBytes(valido));
        desconhecido.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=nao-existe");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await ana.PutAsync("/api/remoto/retrato", desconhecido)).StatusCode);

        // O charset conhecido, diferente de UTF-8, é lido.
        var utf16 = new ByteArrayContent(Encoding.Unicode.GetBytes(valido));
        utf16.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-16");
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsync("/api/remoto/retrato", utf16)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsync("/api/remoto/retrato", Json("{ isto não é json"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsync("/api/remoto/retrato", Json("null"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsync("/api/remoto/retrato", Json("""{"contrato":"um"}"""))).StatusCode);

        var grande = Retrato(titulo: "x") with
        {
            Pendencias = Enumerable.Range(0, 100).Select(i => new PendenciaNoRetratoV1($"p{i}", "pergunta", "título", new string('a', 2000), JaTentei: new string('b', 2000))).ToArray(),
        };
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ana.PutAsJsonAsync("/api/remoto/retrato", grande)).StatusCode);

        // Sem Content-Length (chunked), o teto vale igual.
        var chunked = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(grande, new JsonSerializerOptions(JsonSerializerDefaults.Web)))));
        chunked.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");
        var pedido = new HttpRequestMessage(HttpMethod.Put, "/api/remoto/retrato") { Content = chunked };
        pedido.Headers.TransferEncodingChunked = true;
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await ana.SendAsync(pedido)).StatusCode);
    }

    [Fact]
    public async Task A_falha_ao_ler_o_corpo_e_500_e_nao_415()
    {
        var (_, token) = await DevAsync("ana");
        using var comFalha = _api.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IStartupFilter>(new CorpoQueFalha())));
        var cliente = comFalha.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resposta = await cliente.PutAsJsonAsync("/api/remoto/retrato", Retrato());

        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, resposta.StatusCode);
    }

    /// <summary>Troca o corpo dos envios do remoto por um stream que lança — o defeito interno que não pode virar 415.</summary>
    private sealed class CorpoQueFalha : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> proximo) => app =>
        {
            app.Use((http, seguir) =>
            {
                if (http.Request.Path.StartsWithSegments("/api/remoto"))
                    http.Request.Body = new StreamQueLanca();
                return seguir(http);
            });
            proximo(app);
        };
    }

    private sealed class StreamQueLanca : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Falha simulada ao ler o corpo.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Falha simulada ao ler o corpo.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
