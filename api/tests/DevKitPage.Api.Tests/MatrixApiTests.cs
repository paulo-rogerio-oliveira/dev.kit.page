using System.Net;
using System.Net.Http.Json;
using DevKit.Matrix;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class MatrixApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();
    private static string Rota(string tipo = "mapa", string id = "app", string app = "app") => $"/api/matrix/{app}/{tipo}/{id}";
    private static DocumentoMatrix Mapa(string projeto = "", string app = "app") => new()
    {
        Aplicacao = app, Id = app, Projeto = projeto,
        Mapa = new("Aplicação", ["repo-a", "repo-b"], [new("chat", "Chat")], [new("web", "Web")], [])
    };
    private static DocumentoMatrix Matriz(string projeto = "", string app = "app", string id = "demanda") => new()
    {
        Aplicacao = app, Id = id, Tipo = "matriz", Projeto = projeto,
        Matriz = new(1, [new("chat", "Chat")], [new("web", "Web")],
            [new("c1", "chat", "web", "verificar", true, "Compartilhado", "rule:28", [new("task-1")], "Contexto", "log")])
    };
    private static Task<HttpResponseMessage> Editar(HttpClient cliente, DocumentoMatrix d, int versao = 0, bool aplicar = true)
        => cliente.PutAsJsonAsync(Rota(d.Tipo, d.Id, d.Aplicacao) + (aplicar ? "?aplicar=true" : ""), new EdicaoMatrix(versao, d));

    [Fact]
    public async Task Avulsa_simula_aplica_le_historico_conflita_e_isola_dono_aplicacao_e_demanda()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Matrix");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@matrix.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@matrix.com");
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa(), aplicar: false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync(Rota())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync(Rota())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Editar(bia, Mapa(), 1)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Editar(ana, Mapa(), 0)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Matriz())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa(), 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa(app: "outra"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Matriz(id: "outra-demanda"))).StatusCode);
        var mapa = await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota());
        var matriz = await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "demanda"));
        Assert.Equal(2, mapa!.Length);
        Assert.Equal(1, Assert.Single(matriz!).Documento.Matriz!.RevisaoDoMapa);
        Assert.NotEmpty(mapa[0].Autor);
        Assert.Equal(HttpStatusCode.BadRequest, (await Editar(ana, Mapa() with { Contrato = 2 }, 2)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync(Rota("matriz", "outro") + "?aplicar=true", new EdicaoMatrix(0, Matriz()))).StatusCode);
    }

    [Fact]
    public async Task Projeto_usa_mesmo_acl_e_cliente_anterior_nao_apaga_a_matrix()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Matrix ACL");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@acl-matrix.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@acl-matrix.com");
        var envio = new EnvioDeProjetoV1("projeto", "Projeto", "empresa", "", ["repo-a"], "feature/projeto", false, "", [], false, 1, "main", "rascunho", null, []);
        var projeto = (await (await ana.PutAsJsonAsync("/api/projetos", envio)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa("projeto"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Matriz("projeto"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync(Rota())).StatusCode);
        await ana.PostAsJsonAsync($"/api/projetos/{projeto.Id}/acessos", new ConcessaoDeAcessoV1("bia@acl-matrix.com", "leitor"));
        Assert.Equal(HttpStatusCode.OK, (await bia.GetAsync(Rota())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Editar(bia, Matriz("projeto"), 1)).StatusCode);
        var registro = new DocumentoMatrix { Aplicacao = "app", Tipo = "registro", Id = "demanda", Projeto = "projeto",
            Registro = new("c1", 1, "task-1", "implementacao", "atendido", "Validado", ["log"], new()) };
        Assert.Equal(HttpStatusCode.Forbidden, (await Editar(bia, registro)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, registro)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync($"/api/projetos/{projeto.Id}", envio with { Versao = 1, Nome = "Cliente anterior" })).StatusCode);
        Assert.Single((await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "demanda")))!);
        Assert.Single((await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("registro", "demanda")))!);
    }

    [Fact]
    public async Task Identidade_do_solicitante_e_sessao_da_maquina_sao_conferidas_no_vinculo()
    {
        var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync(Rota())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Editar(anonimo, Mapa())).StatusCode);
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Matrix vínculo");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@vinculo-matrix.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@vinculo-matrix.com");
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Matriz())).StatusCode);
        const string maquina = "3f2b9c0e5d7a";
        var heartbeat = await ana.PostAsJsonAsync("/api/remoto/heartbeat", new HeartbeatV1(RemotoV1.Contrato, maquina,
            "Demonstração", "144", RemotoV1.OrigemServico, true, RemotoV1.AgenteAtivo, true));
        Assert.Equal(HttpStatusCode.OK, heartbeat.StatusCode);
        Assert.Contains("matrix-v1", (await heartbeat.Content.ReadFromJsonAsync<HeartbeatRecebidoV1>())!.Recursos!);
        var retrato = new RetratoV1(RemotoV1.Contrato, maquina, DateTimeOffset.UtcNow, [],
            [new FluxoNoRetratoV1("sessao", "Task", "Implementação", "executando")], []);
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync("/api/remoto/retrato", retrato)).StatusCode);
        var vinculo = new DocumentoMatrix { Aplicacao = "app", Tipo = "vinculo", Id = "demanda",
            Vinculo = new("task-1", "sessao", Maquina: maquina) };
        Assert.Equal(HttpStatusCode.NotFound, (await Editar(bia, vinculo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, vinculo)).StatusCode);
        Assert.Equal("sessao", (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("vinculo", "demanda")))![0].Documento.Vinculo!.Sessao);
    }

    [Fact]
    public async Task Concorrencia_rejeita_edicao_vencida_e_vinculo_sem_identidade_de_maquina()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Matrix CAS");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@cas-matrix.com");
        await Editar(ana, Mapa()); await Editar(ana, Matriz());
        var respostas = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Editar(ana, Mapa(), 1)));
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var vinculo = new DocumentoMatrix { Aplicacao = "app", Tipo = "vinculo", Id = "demanda", Vinculo = new("task-1", "sessao", Maquina: "desconhecida") };
        Assert.Equal(HttpStatusCode.NotFound, (await Editar(ana, vinculo)).StatusCode);
        var invalida = Matriz() with { Matriz = Matriz().Matriz! with { RevisaoDoMapa = 99 } };
        Assert.Equal(HttpStatusCode.BadRequest, (await Editar(ana, invalida, 1)).StatusCode);
    }
}
