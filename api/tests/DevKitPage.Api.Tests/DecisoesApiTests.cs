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
    public async Task Mensagem_aprovo_nao_autoriza_e_aprovacao_exige_codigo_valido_nao_reutilizado()
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
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar"))).StatusCode);
        var chave = await Ativar(c, relogio);
        var expirado = Totp.Codigo(chave, relogio.Passo - 4);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: expirado))).StatusCode);
        var codigo = Totp.Codigo(chave, relogio.Passo);
        var r = await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: codigo)); r.EnsureSuccessStatusCode();
        var autorizacao = (await r.Content.ReadFromJsonAsync<RespostaDaDecisaoV1>())!;
        Assert.True(autorizacao.SegundoFator); Assert.Equal(Snapshot.Sha, autorizacao.Sha); Assert.Equal(Snapshot.Destino, autorizacao.Destino);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsync($"{Rota}/respostas/{autorizacao.Id}/recebida", null)).StatusCode);
        await Retrato(c, Aprovacao with { Id = "outra" });
        var outra = await Ler(c, "outra");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Rota + "/outra", new ResponderDecisaoV1(outra.SnapshotId, "aprovar", Codigo: codigo))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync($"{Rota}/respostas/{autorizacao.Id}/recebida", null)).StatusCode);
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
        var chave = aprovar ? await Ativar(c, relogio) : null;
        var d = await Ler(c, aprovar ? "aprovacao" : "pergunta");
        ResponderDecisaoV1 Pedido() => new(d.SnapshotId, aprovar ? "aprovar" : "responder", aprovar ? null : "Manter", chave is null ? null : Totp.Codigo(chave, relogio.Passo));
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
        Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync("/api/remoto/segundo-fator/preparar", new PrepararAutenticadorV1("errada"))).StatusCode);
        var chave = await Ativar(c, relogio);
        Assert.True((await c.GetFromJsonAsync<EstadoDoAutenticadorV1>("/api/remoto/segundo-fator"))!.Ativo);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/remoto/segundo-fator/preparar", new PrepararAutenticadorV1(ApiDeTeste.NovaSenha))).StatusCode);
        await PrepararMaquina(c); await Retrato(c, Aprovacao);
        var d = await Ler(c, "aprovacao");
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: "abcdef"))).StatusCode);
        var bloqueada = await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: Totp.Codigo(chave, relogio.Passo)));
        Assert.Equal(HttpStatusCode.Forbidden, bloqueada.StatusCode);
        Assert.Contains("Limite de tentativas", await bloqueada.Content.ReadAsStringAsync());
        Assert.Empty((await c.GetFromJsonAsync<RespostaDaDecisaoV1[]>(Rota + "/respostas"))!);
        relogio.Avancar(301); await PrepararMaquina(c);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync(Rota + "/aprovacao", new ResponderDecisaoV1(d.SnapshotId, "aprovar", Codigo: Totp.Codigo(chave, relogio.Passo)))).StatusCode);
    }
}
