using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class EdicaoRemotaDoItemApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();
    private static EnvioDeProjetoV1 Envio() => new("editar", "Editar", "empresa", "", ["repo"], "feature/projeto-editar",
        false, "Definição preservada", [], true, 2, "release", "rascunho", null,
        [new(1, "Primeiro", "Descrição", "14", [], ["api"], [], 1, 0, "planejado", Design:
            new(true, 1, [new("repo", "tela", 2, new string('a', 64))]), Agente: "codex", Modelo: "gpt-6.1-sol"),
         new(2, "Segundo", "Outro", "13", [], ["web"], [], 1, 0, "planejado", Agente: "codex", Modelo: "gpt-6.1-sol")])
        { Board = "nao", BaseDoBranch = "develop", Agente = "codex", Modelo = "gpt-6.1-sol", Modelos = [new("codex", "gpt-6.1-sol")] };
    private static EdicaoRemotaDoItemV1 Editar(ProjetoV1 p) => new(p.Versao, "Título editado", "Nova descrição", "14",
        "codex", "gpt-6.1-sol", [], ["api"], 2);

    [Fact]
    public async Task Edicao_http_persiste_e_outra_requisicao_rele_preservando_metadados_e_sem_disparar()
    {
        var cliente = await _api.AdminAsync();
        var criada = await cliente.PutAsJsonAsync("/api/projetos", Envio() with { Nivel = "usuario" });
        var p = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var resposta = await cliente.PutAsJsonAsync($"/api/projetos/{p.Id}/itens/1", Editar(p));
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var salvo = (await resposta.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var relido = (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{p.Id}"))!;
        Assert.Equal(p.Versao + 1, relido.Versao);
        Assert.Equal("aguardando", relido.Situacao);
        Assert.Equal("Título editado", relido.Itens[0].Titulo);
        Assert.Equal(relido.Versao, relido.Itens[0].VersaoDaEdicaoDoPlano);
        Assert.Equal(0, relido.Itens[1].VersaoDaEdicaoDoPlano);
        Assert.Equal(JsonSerializer.Serialize(salvo), JsonSerializer.Serialize(relido));
        Assert.Equal(JsonSerializer.Serialize(p.Itens[0].Design), JsonSerializer.Serialize(relido.Itens[0].Design));
        Assert.Equal(JsonSerializer.Serialize(p.Itens[1]), JsonSerializer.Serialize(relido.Itens[1]));
        Assert.Equal((p.BaseDoBranch, p.DestinoDaPr, p.Board, p.Modelo), (relido.BaseDoBranch, relido.DestinoDaPr, relido.Board, relido.Modelo));
        Assert.All(relido.Itens, i => Assert.Equal("planejado", i.Situacao));
        // O mesmo pedido atrasado não substitui o snapshot novo.
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PutAsJsonAsync($"/api/projetos/{p.Id}/itens/1", Editar(p))).StatusCode);
        Assert.Equal(relido.Versao, (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{p.Id}"))!.Versao);
    }

    [Fact]
    public async Task Leitor_e_outro_escopo_nao_editam_e_versao_errada_nao_grava()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Edição");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@edicao.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@edicao.com");
        var p = (await (await ana.PutAsJsonAsync("/api/projetos", Envio())).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{p.Id}";
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PutAsJsonAsync(rota + "/itens/1", Editar(p))).StatusCode);
        await ana.PostAsJsonAsync(rota + "/acessos", new ConcessaoDeAcessoV1("bia@edicao.com", "leitor"));
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PutAsJsonAsync(rota + "/itens/1", Editar(p))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PutAsJsonAsync(rota + "/itens/1", Editar(p) with { VersaoEsperada = 0 })).StatusCode);
        Assert.Equal(p.Versao, (await ana.GetFromJsonAsync<ProjetoV1>(rota))!.Versao);
        using var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync(rota + "/itens/1", Editar(p))).StatusCode);
    }

    [Fact]
    public async Task Ciclo_componente_que_moveria_iniciado_e_modelo_estranho_nao_gravam()
    {
        var cliente = await _api.AdminAsync();
        var envio = Envio() with { Nivel = "usuario", Itens = [Envio().Itens[0], Envio().Itens[1] with
            { Dependencias = [1], Onda = 2, Situacao = "rodando" }] };
        var p = (await (await cliente.PutAsJsonAsync("/api/projetos", envio)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{p.Id}";
        foreach (var edicao in new[] { Editar(p) with { Dependencias = [2] }, Editar(p) with { Modelo = "inventado" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync(rota + "/itens/1", edicao)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync(rota + "/itens/2", Editar(p))).StatusCode);
        var antes = await cliente.GetFromJsonAsync<ProjetoV1>(rota);
        Assert.Equal(p.Versao, antes!.Versao);
        Assert.Equal(JsonSerializer.Serialize(p.Itens), JsonSerializer.Serialize(antes.Itens));
        // Componente concorrente exigiria mover o item rodando: a edição é recusada.
        var outro = envio with { Slug = "concorrente", Itens = [Envio().Itens[0], Envio().Itens[1] with { Situacao = "rodando" }] };
        var concorrente = (await (await cliente.PutAsJsonAsync("/api/projetos", outro)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync($"/api/projetos/{concorrente.Id}/itens/1",
            Editar(concorrente) with { Componentes = ["web"] })).StatusCode);
        Assert.Equal(concorrente.Versao, (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{concorrente.Id}"))!.Versao);
    }
}
