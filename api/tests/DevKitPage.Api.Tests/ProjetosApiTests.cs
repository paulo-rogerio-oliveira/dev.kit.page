using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class ProjetosApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static EnvioDeProjetoV1 Envio(string nivel = "usuario") => new(
        "portal", "Portal", nivel, "dev", ["https://github.com/acme/portal"], "feature/dev/projeto-portal",
        false, "Definição aprovada", [], true, 2, "feature/dev/semana5", "aprovado", null,
        [new(1, "Sincronizar", "Descrição", "6", ["https://github.com/acme/portal"], ["Projetos"], [], 1,
            747, "rodando", "bug", 2, 14, "feature/dev/projeto-portal-item-1")],
        700, 600, "DevKit/Sprint 3", "vincular", ["feature/dev/us-710"], DateTimeOffset.UtcNow);

    [Fact]
    public async Task Snapshot_completo_volta_em_outra_sessao_e_versao_velha_recebe_estado_atual()
    {
        var cliente = await _api.AdminAsync();
        var envio = Envio();
        var criada = await cliente.PutAsJsonAsync("/api/projetos", envio);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var p = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(1, p.Versao);
        Assert.Equal(envio.Branch, p.Branch);
        Assert.Equal(envio.BranchesDasTasks, p.BranchesDasTasks);
        Assert.Equal((700, 600, "DevKit/Sprint 3", "vincular"), (p.FeatureId, p.EpicId, p.Iteracao, p.Board));
        Assert.Equal(JsonSerializer.Serialize(envio.Itens), JsonSerializer.Serialize(p.Itens));

        // Duas máquinas leram v1. A grava primeiro; B não pode sobrescrevê-la silenciosamente.
        var lida = (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{p.Id}"))!;
        var atualizada = await cliente.PutAsJsonAsync($"/api/projetos/{p.Id}", envio with { Versao = lida.Versao, Nome = "Portal atualizado" });
        Assert.Equal(HttpStatusCode.OK, atualizada.StatusCode);
        var conflito = await cliente.PutAsJsonAsync($"/api/projetos/{p.Id}", envio with { Versao = 1, Nome = "Desatualizado" });
        Assert.Equal(HttpStatusCode.Conflict, conflito.StatusCode);
        var problema = await conflito.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Portal atualizado", problema.GetProperty("atual").GetProperty("nome").GetString());
        Assert.Single((await cliente.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!);
        Assert.Empty((await cliente.GetFromJsonAsync<ProjetoV1[]>("/api/projetos?desde=2099-01-01T00:00:00Z"))!);
    }

    [Fact]
    public async Task Envio_incompleto_preserva_itens_e_slug_duplicado_vira_conflito()
    {
        var cliente = await _api.AdminAsync();
        var envio = Envio();
        var p = (await (await cliente.PutAsJsonAsync("/api/projetos", envio)).Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PutAsJsonAsync("/api/projetos", envio)).StatusCode);
        var r = await cliente.PutAsJsonAsync($"/api/projetos/{p.Id}", envio with { Versao = 1, Itens = [] });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Single((await r.Content.ReadFromJsonAsync<ProjetoV1>())!.Itens);
    }

    [Fact]
    public async Task Projetos_seguem_isolamento_de_usuario_time_empresa_e_rejeitam_acesso_direto_externo()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var outra = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@a.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(outra.CodigoDeAdesao, "caio@b.com");
        foreach (var nivel in new[] { "usuario", "time", "empresa" })
            Assert.Equal(HttpStatusCode.Created, (await ana.PutAsJsonAsync("/api/projetos", Envio(nivel))).StatusCode);
        var deAna = (await ana.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!;
        Assert.Equal(3, deAna.Length);
        Assert.Equal(2, (await bia.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!.Length);
        Assert.Single((await bia.GetFromJsonAsync<ProjetoV1[]>("/api/projetos?time=outro"))!);
        Assert.Empty((await caio.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!);
        foreach (var p in deAna)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync($"/api/projetos/{p.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await caio.PutAsJsonAsync($"/api/projetos/{p.Id}", Envio(p.Nivel) with { Versao = p.Versao })).StatusCode);
        }
    }

    [Fact]
    public async Task Nao_autenticado_e_snapshot_invalido_sao_recusados()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/projetos")).StatusCode);
        var cliente = await _api.AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync("/api/projetos", Envio() with { Slug = "../fora" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync("/api/projetos", Envio() with { Itens = [Envio().Itens[0], Envio().Itens[0]] })).StatusCode);
    }

    [Fact]
    public async Task Duas_maquinas_enviando_a_mesma_versao_tem_um_vencedor_e_um_409()
    {
        var cliente = await _api.AdminAsync();
        var envio = Envio();
        var inicial = await cliente.PutAsJsonAsync("/api/projetos", envio);
        var p = (await inicial.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var respostas = await Task.WhenAll(
            cliente.PutAsJsonAsync($"/api/projetos/{p.Id}", envio with { Versao = 1, Nome = "Máquina A" }),
            cliente.PutAsJsonAsync($"/api/projetos/{p.Id}", envio with { Versao = 1, Nome = "Máquina B" }));
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(respostas, r => r.StatusCode == HttpStatusCode.Conflict);
        var atual = (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{p.Id}"))!;
        Assert.Equal(2, atual.Versao);
        Assert.Contains(atual.Nome, new[] { "Máquina A", "Máquina B" });
    }
}
