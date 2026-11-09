using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Api.Tests;

public sealed class DecisoesApiTests
{
    private const string Rota = "/api/remoto/maquinas/notebook/decisoes";
    private static SnapshotDaDecisaoV1 Snapshot => new("feature/item-9", "feature/projeto", new string('a', 40),
        [new("Código", 96, 90), new("Visual", 95, 90)], 128, 0, true, 4, 2, "+ decisões auditadas", []);
    private static PendenciaNoRetratoV1 Pergunta => new("pergunta", "pergunta", "Continuar?", "Qual opção?", ["Manter", "Revisar"], "Comparei a entrega.");
    private static PendenciaNoRetratoV1 Aprovacao => new("aprovacao", "aprovacao", "Integrar no projeto?", Snapshot: Snapshot);
    private static async Task Retrato(HttpClient c, params PendenciaNoRetratoV1[] pendencias)
    {
        (await c.PutAsJsonAsync("/api/remoto/retrato", new RetratoV1(1, "notebook", DateTimeOffset.UtcNow, [], [], pendencias))).EnsureSuccessStatusCode();
    }
    private static async Task PrepararMaquina(HttpClient c)
        => (await c.PostAsJsonAsync("/api/remoto/heartbeat", new HeartbeatV1(1, "notebook", "Notebook", "96", "servico", true, "ativo", false))).EnsureSuccessStatusCode();
    private static async Task<DecisaoPendenteV1> Ler(HttpClient c, string id)
        => (await c.GetFromJsonAsync<DecisaoPendenteV1[]>(Rota))!.Single(d => d.Pendencia.Id == id);
    private static async Task<string> Ativar(HttpClient c, Relogio relogio)
    {
        var preparo = await c.PostAsJsonAsync("/api/remoto/segundo-fator/preparar", new PrepararAutenticadorV1(ApiDeTeste.NovaSenha));
        preparo.EnsureSuccessStatusCode();
        var chave = (await preparo.Content.ReadFromJsonAsync<AutenticadorPreparadoV1>())!.Chave;
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/remoto/segundo-fator/confirmar", new ConfirmarAutenticadorV1(Totp.Codigo(chave, relogio.Passo)))).StatusCode);
        relogio.Avancar(); return chave;
    }
    private sealed class Relogio : TimeProvider
    {
        private DateTimeOffset _agora = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _agora;
        public long Passo => _agora.ToUnixTimeSeconds() / 30;
        public void Avancar(int segundos = 30) => _agora = _agora.AddSeconds(segundos);
    }

