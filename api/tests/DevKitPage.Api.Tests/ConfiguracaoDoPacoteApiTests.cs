using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

public sealed class ConfiguracaoDoPacoteApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    [Fact]
    public async Task Configuracao_tem_Debug_padrao_edicao_minima_ACL_versao_e_compatibilidade()
    {
        var admin = await _api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Configuração do pacote");
        var (dono, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "dono@pacote.test");
        var (leitor, _) = await _api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "leitor@pacote.test");
        var envio = new EnvioDeProjetoV1("pacote", "Pacote", "empresa", "", ["https://github.com/acme/app"],
            "feature/pacote", false, "Definição preservada", [], true, 1, "main", "aprovado", null,
            [new(1, "Implementar", "", "", [], [], [], 1, 0, "planejado")]);
        var criada = await dono.PutAsJsonAsync("/api/projetos", envio);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal("Debug", projeto.ConfiguracaoDoPacote);
        var rota = $"/api/projetos/{projeto.Id}";
        var edicao = new EdicaoDaConfiguracaoDoPacoteV1(projeto.Versao, "Release");
        using var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync(rota + "/configuracao-do-pacote", edicao)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dono.PostAsJsonAsync(rota + "/acessos", new ConcessaoDeAcessoV1("leitor@pacote.test", "leitor"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await leitor.PutAsJsonAsync(rota + "/configuracao-do-pacote", edicao)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await dono.PutAsJsonAsync(rota + "/configuracao-do-pacote", edicao with { VersaoEsperada = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await dono.PutAsJsonAsync(rota + "/configuracao-do-pacote", edicao with { ConfiguracaoDoPacote = "C:\\sentinela\\comando" })).StatusCode);
        // O endpoint aceita só o valor e a versão; propriedades extras não editam o snapshot.
        var salva = await dono.PutAsJsonAsync(rota + "/configuracao-do-pacote", new
        {
            versaoEsperada = projeto.Versao, configuracaoDoPacote = "Release",
            definicao = "SENTINELA_NAO_GRAVAR", comando = "SENTINELA_NAO_GRAVAR", skillDoPacote = "SENTINELA_NAO_GRAVAR",
        });
        Assert.Equal(HttpStatusCode.OK, salva.StatusCode);
        projeto = (await salva.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal("Release", projeto.ConfiguracaoDoPacote);
        Assert.Equal(envio.Definicao, projeto.Definicao);
        Assert.DoesNotContain("SENTINELA_NAO_GRAVAR", await salva.Content.ReadAsStringAsync());
        var legado = JsonSerializer.SerializeToNode(envio with { Versao = projeto.Versao }, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        legado.Remove("configuracaoDoPacote");
        var regravada = await dono.PutAsJsonAsync(rota, legado);
        Assert.Equal(HttpStatusCode.OK, regravada.StatusCode);
        Assert.Equal("Release", (await regravada.Content.ReadFromJsonAsync<ProjetoV1>())!.ConfiguracaoDoPacote);
        Assert.Equal("Release", (await leitor.GetFromJsonAsync<ProjetoV1>(rota))!.ConfiguracaoDoPacote);
    }
}
