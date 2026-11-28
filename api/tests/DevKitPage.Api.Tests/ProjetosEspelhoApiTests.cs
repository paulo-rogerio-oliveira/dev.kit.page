using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// (boards-estados, item 5) O espelho no Azure Boards e a marca de disparo no contrato de projetos: vão e voltam sem perda,
/// o cliente antigo que não os envia não apaga o gravado, o formato fora do contrato é recusado sem gravar, e as edições
/// pelo site (item e skill do pacote) os preservam. A lista de resumos devolve a marca sem ler o documento.
/// </summary>
public sealed class ProjetosEspelhoApiTests : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Disparo = new(2026, 11, 27, 12, 30, 0, TimeSpan.Zero);
    private static readonly EstadosNoBoardV1 Estados = new(new("", "", ""), new("", "", "Resolved"));

    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static EnvioDeProjetoV1 Envio(string situacao = "aprovado") => new("boards-estados", "Projetos em qualquer nível",
        "usuario", "", ["https://github.com/acme/portal"], "feature/dev/projeto-boards-estados", false, "Definição", [], true, 2,
        "develop", situacao, null,
        [new(1, "Espelho configurável", "Descrição", "14", [], ["api"], [], 1, 813, "planejado", Tipo: "User Story",
            Agente: "codex", Modelo: "gpt-6.1-sol")],
        FeatureId: 812, Board: "criar", Agente: "codex", Modelo: "gpt-6.1-sol", Modelos: [new("codex", "gpt-6.1-sol")],
        NivelNoBoard: "feature", EstadosNoBoard: Estados, DisparadoEm: Disparo);

    private static void TemOEspelhoEAMarca(ProjetoV1 projeto, string nivel = "feature", string finalizacaoDosItens = "Resolved")
    {
        Assert.Equal(nivel, projeto.NivelNoBoard);
        Assert.Equal(finalizacaoDosItens, projeto.EstadosNoBoard?.Itens?.Finalizacao);
        Assert.Equal("", projeto.EstadosNoBoard?.Pai?.Finalizacao);
        Assert.Equal(Disparo, projeto.DisparadoEm);
        Assert.Equal(812, projeto.FeatureId);
    }

    [Fact]
    public async Task Espelho_e_marca_vao_e_voltam_e_o_cliente_antigo_nao_os_apaga()
    {
        var cliente = await _api.AdminAsync();
        var criada = await cliente.PutAsJsonAsync("/api/projetos", Envio());
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";
        TemOEspelhoEAMarca(projeto);
        TemOEspelhoEAMarca((await cliente.GetFromJsonAsync<ProjetoV1>(rota))!);
        TemOEspelhoEAMarca(Assert.Single((await cliente.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!));
        var resumo = Assert.Single((await cliente.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos"))!);
        Assert.Equal(Disparo, resumo.DisparadoEm);

        // O cliente antigo (o dev.kit de antes) não conhece os campos: o envio dele não os apaga.
        var antigo = JsonSerializer.SerializeToNode(Envio(), Json)!.AsObject();
        antigo.Remove("nivelNoBoard");
        antigo.Remove("estadosNoBoard");
        antigo.Remove("disparadoEm");
        antigo["versao"] = projeto.Versao;
        antigo["nome"] = "Renomeado pelo cliente antigo";
        Assert.Equal(HttpStatusCode.OK, (await cliente.PutAsJsonAsync(rota, antigo)).StatusCode);
        projeto = (await cliente.GetFromJsonAsync<ProjetoV1>(rota))!;
        Assert.Equal("Renomeado pelo cliente antigo", projeto.Nome);
        TemOEspelhoEAMarca(projeto);
        Assert.Equal(Disparo, Assert.Single((await cliente.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos"))!).DisparadoEm);

        // Os campos nulos também são "não enviei": preservam. O explícito vale: nível vazio (feature) e estados novos inteiros.
        var nulos = JsonSerializer.SerializeToNode(Envio() with { Versao = projeto.Versao, NivelNoBoard = null, EstadosNoBoard = null, DisparadoEm = null }, Json)!;
        Assert.Equal(HttpStatusCode.OK, (await cliente.PutAsJsonAsync(rota, nulos)).StatusCode);
        projeto = (await cliente.GetFromJsonAsync<ProjetoV1>(rota))!;
        TemOEspelhoEAMarca(projeto);
        var trocado = await cliente.PutAsJsonAsync(rota, Envio() with { Versao = projeto.Versao, NivelNoBoard = " Epic ",
            EstadosNoBoard = new(Itens: new("Proposed", "In Progress", "Review")) });
        Assert.Equal(HttpStatusCode.OK, trocado.StatusCode);
        projeto = (await trocado.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal("epic", projeto.NivelNoBoard);
        Assert.Null(projeto.EstadosNoBoard?.Pai);
        Assert.Equal(("Proposed", "In Progress", "Review"),
            (projeto.EstadosNoBoard!.Itens!.Inicial, projeto.EstadosNoBoard.Itens.Execucao, projeto.EstadosNoBoard.Itens.Finalizacao));
        var feature = await cliente.PutAsJsonAsync(rota, Envio() with { Versao = projeto.Versao, NivelNoBoard = "" });
        Assert.Equal("", (await feature.Content.ReadFromJsonAsync<ProjetoV1>())!.NivelNoBoard);
    }

    [Theory]
    [InlineData("task", null)]
    [InlineData("feature", "Active\nClosed")]
    [InlineData("feature", "longo")]
    public async Task Espelho_fora_do_contrato_e_recusado_sem_gravar(string nivel, string? estado)
    {
        var cliente = await _api.AdminAsync();
        var projeto = (await (await cliente.PutAsJsonAsync("/api/projetos", Envio())).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";
        var finalizacao = estado == "longo" ? new string('x', ProjetosV1.TamanhoMaximoDoEstado + 1) : estado;

        var recusado = await cliente.PutAsJsonAsync(rota, Envio() with { Versao = projeto.Versao, NivelNoBoard = nivel,
            EstadosNoBoard = new(Itens: new(Finalizacao: finalizacao)), DisparadoEm = Disparo.AddDays(1) });

        Assert.Equal(HttpStatusCode.BadRequest, recusado.StatusCode);
        var relido = (await cliente.GetFromJsonAsync<ProjetoV1>(rota))!;
        Assert.Equal(projeto.Versao, relido.Versao);
        TemOEspelhoEAMarca(relido);
    }

    [Fact]
    public async Task Versao_velha_com_os_campos_novos_e_conflito_e_nao_muda_o_gravado()
    {
        var cliente = await _api.AdminAsync();
        var projeto = (await (await cliente.PutAsJsonAsync("/api/projetos", Envio())).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";
        Assert.Equal(HttpStatusCode.OK, (await cliente.PutAsJsonAsync(rota, Envio() with { Versao = projeto.Versao })).StatusCode);

        var velho = await cliente.PutAsJsonAsync(rota, Envio() with { Versao = projeto.Versao, NivelNoBoard = "requisito",
            DisparadoEm = Disparo.AddDays(2) });

        Assert.Equal(HttpStatusCode.Conflict, velho.StatusCode);
        TemOEspelhoEAMarca((await cliente.GetFromJsonAsync<ProjetoV1>(rota))!);
    }

    [Fact]
    public async Task Edicao_do_item_e_da_skill_pelo_site_preservam_o_espelho_e_a_marca()
    {
        var cliente = await _api.AdminAsync();
        var envio = Envio() with { SkillDoPacote = "pacote-x", SkillsDoPacote = [new("pacote-x", "somente-comando"), new("pacote-y", "com-parametros")] };
        var projeto = (await (await cliente.PutAsJsonAsync("/api/projetos", envio)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";

        var skill = await cliente.PutAsJsonAsync(rota + "/skill-do-pacote", new EdicaoDaSkillDoPacoteV1(projeto.Versao, "pacote-y"));
        Assert.Equal(HttpStatusCode.OK, skill.StatusCode);
        projeto = (await skill.Content.ReadFromJsonAsync<ProjetoV1>())!;
        TemOEspelhoEAMarca(projeto);

        var item = await cliente.PutAsJsonAsync(rota + "/itens/1", new EdicaoRemotaDoItemV1(projeto.Versao, "Espelho revisto", "Nova",
            "14", "codex", "gpt-6.1-sol", [], ["api"], 1));
        Assert.Equal(HttpStatusCode.OK, item.StatusCode);
        projeto = (await item.Content.ReadFromJsonAsync<ProjetoV1>())!;
        // Editar o plano o devolve à aprovação; o espelho e a marca do host ficam como estavam (o host decide o que muda).
        Assert.Equal("aguardando", projeto.Situacao);
        TemOEspelhoEAMarca(projeto);
        Assert.Equal(813, projeto.Itens.Single().WorkItemId);
    }

    [Fact]
    public async Task Projeto_sem_espelho_nem_marca_volta_sem_eles()
    {
        var cliente = await _api.AdminAsync();
        var semCampos = Envio("aguardando") with { NivelNoBoard = null, EstadosNoBoard = null, DisparadoEm = null, FeatureId = 0, Board = "nao" };
        var criada = await cliente.PutAsJsonAsync("/api/projetos", semCampos);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;

        Assert.Null(projeto.NivelNoBoard);
        Assert.Null(projeto.EstadosNoBoard);
        Assert.Null(projeto.DisparadoEm);
        Assert.Null(Assert.Single((await cliente.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos"))!).DisparadoEm);
        var json = await cliente.GetFromJsonAsync<JsonObject>($"/api/projetos/{projeto.Id}");
        Assert.True(json!.ContainsKey("disparadoEm"));
        Assert.Null(json["disparadoEm"]);
    }
}
