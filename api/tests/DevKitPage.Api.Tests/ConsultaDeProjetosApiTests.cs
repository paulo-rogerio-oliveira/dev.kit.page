using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class ConsultaDeProjetosApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static EnvioDeProjetoV1 Envio() => new("leve", "Projeto leve", "empresa", "", ["repo"],
        "feature/projeto-leve", false, "Definição completa", [], true, 2, "release", "rascunho", null,
        [new(1, "Item preservado", "Descrição completa", "", [], [], [], 1, 0, "pergunta")])
    {
        BaseDoBranch = "develop", Agente = "codex", Modelo = "gpt-6.1-sol", Board = "nao",
        Modelos = [new("codex", "gpt-6.1-sol")],
    };

    [Fact]
    public async Task Resumo_respeita_acl_e_leitor_nao_consegue_gravar_nem_apagar_plano_omitido()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Resumos");
        var outra = await ApiDeTeste.EmpresaAsync(admin, "Outra");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@resumos.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@resumos.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(outra.CodigoDeAdesao, "caio@resumos.com");
        var criada = await ana.PutAsJsonAsync("/api/projetos", Envio());
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var completo = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var detalhe = $"/api/projetos/{completo.Id}";
        Assert.Empty((await bia.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos"))!);
        Assert.Empty((await caio.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync(detalhe)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync(detalhe + "/acessos",
            new ConcessaoDeAcessoV1("bia@resumos.com", "leitor"))).StatusCode);
        var resumo = Assert.Single((await bia.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos"))!);
        Assert.Equal((completo.Id, completo.Versao, "leitor", 1, true),
            (resumo.Id, resumo.Versao, resumo.Papel, resumo.TotalDeItens, resumo.TemPendencias));
        var json = JsonSerializer.SerializeToElement(resumo, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(json.TryGetProperty("itens", out _));
        Assert.False(json.TryGetProperty("definicao", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PutAsJsonAsync(detalhe, Envio() with { Versao = completo.Versao })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync(detalhe, resumo)).StatusCode);
        var relido = (await ana.GetFromJsonAsync<ProjetoV1>(detalhe))!;
        Assert.Equal(completo.Versao, relido.Versao);
        Assert.Equal(JsonSerializer.Serialize(completo.Itens), JsonSerializer.Serialize(relido.Itens));
        Assert.Equal(("develop", "release", "gpt-6.1-sol", "nao"),
            (relido.BaseDoBranch, relido.DestinoDaPr, relido.Modelo, relido.Board));
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync(detalhe)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync("/api/projetos/9223372036854775807")).StatusCode);
        using var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync("/api/projetos/resumos")).StatusCode);
    }

    [Fact]
    public async Task Resumo_reflete_edicao_sem_mudar_snapshot_antigo_e_detalhe_so_do_projeto_aberto()
    {
        var cliente = await _api.AdminAsync();
        var envio = Envio() with { Nivel = "usuario", Itens = [Envio().Itens[0] with
            { Design = new(true, 1, [new("app", "tela", 2, new string('a', 64))]), Agente = "codex", Modelo = "gpt-6.1-sol" }] };
        var criada = (await (await cliente.PutAsJsonAsync("/api/projetos", envio)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(HttpStatusCode.Created, (await cliente.PutAsJsonAsync("/api/projetos", envio with { Slug = "outro", Nome = "Outro" })).StatusCode);
        Assert.Equal(2, (await cliente.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos"))!.Length);
        var alterada = await cliente.PutAsJsonAsync($"/api/projetos/{criada.Id}", envio with
            { Versao = criada.Versao, Nome = "Renomeado", Itens = [] });
        Assert.Equal(HttpStatusCode.OK, alterada.StatusCode);
        var resumo = (await cliente.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos"))!.Single(p => p.Id == criada.Id);
        Assert.Equal(("Renomeado", criada.Versao + 1, 1), (resumo.Nome, resumo.Versao, resumo.TotalDeItens));
        var detalhe = (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{criada.Id}"))!;
        Assert.Equal(JsonSerializer.Serialize(envio.Itens), JsonSerializer.Serialize(detalhe.Itens));
        var antigo = (await cliente.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!.Single(p => p.Id == criada.Id);
        Assert.Equal(JsonSerializer.Serialize(detalhe.Itens), JsonSerializer.Serialize(antigo.Itens));
        Assert.Equal(detalhe.Modelos, antigo.Modelos);
        Assert.Empty((await cliente.GetFromJsonAsync<ResumoDoProjetoV1[]>("/api/projetos/resumos?desde=2099-01-01T00%3A00%3A00Z"))!);
    }
}