    [Fact]
    public async Task Os_tres_niveis_exigem_login_e_isolam_o_dono()
    {
        using var api = new ApiDeTeste();
        var anonimo = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Rota)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync(Rota + "/pergunta", new ResponderDecisaoV1(new string('a', 64), "responder", "Manter"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PostAsJsonAsync("/api/remoto/segundo-fator/preparar", new PrepararAutenticadorV1("senha"))).StatusCode);
        var admin = await api.AdminAsync();
        await PrepararMaquina(admin); await Retrato(admin, Pergunta, Aprovacao);
        var (outro, _) = await api.UsuarioAsync(admin, "outro", Papeis.Dev);
        Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync(Rota)).StatusCode);
        var d = await Ler(admin, "aprovacao");
        Assert.Equal(HttpStatusCode.NotFound, (await outro.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: "123456"))).StatusCode);
        Assert.Empty((await outro.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);
    }

    [Fact]
    public async Task Dispensar_fecha_o_card_so_no_site_o_filtro_traz_de_volta_e_restaurar_devolve()
    {
        using var api = new ApiDeTeste(); var c = await api.AdminAsync();
        await PrepararMaquina(c); await Retrato(c, Pergunta, Aprovacao);
        var dispensa = await c.PostAsJsonAsync(Rota + "/pergunta", new { acao = DecisoesV1.AcaoDispensar });
        dispensa.EnsureSuccessStatusCode();
        var lida = (await dispensa.Content.ReadFromJsonAsync<DispensaDaPendenciaV1>())!;
        Assert.Equal(("pergunta", true), (lida.PendenciaId, lida.Dispensada));
        // O duplo clique não grava duas.
        (await c.PostAsJsonAsync(Rota + "/pergunta", new { acao = DecisoesV1.AcaoDispensar })).EnsureSuccessStatusCode();

        Assert.Equal(["aprovacao"], (await c.GetFromJsonAsync<DecisaoPendenteV1[]>(Rota))!.Select(d => d.Pendencia.Id));
        var todas = (await c.GetFromJsonAsync<DecisaoPendenteV1[]>(Rota + "?dispensadas=true"))!;
        Assert.Equal([("pergunta", true), ("aprovacao", false)], todas.Select(d => (d.Pendencia.Id, d.Dispensada)));
        // Nada foi decidido nem vai à máquina.
        Assert.Empty((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);
        Assert.Null(todas[0].Resposta);

        // Outro usuário não dispensa a pendência da máquina alheia, e a pendência que não existe é 404.
        var (outro, _) = await api.UsuarioAsync(c, "outro", Papeis.Dev);
        Assert.Equal(HttpStatusCode.NotFound, (await outro.PostAsJsonAsync(Rota + "/aprovacao", new { acao = DecisoesV1.AcaoDispensar })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.PostAsJsonAsync(Rota + "/inexistente", new { acao = DecisoesV1.AcaoDispensar })).StatusCode);

        (await c.PostAsJsonAsync(Rota + "/pergunta", new { acao = DecisoesV1.AcaoRestaurar })).EnsureSuccessStatusCode();
        Assert.Equal(["pergunta", "aprovacao"], (await c.GetFromJsonAsync<DecisaoPendenteV1[]>(Rota))!.Select(d => d.Pendencia.Id));

        // A pendência que sai do retrato leva a dispensa junto: se voltar, volta visível.
        (await c.PostAsJsonAsync(Rota + "/pergunta", new { acao = DecisoesV1.AcaoDispensar })).EnsureSuccessStatusCode();
        await Retrato(c, Aprovacao);
        (await c.PostAsJsonAsync(Rota + "/aprovacao", new { acao = DecisoesV1.AcaoRestaurar })).EnsureSuccessStatusCode();
        await Retrato(c, Pergunta, Aprovacao);
        Assert.Equal(2, (await c.GetFromJsonAsync<DecisaoPendenteV1[]>(Rota))!.Length);
    }

    [Fact]
    public async Task Pergunta_aceita_so_opcao_do_snapshot_audita_e_consumo_nao_repete()
    {
        using var api = new ApiDeTeste(); var c = await api.AdminAsync();
        await PrepararMaquina(c); await Retrato(c, Pergunta);
        var d = await Ler(c, "pergunta");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Rota + "/pergunta", new ResponderDecisaoV1(d.SnapshotId, "responder", "Inventada"))).StatusCode);
        var r = await c.PostAsJsonAsync(Rota + "/pergunta", new ResponderDecisaoV1(d.SnapshotId, "responder", "Manter")); r.EnsureSuccessStatusCode();
        var resposta = (await r.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!;
        Assert.False(resposta.SegundoFator); Assert.Equal("Manter", resposta.Opcao);
        Assert.NotNull((await Ler(c, "pergunta")).Resposta);
        Assert.Single((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"{Rota}/respostas/{resposta.Id}/recebida", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"{Rota}/respostas/{resposta.Id}/recebida", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(Rota + "/pergunta", new ResponderDecisaoV1(d.SnapshotId, "responder", "Revisar"))).StatusCode);
    }

    [Fact]
    public async Task Autorizar_com_o_login_sem_codigo_vale_so_para_o_escopo_revisado_e_vai_a_maquina_como_o_aprovar()
    {
        using var original = new ApiDeTeste(); var relogio = new Relogio();
        using var api = original.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(relogio)));
        var c = await original.AdminAsync();
        var autenticado = api.CreateClient(); autenticado.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization; c = autenticado;
        var pedido = new PendenciaNoRetratoV1("pendencia-comando", RemotoV1.PendenciaAutorizacao, "#515 Implementar",
            "Autorizar o comando bloqueado uma vez?", Escopo: new EscopoDaAutorizacaoV1(DecisoesV1.AtoExecutarComando, "#515 Implementar", Comando: "dotnet test"));
        var batimento = await c.PostAsJsonAsync("/api/remoto/heartbeat", new HeartbeatV1(1, "notebook", "Notebook", "130", "servico", true, "ativo", false));
        Assert.Contains(RemotoV1.RecursoDecisoesAutorizar, (await batimento.Content.ReadFromJsonAsync<HeartbeatRecebidoV1>())!.Recursos!);
        await Retrato(c, pedido);
        var d = await Ler(c, "pendencia-comando");
        Assert.Null(d.Bloqueio);
        Assert.Equal("dotnet test", d.Pendencia.Escopo!.Comando);

        // Responder e aprovar não valem para a autorização.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Rota + "/pendencia-comando", new ResponderDecisaoV1(d.SnapshotId, DecisoesV1.AcaoAprovar))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Rota + "/pendencia-comando", new ResponderDecisaoV1(d.SnapshotId, DecisoesV1.AcaoResponder, "Sim"))).StatusCode);

        // O escopo mudou depois da revisão (outro comando): a autorização revisada não vale para ele.
        await Retrato(c, pedido with { Escopo = pedido.Escopo! with { Comando = "rm -rf build" } });
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(Rota + "/pendencia-comando",
            new ResponderDecisaoV1(d.SnapshotId, DecisoesV1.AcaoAutorizar))).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);

        // Com o escopo de agora e só o login (sem código, sem autenticador configurado), a autorização é gravada — sem o
        // segundo fator na auditoria, que não houve — e a máquina a consome uma vez.
        await Retrato(c, pedido);
        var r = await c.PostAsJsonAsync(Rota + "/pendencia-comando", new ResponderDecisaoV1(d.SnapshotId, DecisoesV1.AcaoAutorizar, Dispositivo: ConversaV1.DispositivoNavegador));
        r.EnsureSuccessStatusCode();
        var autorizacao = (await r.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!;
        Assert.Equal((DecisoesV1.AcaoAutorizar, false), (autorizacao.Acao, autorizacao.SegundoFator));
        Assert.Equal((relogio.GetUtcNow(), ConversaV1.DispositivoNavegador, d.SnapshotId), (autorizacao.Em, autorizacao.Dispositivo, autorizacao.SnapshotId));
        Assert.False((await c.GetFromJsonAsync<EstadoDoAutenticadorV1>("/api/remoto/segundo-fator"))!.Ativo);
        Assert.Single((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"{Rota}/respostas/{autorizacao.Id}/recebida", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"{Rota}/respostas/{autorizacao.Id}/recebida", null)).StatusCode);

        // A pendência de autorização sem escopo não entra no retrato.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsJsonAsync("/api/remoto/retrato",
            new RetratoV1(1, "notebook", DateTimeOffset.UtcNow, [], [], [pedido with { Escopo = null }]))).StatusCode);
    }

    [Fact]
    public async Task O_dispositivo_da_decisao_vem_na_resposta_no_consumo_e_e_opcional()
    {
        using var original = new ApiDeTeste(); var relogio = new Relogio();
        using var api = original.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(relogio)));
        var c = await original.AdminAsync();
        var autenticado = api.CreateClient(); autenticado.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization; c = autenticado;
        var arquivar = new PendenciaNoRetratoV1("autorizacao-arquivar", RemotoV1.PendenciaAutorizacao, "Arquivar 2 task(s) pedidas pelo Orquestrador",
            "O Orquestrador pede para arquivar: #901, #902. Autorizar o arquivamento?",
            Escopo: new EscopoDaAutorizacaoV1(DecisoesV1.AtoArquivarTasks, "Orquestrador", Itens: ["#901", "#902"],
                Comando: "devcli sessao --acao arquivar --alvo #901,#902 --aplicar"));
        await PrepararMaquina(c); await Retrato(c, arquivar, Pergunta);
        var d = await Ler(c, "autorizacao-arquivar");
        Assert.Null(d.Bloqueio);

        // Fora da lista, recusa sem gravar nada.
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync(Rota + "/autorizacao-arquivar",
            new ResponderDecisaoV1(d.SnapshotId, DecisoesV1.AcaoAutorizar, Dispositivo: "tablet"))).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);

        // O celular (com caixa e espaço, normalizado) vem na resposta, na caixa de saída da máquina e no consumo único.
        var r = await c.PostAsJsonAsync(Rota + "/autorizacao-arquivar",
            new ResponderDecisaoV1(d.SnapshotId, DecisoesV1.AcaoAutorizar, Dispositivo: " Celular "));
        r.EnsureSuccessStatusCode();
        var autorizacao = (await r.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!;
        Assert.Equal((DecisoesV1.AcaoAutorizar, false, ConversaV1.DispositivoCelular), (autorizacao.Acao, autorizacao.SegundoFator, autorizacao.Dispositivo));
        Assert.Contains("\"dispositivo\":\"celular\"", await r.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(ConversaV1.DispositivoCelular, Assert.Single((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!).Dispositivo);
        Assert.Equal(ConversaV1.DispositivoCelular, (await Ler(c, "autorizacao-arquivar")).Resposta!.Dispositivo);
        var recebida = await c.PostAsync($"{Rota}/respostas/{autorizacao.Id}/recebida", null);
        Assert.Equal(ConversaV1.DispositivoCelular, (await recebida.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!.Dispositivo);

        // Sem o campo (o site antigo), a decisão vale como antes e a resposta vai sem o dispositivo.
        var p = await Ler(c, "pergunta");
        var semDispositivo = await c.PostAsJsonAsync(Rota + "/pergunta", new { snapshotId = p.SnapshotId, acao = "responder", opcao = "Manter" });
        semDispositivo.EnsureSuccessStatusCode();
        Assert.Null((await semDispositivo.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!.Dispositivo);
    }

    [Fact]
    public async Task Mensagem_aprovo_nao_autoriza_e_aprovacao_vale_com_o_login_ignorando_o_codigo_do_cliente_antigo()
    {
        using var original = new ApiDeTeste(); var relogio = new Relogio();
        using var api = original.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(relogio)));
        var c = await original.AdminAsync();
        // Usa o mesmo banco do factory original com o relógio injetado no segundo host.
        var autenticado = api.CreateClient(); autenticado.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization; c = autenticado;
        await PrepararMaquina(c); await Retrato(c, Aprovacao);
        var d = await Ler(c, "aprovacao");
        var usuario = (await c.GetFromJsonAsync<UsuarioResumo[]>("/api/usuarios"))!.Single(u => u.Login == "admin");
        // A liberação pede a fila (remote-control-v3, item 5); o motor a conclui.
        (await c.PutAsJsonAsync($"/api/usuarios/{usuario.Id}/recursos/{ProvisaoV1.RecursoProvisao}", new LiberacaoDeRecursoV1(true))).EnsureSuccessStatusCode();
        using (var escopo = api.Services.CreateScope())
        {
            var motor = escopo.ServiceProvider.GetRequiredService<IMotorDeProvisao>();
            var recurso = (await motor.ReservarAsync("teste", default))!;
            Assert.True(await motor.ConcluirAsync(recurso.Id, "teste", new RecursoNaNuvem("/subscriptions/teste/storage", "https://filateste.queue.core.windows.net/comandos"), default));
        }
        Assert.Equal(HttpStatusCode.Accepted, (await c.PostAsJsonAsync("/api/remoto/comandos", new ComandoNovoV1(null, "notebook", "mensagem", "aprovo"))).StatusCode);
        Assert.Empty((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);

        // O cliente antigo ainda manda o código (até um inválido ou fora do tamanho): ele é ignorado — nem valida, nem recusa.
        var r = await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: "abc1234567", Dispositivo: ConversaV1.DispositivoCelular));
        r.EnsureSuccessStatusCode();
        var autorizacao = (await r.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!;
        // A auditoria: a origem, o destino e o SHA revisados, o dispositivo, o horário — e SegundoFator falso, porque não houve.
        Assert.False(autorizacao.SegundoFator);
        Assert.Equal((Snapshot.Origem, Snapshot.Destino, Snapshot.Sha), (autorizacao.Origem, autorizacao.Destino, autorizacao.Sha));
        Assert.Equal((ConversaV1.DispositivoCelular, relogio.GetUtcNow()), (autorizacao.Dispositivo, autorizacao.Em));
        using (var escopo = api.Services.CreateScope())
        {
            var gravada = await escopo.ServiceProvider.GetRequiredService<DevKitPageDb>().RespostasDasDecisoes.AsNoTracking().SingleAsync(x => x.Id == autorizacao.Id);
            Assert.Equal(("aprovar", false, Snapshot.Origem, ConversaV1.DispositivoCelular), (gravada.Acao, gravada.SegundoFator, gravada.Origem, gravada.Dispositivo));
            Assert.Equal(relogio.GetUtcNow().UtcDateTime, gravada.EmUtc);
        }
        var recebida = await c.PostAsync($"{Rota}/respostas/{autorizacao.Id}/recebida", null);
        Assert.Equal(HttpStatusCode.OK, recebida.StatusCode);
        Assert.Equal("recebida", (await recebida.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!.Estado);

        // Outra pendência, sem código nenhum: só o login basta. O consumo segue único.
        await Retrato(c, Aprovacao with { Id = "outra" });
        var outra = await Ler(c, "outra");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync(Rota + "/outra", new ResponderDecisaoV1(outra.SnapshotId, "aprovar"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"{Rota}/respostas/{autorizacao.Id}/recebida", null)).StatusCode);
    }

    [Fact]
    public async Task Com_o_autenticador_ativo_o_aprovar_tambem_dispensa_o_codigo_e_nao_gasta_tentativas()
    {
        using var original = new ApiDeTeste(); var relogio = new Relogio();
        using var api = original.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(relogio)));
        var login = await original.AdminAsync();
        var c = api.CreateClient(); c.DefaultRequestHeaders.Authorization = login.DefaultRequestHeaders.Authorization;
        await Ativar(c, relogio);   // o autenticador da Provisão, ligado
        await PrepararMaquina(c); await Retrato(c, Aprovacao);
        var d = await Ler(c, "aprovacao");
        async Task<AutenticadorDoUsuario> Autenticador()
        {
            using var escopo = api.Services.CreateScope();
            return await escopo.ServiceProvider.GetRequiredService<DevKitPageDb>().Autenticadores.AsNoTracking().SingleAsync();
        }
        var antes = await Autenticador();
        var r = await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: "000000"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.False((await r.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!.SegundoFator);
        // O código ignorado não conta como tentativa do TOTP nem gasta o passo: o autenticador da Provisão fica como estava.
        var depois = await Autenticador();
        Assert.Equal((antes.Tentativas, antes.UltimoPasso), (depois.Tentativas, depois.UltimoPasso));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Tentativa_vencida_pode_ser_revisada_sem_repetir_consumo_e_preserva_auditoria(bool aprovar, bool retirarVencida)
    {
        using var original = new ApiDeTeste(); var relogio = new Relogio();
        using var api = original.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(relogio)));
        var login = await original.AdminAsync();
        var c = api.CreateClient(); c.DefaultRequestHeaders.Authorization = login.DefaultRequestHeaders.Authorization;
        await PrepararMaquina(c); await Retrato(c, aprovar ? Aprovacao : Pergunta);
        var d = await Ler(c, aprovar ? "aprovacao" : "pergunta");
        // O aprovar vale com o login: sem código (semana 7).
        ResponderDecisaoV1 Pedido() => new(d.SnapshotId, aprovar ? "aprovar" : "responder", aprovar ? null : "Manter");
        var primeira = await c.PostAsJsonAsync(Rota + "/" + d.Pendencia.Id, Pedido()); primeira.EnsureSuccessStatusCode();
        var anterior = (await primeira.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(Rota + "/" + d.Pendencia.Id, Pedido())).StatusCode);
        relogio.Avancar(301); await PrepararMaquina(c);
        Assert.Equal("vencida", (await Ler(c, d.Pendencia.Id)).Resposta!.Estado);
        Assert.Empty((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);
        if (retirarVencida) Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"{Rota}/respostas/{anterior.Id}/recebida", null)).StatusCode);
        var nova = await c.PostAsJsonAsync(Rota + "/" + d.Pendencia.Id, Pedido()); nova.EnsureSuccessStatusCode();
        var atual = (await nova.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!;
        Assert.NotEqual(anterior.Id, atual.Id); Assert.Equal(anterior.SnapshotId, atual.SnapshotId);
        Assert.Equal(relogio.GetUtcNow(), atual.Em);
        Assert.Equal(atual.Id, (await Ler(c, d.Pendencia.Id)).Resposta!.Id);
        Assert.Equal(atual.Id, Assert.Single((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!).Id);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"{Rota}/respostas/{anterior.Id}/recebida", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"{Rota}/respostas/{atual.Id}/recebida", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"{Rota}/respostas/{atual.Id}/recebida", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(Rota + "/" + d.Pendencia.Id, Pedido())).StatusCode);
        using var escopo = api.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<DevKitPageDb>();
        var historico = await db.RespostasDasDecisoes.AsNoTracking().OrderBy(r => r.Id).ToArrayAsync();
        Assert.Equal(new[] { "vencida", "recebida" }, historico.Select(r => r.Estado));
    }

    [Fact]
    public async Task Nota_regressao_snapshot_mudado_e_evidencia_ausente_bloqueiam_aprovacao()
    {
        using var api = new ApiDeTeste(); var c = await api.AdminAsync();
        await PrepararMaquina(c); await Retrato(c, Aprovacao);
        var antigo = await Ler(c, "aprovacao");
        foreach (var snapshot in new SnapshotDaDecisaoV1?[] { null, Snapshot with { Notas = [new("Código", 89, 90)] }, Snapshot with { TestesFalhos = 1 }, Snapshot with { RegressaoConcluida = false } })
        {
            await Retrato(c, Aprovacao with { Snapshot = snapshot });
            var d = await Ler(c, "aprovacao"); Assert.NotNull(d.Bloqueio);
            Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: "123456"))).StatusCode);
        }
        await Retrato(c, Aprovacao with { Snapshot = Snapshot with { Destino = "outro-destino" } });
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(antigo.SnapshotId, "aprovar", Codigo: "123456"))).StatusCode);
        var atual = await Ler(c, "aprovacao");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(atual.SnapshotId, "ajuste"))).StatusCode);
    }

    [Fact]
    public async Task Autenticador_exige_senha_confirmacao_e_limita_tentativas_sem_permitir_troca()
    {
        using var original = new ApiDeTeste(); var relogio = new Relogio();
        using var api = original.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(relogio)));
        var login = await original.AdminAsync();
        var c = api.CreateClient(); c.DefaultRequestHeaders.Authorization = login.DefaultRequestHeaders.Authorization;
        // O autenticador segue existindo para a Provisão (semana 7: a decisão remota não o usa): o limite de tentativas vale
        // na confirmação dele.
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/remoto/segundo-fator/preparar", new PrepararAutenticadorV1("errada"))).StatusCode);
        var preparo = await c.PostAsJsonAsync("/api/remoto/segundo-fator/preparar", new PrepararAutenticadorV1(ApiDeTeste.NovaSenha));
        preparo.EnsureSuccessStatusCode();
        var chave = (await preparo.Content.ReadFromJsonAsync<AutenticadorPreparadoV1>())!.Chave;
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/remoto/segundo-fator/confirmar", new ConfirmarAutenticadorV1("abcdef"))).StatusCode);
        var bloqueada = await c.PostAsJsonAsync("/api/remoto/segundo-fator/confirmar", new ConfirmarAutenticadorV1(Totp.Codigo(chave, relogio.Passo)));
        Assert.Equal(HttpStatusCode.Forbidden, bloqueada.StatusCode);
        Assert.Contains("Limite de tentativas", await bloqueada.Content.ReadAsStringAsync());
        Assert.False((await c.GetFromJsonAsync<EstadoDoAutenticadorV1>("/api/remoto/segundo-fator"))!.Ativo);
        relogio.Avancar(301);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/remoto/segundo-fator/confirmar", new ConfirmarAutenticadorV1(Totp.Codigo(chave, relogio.Passo)))).StatusCode);
        Assert.True((await c.GetFromJsonAsync<EstadoDoAutenticadorV1>("/api/remoto/segundo-fator"))!.Ativo);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/remoto/segundo-fator/preparar", new PrepararAutenticadorV1(ApiDeTeste.NovaSenha))).StatusCode);
    }
}
