using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class SkillDoPacoteApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static EnvioDeProjetoV1 Envio(string nivel = "usuario") => new(
        "pacote", "Pacote", nivel, "", ["https://github.com/acme/app"], "feature/dev/projeto-pacote",
        false, "Definição", [], true, 2, "main", "aprovado", null,
        [new(1, "Item", "", "", [], [], [], 1, 0, "planejado")],
        SkillDoPacote: "pacote-x", SkillsDoPacote: [new("pacote-x", "somente-comando"), new("pacote-y", "com-parametros")]);

    [Fact]
    public async Task Round_trip_preserva_cliente_antigo_e_edicao_minima_tem_versao_esperada()
    {
        var cliente = await _api.AdminAsync();
        var criada = await cliente.PutAsJsonAsync("/api/projetos", Envio());
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";
        Assert.Equal("pacote-x", projeto.SkillDoPacote);
        Assert.Equal(Envio().SkillsDoPacote, projeto.SkillsDoPacote);
        var antigo = JsonSerializer.SerializeToNode(Envio(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        antigo.Remove("skillDoPacote");
        antigo.Remove("skillsDoPacote");
        antigo["versao"] = projeto.Versao;
        Assert.Equal(HttpStatusCode.OK, (await cliente.PutAsJsonAsync(rota, antigo)).StatusCode);
        projeto = (await cliente.GetFromJsonAsync<ProjetoV1>(rota))!;
        Assert.Equal("pacote-x", projeto.SkillDoPacote);
        Assert.Equal(Envio().SkillsDoPacote, projeto.SkillsDoPacote);
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PutAsJsonAsync(rota + "/skill-do-pacote", new EdicaoDaSkillDoPacoteV1(1, "pacote-y"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync(rota + "/skill-do-pacote", new EdicaoDaSkillDoPacoteV1(projeto.Versao, "ausente"))).StatusCode);
        var editada = await cliente.PutAsJsonAsync(rota + "/skill-do-pacote", new EdicaoDaSkillDoPacoteV1(projeto.Versao, "pacote-y"));
        Assert.Equal(HttpStatusCode.OK, editada.StatusCode);
        var depois = (await editada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal("pacote-y", depois.SkillDoPacote);
        Assert.Equal(JsonSerializer.Serialize(projeto.Itens), JsonSerializer.Serialize(depois.Itens));
        Assert.Equal(projeto.Situacao, depois.Situacao);
        var removida = await cliente.PutAsJsonAsync(rota + "/skill-do-pacote", new EdicaoDaSkillDoPacoteV1(depois.Versao, ""));
        Assert.Equal(HttpStatusCode.OK, removida.StatusCode);
        Assert.Equal("", (await cliente.GetFromJsonAsync<ProjetoV1>(rota))!.SkillDoPacote);
    }

    [Fact]
    public async Task Edicao_exige_autenticacao_e_editor_e_nao_altera_a_escolha_quando_recusada()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Pacote");
        var (ana, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@pacote.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@pacote.com");
        var criada = await ana.PutAsJsonAsync("/api/projetos", Envio("empresa"));
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var rota = $"/api/projetos/{projeto.Id}";
        using var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync(rota + "/skill-do-pacote", new EdicaoDaSkillDoPacoteV1(1, ""))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync(rota + "/acessos", new ConcessaoDeAcessoV1("bia@pacote.com", "leitor"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await bia.PutAsJsonAsync(rota + "/skill-do-pacote", new EdicaoDaSkillDoPacoteV1(1, ""))).StatusCode);
        Assert.Equal("pacote-x", (await ana.GetFromJsonAsync<ProjetoV1>(rota))!.SkillDoPacote);
    }
}
