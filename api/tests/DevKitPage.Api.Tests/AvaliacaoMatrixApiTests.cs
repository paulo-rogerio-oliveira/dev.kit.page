using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKit.Matrix;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api.Tests;

public sealed class AvaliacaoMatrixApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();
    private static string Rota(DocumentoMatrix d) => $"/api/matrix/{d.Aplicacao}/{d.Tipo}/{d.Id}";
    private static Task<HttpResponseMessage> Gravar(HttpClient c, DocumentoMatrix d, int versao = 0, bool aplicar = true)
        => c.PutAsJsonAsync(Rota(d) + "?aplicar=" + aplicar.ToString().ToLowerInvariant(), new EdicaoMatrix(versao, d));
    private static DocumentoMatrix Matriz() => new()
    {
        Aplicacao = "app", Tipo = "matriz", Id = "demanda", Matriz = new(1, [new("avaliacao", "Avaliação")], [new("api", "API"), new("cli", "CLI")],
            [new("c1", "avaliacao", "api", "verificar", true, "Validar contrato", "pedido", [new("task-1")], "Ida e volta", "log"),
             new("c2", "avaliacao", "cli", "verificar", true, "Validar registro", "pedido", [new("task-2")], "Registro", "log")])
    };

    private async Task<HttpClient> Preparar()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Matrix avaliação");
        var (dev, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "dev@matrix-avaliacao.test");
        dev.DefaultRequestHeaders.Add("X-DevKit-Matrix-Avaliacao", "1");
        var matriz = Matriz().Matriz!;
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, new() { Aplicacao = "app", Id = "app", Mapa = new("App", ["repo"], matriz.Componentes, matriz.Layers, []) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, Matriz())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dev.PostAsJsonAsync("/api/remoto/heartbeat",
            new HeartbeatV1(RemotoV1.Contrato, "matrix-host", "Demonstração", "151", RemotoV1.OrigemServico, true, RemotoV1.AgenteAtivo, true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dev.PutAsJsonAsync("/api/remoto/retrato", new RetratoV1(RemotoV1.Contrato, "matrix-host", DateTimeOffset.UtcNow,
            [], [new FluxoNoRetratoV1("implementador", "Task", "Implementação", "executando")], []))).StatusCode);
        return dev;
    }

    private static DocumentoMatrix Recibo()
    {
        var shas = new Dictionary<string, string> { ["repo"] = new('a', 40) };
        var entrega = JsonSerializer.Serialize(new { Conferencia = new { Ok = true }, Repositorios = new[] { new { Name = "repo", Delivery = new { Head = shas["repo"] } } } });
        return new() { Aplicacao = "app", Tipo = "evento", Id = RegrasAvaliacaoMatrix.IdDoEvento("gatilho:123", "demanda", "implementador"),
            Evento = new("gatilho:123", "demanda", "matrix-host", "implementador", 7, 1, 1, 1, ["task-1"], ["c1"], shas, entrega) };
    }

    private static DocumentoMatrix Resultado(DocumentoMatrix recibo)
    {
        var e = recibo.Evento!;
        return new() { Aplicacao = "app", Tipo = "registro", Id = "demanda", Registro = new("c1", 1, "task-1", "avaliacao", "atendido", "Conferido com pedido e recorte", ["log"], e.Shas)
            { Evento = e.Evento, SessaoAvaliada = e.SessaoAvaliada, Avaliador = e.Avaliador, RevisaoEfetiva = e.RevisaoEfetiva,
                RevisaoDoContexto = e.RevisaoDoContexto, RevisaoDaEntrega = e.RevisaoDaEntrega } };
    }

    [Fact]
    public async Task Recibo_exige_host_e_sessao_do_solicitante_fica_imutavel_e_registro_confere_todos_os_vinculos()
    {
        var dev = await Preparar(); var recibo = Recibo(); var resultado = Resultado(recibo);
        Assert.Equal(HttpStatusCode.NotFound, (await Gravar(dev, resultado)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Gravar(dev, recibo with { Evento = recibo.Evento! with { Maquina = "outra-maquina" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Gravar(dev, recibo with { Id = RegrasAvaliacaoMatrix.IdDoEvento("gatilho:123", "demanda", "outra-task"), Evento = recibo.Evento! with { SessaoAvaliada = "outra-task" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, recibo, aplicar: false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await dev.GetAsync(Rota(recibo))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, recibo)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Gravar(dev, recibo, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Gravar(dev, resultado with { Registro = resultado.Registro! with { Avaliador = 99 } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Gravar(dev, resultado with { Registro = resultado.Registro! with { Shas = new() { ["repo"] = new('b', 40) } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Gravar(dev, resultado with { Registro = resultado.Registro! with { Celula = "c2", Responsavel = "task-2" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, resultado)).StatusCode);
        var persistido = Assert.Single((await dev.GetFromJsonAsync<RevisaoMatrix[]>(Rota(resultado)))!).Documento.Registro!;
        Assert.Equal(recibo.Evento!.RevisaoDaEntrega, persistido.RevisaoDaEntrega);
        Assert.Equal(7, persistido.Avaliador);
        dev.DefaultRequestHeaders.Remove("X-DevKit-Matrix-Avaliacao");
        using var antigo = JsonDocument.Parse(await dev.GetStringAsync(Rota(resultado)));
        var legado = antigo.RootElement[0].GetProperty("documento").GetProperty("registro");
        Assert.False(legado.TryGetProperty("evento", out _));
        Assert.False(legado.TryGetProperty("avaliador", out _));
        Assert.Equal("atendido", legado.GetProperty("resultado").GetString());
        Assert.DoesNotContain((await dev.GetFromJsonAsync<CabecalhoMatrix[]>("/api/matrix"))!, c => c.Tipo == "evento");
        Assert.Equal(HttpStatusCode.NotFound, (await dev.GetAsync(Rota(recibo))).StatusCode);
    }

    [Fact]
    public async Task Transferencia_e_nova_avaliacao_persistidas_comprovam_o_dono_atual_preservando_o_historico()
    {
        var dev = await Preparar(); var recibo = Recibo(); var resultado = Resultado(recibo);
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, recibo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, resultado)).StatusCode);
        var matriz = Matriz();
        matriz = matriz with { Matriz = matriz.Matriz! with { Celulas = matriz.Matriz.Celulas
            .Select(c => c.Id == "c1" ? c with { Responsaveis = [new("task-2")] } : c).ToArray() } };
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, matriz, 1)).StatusCode);
        var novoRecibo = recibo with { Id = RegrasAvaliacaoMatrix.IdDoEvento("gatilho:124", "demanda", "implementador"),
            Evento = recibo.Evento! with { Evento = "gatilho:124", Revisao = 2, RevisaoEfetiva = 2, RevisaoDoContexto = 2,
                Responsaveis = ["task-2"], Celulas = ["c1", "c2"] } };
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, novoRecibo)).StatusCode);
        var novo = Resultado(novoRecibo);
        novo = novo with { Registro = novo.Registro! with { RevisaoDaDefinicao = 2, Responsavel = "task-2" } };
        var historico = (await dev.GetFromJsonAsync<RevisaoMatrix[]>(Rota(matriz)))!;
        var antigos = (await dev.GetFromJsonAsync<RevisaoMatrix[]>(Rota(resultado)))!;
        Assert.StartsWith("A avaliar", RegrasAvaliacaoMatrix.EstadoDaCelula(matriz.Matriz!.Celulas[0], antigos, historico));
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, novo, 1)).StatusCode);
        var registros = (await dev.GetFromJsonAsync<RevisaoMatrix[]>(Rota(resultado)))!;
        Assert.Equal(2, registros.Length);
        Assert.Equal("task-1", registros[0].Documento.Registro!.Responsavel);
        Assert.NotEmpty(RegrasAvaliacaoMatrix.MotivoDaObsolescencia(registros[0].Documento.Registro!, historico));
        Assert.Empty(RegrasAvaliacaoMatrix.MotivoDaObsolescencia(registros[1].Documento.Registro!, historico));
        Assert.Equal("Comprovado", RegrasAvaliacaoMatrix.EstadoDaCelula(matriz.Matriz.Celulas[0], registros, historico));
    }

    [Fact]
    public async Task Revisao_alheia_preserva_avaliacao_e_mudanca_concorrente_do_recorte_recusa_novo_resultado()
    {
        var dev = await Preparar(); var recibo = Recibo(); var resultado = Resultado(recibo);
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, recibo)).StatusCode);
        var matriz = Matriz();
        var alheia = matriz with { Matriz = matriz.Matriz! with { Celulas = matriz.Matriz.Celulas.Select(c => c.Id == "c2" ? c with { Criterio = "Outra task" } : c).ToArray() } };
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, alheia, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, resultado)).StatusCode);
        var propria = alheia with { Matriz = alheia.Matriz! with { Celulas = alheia.Matriz.Celulas.Select(c => c.Id == "c1" ? c with { Responsaveis = [new("task-2")] } : c).ToArray() } };
        Assert.Equal(HttpStatusCode.OK, (await Gravar(dev, propria, 2)).StatusCode);
        var recusada = await Gravar(dev, resultado, 1);
        Assert.Equal(HttpStatusCode.BadRequest, recusada.StatusCode);
        Assert.Contains("Recorte obsoleto", await recusada.Content.ReadAsStringAsync());
        Assert.Single((await dev.GetFromJsonAsync<RevisaoMatrix[]>(Rota(resultado)))!);
        var historico = (await dev.GetFromJsonAsync<RevisaoMatrix[]>(Rota(matriz)))!;
        Assert.Contains("revisão 3", RegrasAvaliacaoMatrix.MotivoDaObsolescencia(resultado.Registro!, historico));
    }
}
