using System.Net;
using System.Net.Http.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class ProjetosAcessoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static EnvioDeProjetoV1 Envio(int? versao = null, string situacao = "rascunho")
        => new("ondas", "Projeto em ondas", "empresa", "", ["https://github.com/acme/app"],
            "feature/dev/projeto-ondas", false, "Definição", [], true, 2, "main", situacao, versao,
            [new(1, "Implementar", "", "", [], [], [], 1, 0, "planejado")]);

    [Fact]
    public async Task Dono_concede_altera_remove_e_leitor_nao_altera_aprova_ou_dispara()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Acesso");
        var outra = await ApiDeTeste.EmpresaAsync(admin, "Outra");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@acesso.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@acesso.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(outra.CodigoDeAdesao, "caio@outra.com");
        var criada = await ana.PutAsJsonAsync("/api/projetos", Envio());
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";
        Assert.Equal("dono", projeto.Papel);
        Assert.Empty((await bia.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync(rota + "/acessos")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync(rota)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PostAsJsonAsync(rota + "/acessos", new ConcessaoDeAcessoV1("caio@outra.com", "editor"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync(rota + "/acessos", new ConcessaoDeAcessoV1("bia@acesso.com", "admin"))).StatusCode);

        var concessao = await ana.PostAsJsonAsync(rota + "/acessos", new ConcessaoDeAcessoV1("bia@acesso.com", "leitor"));
        Assert.Equal(HttpStatusCode.OK, concessao.StatusCode);
        var acessos = (await concessao.Content.ReadFromJsonAsync<AcessosDoProjetoV1>())!;
        var biaId = Assert.Single(acessos.Acessos).Usuario.Id;
        Assert.Equal("projeto", acessos.Recurso);
        Assert.Equal("leitor", (await bia.GetFromJsonAsync<ProjetoV1>(rota))!.Papel);
        Assert.Single((await bia.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!);
        foreach (var situacao in new[] { "rascunho", "aprovado", "rodando" })
        {
            // "rodando" pertence ao item, a aprovação e o disparo chegam no snapshot.
            var envio = situacao == "rodando"
                ? Envio(1, "aprovado") with { Itens = [Envio().Itens[0] with { Situacao = "rodando" }] }
                : Envio(1, situacao);
            var recusada = await bia.PutAsJsonAsync(rota, envio);
            Assert.Equal(HttpStatusCode.Forbidden, recusada.StatusCode);
            var motivo = await recusada.Content.ReadAsStringAsync();
            Assert.Contains("leitor", motivo);
            Assert.Contains("editor ou dono", motivo);
            Assert.Contains(acessos.Acessos[0].Usuario.Login, motivo);
            Assert.Contains("Projeto em ondas", motivo);
        }
        Assert.Equal(1, (await ana.GetFromJsonAsync<ProjetoV1>(rota))!.Versao);
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.DeleteAsync(rota + $"/acessos/{biaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PostAsJsonAsync(rota + "/acessos", new ConcessaoDeAcessoV1("ana@acesso.com", "editor"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.DeleteAsync(rota + $"/acessos/{acessos.Dono!.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync(rota + $"/acessos/{biaId}", new ConcessaoDeAcessoV1(null, "editor"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bia.PutAsJsonAsync(rota, Envio(1, "aprovado"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bia.PutAsJsonAsync(rota, Envio(2, "aprovado") with { Itens = [Envio().Itens[0] with { Situacao = "rodando" }] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync(rota + $"/acessos/{biaId}")).StatusCode);
        Assert.Empty((await bia.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PutAsJsonAsync(rota, Envio(3))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PutAsJsonAsync("/api/projetos", Envio(3))).StatusCode);
    }

    [Fact]
    public async Task Transferencia_exige_confirmacao_e_preserva_o_dono_anterior_como_editor()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Transferência");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@transfer.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@transfer.com");
        var p = (await (await ana.PutAsJsonAsync("/api/projetos", Envio())).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{p.Id}";
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsJsonAsync(rota + "/dono", new TransferenciaDeDonoV1("bia@transfer.com", false))).StatusCode);
        var r = await ana.PostAsJsonAsync(rota + "/dono", new TransferenciaDeDonoV1("bia@transfer.com", true));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("editor", (await ana.GetFromJsonAsync<ProjetoV1>(rota))!.Papel);
        Assert.Equal("dono", (await bia.GetFromJsonAsync<ProjetoV1>(rota))!.Papel);
    }
}
