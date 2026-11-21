using System.Net;
using System.Net.Http.Json;
using DevKit.Matrix;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

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
    public async Task Catalogo_de_sincronizacao_preserva_acl_revisao_e_identidade_da_aplicacao()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/matrix")).StatusCode);
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Matrix catálogo");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@catalogo-matrix.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@catalogo-matrix.com");
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Matriz())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa(), 1)).StatusCode);
        var catalogo = (await ana.GetFromJsonAsync<CabecalhoMatrix[]>("/api/matrix"))!;
        Assert.Equal(2, catalogo.Length);
        Assert.Equal(2, Assert.Single(catalogo, c => c.Tipo == "mapa").Versao);
        Assert.All(catalogo, c => Assert.Equal("app", c.Aplicacao));
        Assert.Empty((await bia.GetFromJsonAsync<CabecalhoMatrix[]>("/api/matrix"))!);
    }

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
        Assert.Equal(2, (await bia.GetFromJsonAsync<CabecalhoMatrix[]>("/api/matrix"))!.Length);
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
        var retrato = new RetratoV1(RemotoV1.Contrato, maquina, DateTimeOffset.UtcNow,
            [new ProjetoNoRetratoV1("outro-projeto", "Outro", ProjetosV1.Aprovado, 1,
                [new ItemNoRetratoV1(1, "Item", ProjetosV1.ItemRodando, 1)])],
            [new FluxoNoRetratoV1("sessao", "Task", "Implementação", "executando"),
                new FluxoNoRetratoV1("sessao-item", "Task do item", "Implementação", "executando", "outro-projeto", 1)], []);
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync("/api/remoto/retrato", retrato)).StatusCode);
        var vinculo = new DocumentoMatrix { Aplicacao = "app", Tipo = "vinculo", Id = "demanda",
            Vinculo = new("task-1", "sessao", Maquina: maquina) };
        Assert.Equal(HttpStatusCode.NotFound, (await Editar(bia, vinculo)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Editar(ana, vinculo with { Vinculo = new("task-1", "sessao-item", Maquina: maquina) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, vinculo)).StatusCode);
        Assert.Equal("sessao", (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("vinculo", "demanda")))![0].Documento.Vinculo!.Sessao);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Finalizacao_sem_item_exige_recibo_do_host_atual_e_preserva_vinculo_anterior_na_retomada(string novaLinha)
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Matrix finalização");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@final-matrix.com");
        ana.DefaultRequestHeaders.Add("X-DevKit-Matrix-Avaliacao", "1");
        var politica = new PoliticaDoProjetoV1("projeto", "finalizacao-unica", 1, "1", DateTimeOffset.UtcNow, "escolha")
        { IdentidadeDoFluxo = "fluxo:final", RevisaoDoFluxo = "rev", ModeloDaFinalizacao = new("claude", "sonnet") };
        var envio = new EnvioDeProjetoV1("projeto", "Projeto", "empresa", "", ["repo-a"], "feature/projeto", false, "", [], false, 1,
            "develop", "aprovado", null, [new(1, "Item", "", "item", [], [], [], 1, 0, "integrado")]) { PoliticaDeFinalizacao = politica };
        (await ana.PutAsJsonAsync("/api/projetos", envio)).EnsureSuccessStatusCode();
        (await Editar(ana, Mapa("projeto"))).EnsureSuccessStatusCode();
        (await Editar(ana, Matriz("projeto"))).EnsureSuccessStatusCode();
        const string maquina = "maquina-final";
        (await ana.PostAsJsonAsync("/api/remoto/heartbeat", new HeartbeatV1(1, maquina, "Desktop", "8", "servico", true, "ativo", true)))
            .EnsureSuccessStatusCode();
        var agora = DateTimeOffset.UtcNow;
        var fluxo = new FluxoNoRetratoV1("exec-final", "Finalização", "Finalização", "executando", "projeto");
        async Task Publicar(FluxoNoRetratoV1 f)
        {
            agora = agora.AddSeconds(1);
            (await ana.PutAsJsonAsync("/api/remoto/retrato", new RetratoV1(1, maquina, agora, [], [f], []))).EnsureSuccessStatusCode();
        }
        var vinculo = new DocumentoMatrix { Aplicacao = "app", Tipo = "vinculo", Id = "demanda", Projeto = "projeto",
            Vinculo = new("task-1", fluxo.Id, Maquina: maquina) };
        await Publicar(fluxo); // O cliente antigo/declarativo não oferece recibo.
        Assert.Equal(HttpStatusCode.BadRequest, (await Editar(ana, vinculo)).StatusCode);
        var recibo = new ReciboDaFinalizacaoV1(1, fluxo.Id, 1, "projeto", "feature/projeto", "develop", 1, "fluxo:final", "rev",
            "claude", "sonnet", new Dictionary<string, string> { [RegrasDaFinalizacao.IdDoRepositorio("repo-a")] = new('a', 40) });
        await Publicar(fluxo with { Finalizacao = recibo with { RevisaoDaPolitica = 2 } });
        Assert.Equal(HttpStatusCode.BadRequest, (await Editar(ana, vinculo)).StatusCode);
        await Publicar(fluxo with { Finalizacao = recibo });
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, vinculo, aplicar: false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync(Rota("vinculo", "demanda"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, vinculo)).StatusCode);
        var revisaoPortatil = RegrasVerificacaoFinalMatrix.Revisao(recibo).Replace("\n", novaLinha, StringComparison.Ordinal);
        var eventoPortatil = "finalizacao:" + recibo.Execucao + ":" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(revisaoPortatil)));
        var verificacao = new EventoMatrix(eventoPortatil, "demanda", maquina, fluxo.Id,
            0, 1, 1, 1, ["task-1"], ["c1"], recibo.Shas.ToDictionary(p => p.Key, p => p.Value),
            revisaoPortatil) { Finalizacao = recibo };
        var reciboMatrix = new DocumentoMatrix { Aplicacao = "app", Tipo = "evento", Projeto = "projeto",
            Id = RegrasAvaliacaoMatrix.IdDoEvento(verificacao.Evento, "demanda", fluxo.Id), Evento = verificacao };
        var respostaDoRecibo = await Editar(ana, reciboMatrix);
        Assert.True(respostaDoRecibo.IsSuccessStatusCode, await respostaDoRecibo.Content.ReadAsStringAsync());
        var resultadoFinal = new DocumentoMatrix { Aplicacao = "app", Tipo = "registro", Id = "demanda", Projeto = "projeto",
            Registro = new("c1", 1, "task-1", RegrasVerificacaoFinalMatrix.Tipo, "atendido", "Cenários integrados conferidos", ["log final"], verificacao.Shas)
            { Evento = verificacao.Evento, SessaoAvaliada = fluxo.Id, RevisaoEfetiva = 1, RevisaoDoContexto = 1,
                RevisaoDaEntrega = verificacao.RevisaoDaEntrega } };
        (await Editar(ana, resultadoFinal)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Editar(ana, resultadoFinal with
            { Registro = resultadoFinal.Registro with { Tipo = "avaliacao", Avaliador = 1 } }, 1)).StatusCode);
        var retomada = fluxo with { Id = "exec-retomada", Finalizacao = recibo with { Sessao = "exec-retomada", Execucao = 2 } };
        await Publicar(retomada);
        Assert.Equal(HttpStatusCode.BadRequest, (await Editar(ana, resultadoFinal, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, vinculo with { Vinculo = new("task-1", retomada.Id, Maquina: maquina) }, 1)).StatusCode);
        var historico = (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("vinculo", "demanda")))!;
        Assert.Equal(2, historico.Length);
        Assert.Equal("exec-final", historico[0].Documento.Vinculo!.Sessao);
        Assert.Equal("exec-retomada", historico[1].Documento.Vinculo!.Sessao);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task Transferencia_versionada_preserva_item_e_cliente_antigo_e_vincula_a_finalizacao_fora_do_grafo(string novaLinha)
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Transferência formal");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@transferencia-matrix.com");
        var envio = new EnvioDeProjetoV1("projeto", "Projeto", "empresa", "", ["repo-a"], "feature/projeto", false, "", [], false, 1,
            "develop", "aprovado", null, [new(1, "Implementação", "", "item", [], [], [], 1, 0, "integrado"),
                new(6, "Validação final", "Regressão e CI do conjunto", "avulsa", [], [], [1], 2, 0, "planejado")]);
        var projeto = (await (await ana.PutAsJsonAsync("/api/projetos", envio)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        (await Editar(ana, Mapa("projeto"))).EnsureSuccessStatusCode();
        var matriz = Matriz("projeto");
        matriz = matriz with { Matriz = matriz.Matriz! with { Celulas = [matriz.Matriz.Celulas[0] with { Responsaveis = [new("validacao-6", 6)] }] } };
        (await Editar(ana, matriz)).EnsureSuccessStatusCode();
        (await Editar(ana, matriz with { Id = "segunda" })).EnsureSuccessStatusCode();
        var snapshot = FonteDaTransferenciaMatrix.Retratar(envio, envio.Itens[1]);
        var revisao = (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "demanda")))!.Last();
        var proposta = RegrasDaTransferenciaMatrix.Preparar(revisao, snapshot, "finalizacao", "Validação do conjunto", "Plano aprovado r3", DateTimeOffset.UtcNow);
        var transferenciaPortatil = Assert.Single(proposta.Documento.Matriz!.Transferencias!);
        proposta = proposta with { Documento = proposta.Documento with { Matriz = proposta.Documento.Matriz! with
        {
            Transferencias = [transferenciaPortatil with
            { SnapshotDoItem = transferenciaPortatil.SnapshotDoItem.Replace("\n", novaLinha, StringComparison.Ordinal) }]
        } } };
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, proposta.Documento, 1, aplicar: false)).StatusCode);
        Assert.Single((await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "demanda")))!);
        const string maquina = "maquina-transferencia";
        (await ana.PostAsJsonAsync("/api/remoto/heartbeat", new HeartbeatV1(1, maquina, "Desktop", "8", "servico", true, "ativo", true))).EnsureSuccessStatusCode();
        var agora = DateTimeOffset.UtcNow;
        async Task Publicar(params FluxoNoRetratoV1[] fluxos)
        {
            agora = agora.AddSeconds(1);
            (await ana.PutAsJsonAsync("/api/remoto/retrato", new RetratoV1(1, maquina, agora, [], fluxos, []))).EnsureSuccessStatusCode();
        }
        await Publicar(new FluxoNoRetratoV1("exec-item", "Validação", "Execução", "executando", "projeto", 6));
        Assert.Equal(HttpStatusCode.BadRequest, (await Editar(ana, proposta.Documento, 1)).StatusCode);
        await Publicar();
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@transferencia-matrix.com");
        (await ana.PostAsJsonAsync($"/api/projetos/{projeto.Id}/acessos", new ConcessaoDeAcessoV1("bia@transferencia-matrix.com", "editor"))).EnsureSuccessStatusCode();
        (await bia.PostAsJsonAsync("/api/remoto/heartbeat", new HeartbeatV1(1, "maquina-editora", "Desktop", "8", "servico", true, "ativo", true))).EnsureSuccessStatusCode();
        (await bia.PutAsJsonAsync("/api/remoto/retrato", new RetratoV1(1, "maquina-editora", agora,
            [], [new("exec-de-outra-editora", "Validação", "Execução", "executando", "projeto", 6)], []))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await Editar(ana, proposta.Documento, 1)).StatusCode);
        (await bia.PutAsJsonAsync("/api/remoto/retrato", new RetratoV1(1, "maquina-editora", agora.AddSeconds(1), [], [], []))).EnsureSuccessStatusCode();
        (await Editar(ana, proposta.Documento, 1)).EnsureSuccessStatusCode();
        var politica = new PoliticaDoProjetoV1("projeto", "finalizacao-unica", 1, "1", DateTimeOffset.UtcNow, "adoção explícita")
        { IdentidadeDoFluxo = "teste:final", RevisaoDoFluxo = "rev", ModeloDaFinalizacao = new("claude", "sonnet") };
        var adotar = envio with { Versao = projeto.Versao, PoliticaDeFinalizacao = politica };
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PutAsJsonAsync("/api/projetos", adotar)).StatusCode);
        var segunda = (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "segunda")))!.Last();
        var segundaTransferencia = RegrasDaTransferenciaMatrix.Preparar(segunda, snapshot, "finalizacao", "Validação do conjunto", "Plano aprovado r3", DateTimeOffset.UtcNow);
        (await Editar(ana, segundaTransferencia.Documento, 1)).EnsureSuccessStatusCode();
        var atual = (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "demanda")))!.Last();
        var antigo = atual.Documento with { Matriz = atual.Documento.Matriz! with { Transferencias = null } };
        (await Editar(ana, antigo, atual.Versao)).EnsureSuccessStatusCode();
        Assert.Single((await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "demanda")))!.Last().Documento.Matriz!.Transferencias!);
        var adotado = await ana.PutAsJsonAsync("/api/projetos", adotar);
        adotado.EnsureSuccessStatusCode();
        projeto = (await adotado.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal("planejado", projeto.Itens.Single(i => i.Numero == 6).Situacao);
        Assert.Equal(new[] { 1 }, projeto.ItensDoGrafo);
        var leituraDoPlano = (await ana.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{projeto.Id}"))!;
        Assert.Equal(new[] { 1 }, leituraDoPlano.ItensDoGrafo);
        Assert.Equal(2, leituraDoPlano.Itens.Count);
        var alterado = adotar with { Versao = projeto.Versao, Itens = adotar.Itens.Select(i => i.Numero == 6 ? i with { Descricao = "alteração silenciosa" } : i).ToArray() };
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PutAsJsonAsync("/api/projetos", alterado)).StatusCode);
        var recibo = new ReciboDaFinalizacaoV1(1, "exec-final", 1, "projeto", "feature/projeto", "develop", 1, "teste:final", "rev", "claude", "sonnet",
            new Dictionary<string, string> { [RegrasDaFinalizacao.IdDoRepositorio("repo-a")] = new('a', 40) });
        await Publicar(new FluxoNoRetratoV1("exec-final", "Finalização", "Finalização", "executando", "projeto") { Finalizacao = recibo });
        var vinculo = new DocumentoMatrix { Aplicacao = "app", Tipo = "vinculo", Id = "demanda", Projeto = "projeto",
            Vinculo = new("finalizacao", "exec-final", Maquina: maquina) };
        (await Editar(ana, vinculo)).EnsureSuccessStatusCode();
        var historico = (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "demanda")))!;
        Assert.Equal(6, historico[0].Documento.Matriz!.Celulas[0].Responsaveis[0].Item);
        Assert.Equal("finalizacao", historico[^1].Documento.Matriz!.Celulas[0].Responsaveis[0].Id);
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

    [Theory]
    [InlineData("adicionar-componente", false)]
    [InlineData("adicionar-layer", false)]
    [InlineData("editar-componente-alheio", false)]
    [InlineData("editar-layer-alheia", false)]
    [InlineData("remover-componente-alheio", false)]
    [InlineData("remover-layer-alheia", false)]
    [InlineData("reordenar", false)]
    [InlineData("revisao-do-mapa", false)]
    [InlineData("editar-componente-usado", true)]
    [InlineData("editar-layer-usada", true)]
    public async Task Eixo_so_afeta_celulas_relacionadas_e_preserva_avaliacoes_alheias(string caso, bool afetaPrimeira)
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Matrix eixos");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@eixos-matrix.com");
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa())).StatusCode);
        var matriz = Matriz().Matriz! with
        {
            Componentes = [new("chat", "Chat"), new("tasks", "Tasks"), new("sem-celulas", "Sem células")],
            Layers = [new("web", "Web"), new("cli", "CLI"), new("sem-celulas", "Sem células")],
            Celulas = [Matriz().Matriz!.Celulas[0], Matriz().Matriz!.Celulas[0] with
                { Id = "c2", Componente = "tasks", Layer = "cli", Responsaveis = [new("task-2")] }]
        };
        var documento = Matriz() with { Matriz = matriz };
        var criada = await Editar(ana, documento);
        Assert.Equal(HttpStatusCode.OK, criada.StatusCode);
        var origem = (await criada.Content.ReadFromJsonAsync<ResultadoMatrix>())!.Revisao;
        var registro = new RegistroMatrix("c1", 1, "task-1", "avaliacao", "atendido", "Validado", ["log"], new() { ["repo-a"] = new('a', 40) });
        var outro = registro with { Celula = "c2", Responsavel = "task-2" };
        var r = new DocumentoMatrix { Aplicacao = "app", Id = "demanda", Tipo = "registro", Registro = registro };
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, r)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, r with { Registro = outro }, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Editar(ana, Mapa(), 1)).StatusCode);
        var nova = caso switch
        {
            "adicionar-componente" => matriz with { Componentes = [.. matriz.Componentes, new("novo", "Novo")] },
            "adicionar-layer" => matriz with { Layers = [.. matriz.Layers, new("nova", "Nova")] },
            "editar-componente-alheio" => matriz with { Componentes = matriz.Componentes.Select(e => e.Id == "sem-celulas" ? e with { Orientacao = "Mudou" } : e).ToArray() },
            "editar-layer-alheia" => matriz with { Layers = matriz.Layers.Select(e => e.Id == "sem-celulas" ? e with { Nome = "Mudou" } : e).ToArray() },
            "remover-componente-alheio" => matriz with { Componentes = matriz.Componentes.Where(e => e.Id != "sem-celulas").ToArray() },
            "remover-layer-alheia" => matriz with { Layers = matriz.Layers.Where(e => e.Id != "sem-celulas").ToArray() },
            "reordenar" => matriz with { Componentes = matriz.Componentes.Reverse().ToArray(), Layers = matriz.Layers.Reverse().ToArray() },
            "revisao-do-mapa" => matriz with { RevisaoDoMapa = 2 },
            "editar-componente-usado" => matriz with { Componentes = matriz.Componentes.Select(e => e.Id == "chat" ? e with { Orientacao = "Mudou" } : e).ToArray() },
            _ => matriz with { Layers = matriz.Layers.Select(e => e.Id == "web" ? e with { Nome = "Mudou" } : e).ToArray() }
        };
        var gravada = await Editar(ana, documento with { Matriz = nova }, 1);
        Assert.Equal(HttpStatusCode.OK, gravada.StatusCode);
        var resultado = (await gravada.Content.ReadFromJsonAsync<ResultadoMatrix>())!;
        Assert.Equal(afetaPrimeira ? new[] { "c1" } : [], resultado.Mudancas.Celulas);
        Assert.Equal(afetaPrimeira ? new[] { "task-1" } : [], resultado.Mudancas.Responsaveis);
        var h = (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("matriz", "demanda")))!;
        Assert.Equal(!afetaPrimeira, RegrasMatrix.RegistroVigente(registro, origem, h[1]));
        Assert.True(RegrasMatrix.RegistroVigente(outro, origem, h[1]));
        var registros = (await ana.GetFromJsonAsync<RevisaoMatrix[]>(Rota("registro", "demanda")))!;
        Assert.Equal(2, registros.Length);
        Assert.All(registros, v => Assert.Equal(1, v.Documento.Registro!.RevisaoDaDefinicao));
    }
}
