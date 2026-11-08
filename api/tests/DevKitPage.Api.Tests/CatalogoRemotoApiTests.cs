using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O catálogo do chat remoto (<c>PUT /api/remoto/catalogo</c> e <c>GET /api/remoto/maquinas/{id}/catalogo</c>, contrato
/// seção 5): o dev.kit publica, com o JWT do usuário, o que os atalhos do chat mostram, e o site lê — só o dono, com a data,
/// também com o dev.kit fechado. O mais velho não volta o catálogo para trás, e as recusas são as do retrato.
/// </summary>
public sealed class CatalogoRemotoApiTests : IDisposable
{
    private const string Maquina = "3f2b9c0e5d7a";
    private static readonly DateTimeOffset Gerado = new(2026, 11, 6, 20, 0, 0, TimeSpan.Zero);

    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static HeartbeatV1 Heartbeat(string maquina = Maquina)
        => new(RemotoV1.Contrato, maquina, "Notebook do trabalho", "117", RemotoV1.OrigemServico, true, RemotoV1.AgenteAtivo, false, Gerado);

    private static CatalogoRemotoV1 Catalogo(DateTimeOffset? gerado = null, string titulo = "Handshake do chat remoto", string maquina = Maquina)
        => new(RemotoV1.Contrato, maquina, gerado ?? Gerado, "9b1deb4d3b7d",
            [new WorkItemNoCatalogoV1(812, titulo, "User Story", "Active")],
            [new RepositorioNoCatalogoV1("github.com/empresa/dev.kit.page", "main"), new RepositorioNoCatalogoV1("github.com/empresa/git.kit")],
            [new FluxoNoCatalogoV1("Tarefa avulsa", "Implementação avaliada"), new FluxoNoCatalogoV1("Tela")],
            [new ProjetoNoCatalogoV1("remote-control-v3", "remote-control-v3", ProjetosV1.Aprovado)],
            [new DesignNoCatalogoV1("dev.kit.page · board", 3)]);

    private HttpClient? _admin;

    private async Task<HttpClient> DevAsync(string login)
    {
        // O admin troca a senha no primeiro login: o segundo dev do teste reusa a sessão dele.
        _admin ??= await _api.AdminAsync();
        var (cliente, _) = await _api.UsuarioAsync(_admin, login, Papeis.Dev);
        return cliente;
    }

    private static async Task<string?> CodigoAsync(HttpResponseMessage resposta)
    {
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;
    }

    [Fact]
    public async Task O_dono_publica_e_le_o_catalogo_com_a_data_e_o_outro_usuario_nao_ve_nem_grava()
    {
        var anonimo = _api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.PutAsJsonAsync("/api/remoto/catalogo", Catalogo())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/api/remoto/maquinas/{Maquina}/catalogo")).StatusCode);

        var ana = await DevAsync("ana");
        // Sem o heartbeat a máquina não existe: o catálogo não cria máquina.
        Assert.Equal(HttpStatusCode.NotFound, (await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ana.GetAsync($"/api/remoto/maquinas/{Maquina}/catalogo")).StatusCode);

        (await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).EnsureSuccessStatusCode();
        var enviado = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo());
        Assert.Equal(HttpStatusCode.OK, enviado.StatusCode);
        var recebido = (await enviado.Content.ReadFromJsonAsync<CatalogoRecebidoV1>())!;
        Assert.True(recebido.Aplicado);

        var lido = (await ana.GetFromJsonAsync<CatalogoDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/catalogo"))!;
        Assert.Equal((Maquina, recebido.RecebidoEm), (lido.MaquinaId, lido.RecebidoEm));
        Assert.Equal(812, Assert.Single(lido.Catalogo.WorkItems!).Id);
        Assert.Equal(["github.com/empresa/dev.kit.page", "github.com/empresa/git.kit"], lido.Catalogo.Repositorios!.Select(r => r.Nome));
        Assert.Equal(["Tarefa avulsa", "Tela"], lido.Catalogo.Fluxos!.Select(f => f.Nome));
        Assert.Equal("remote-control-v3", Assert.Single(lido.Catalogo.Projetos!).Slug);
        Assert.Equal(3, Assert.Single(lido.Catalogo.Designs!).Telas);

        // Bia, com o MESMO id anônimo de máquina, não lê o de Ana; o dela é outro, e o de Ana fica intacto.
        var bia = await DevAsync("bia");
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync($"/api/remoto/maquinas/{Maquina}/catalogo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bia.PutAsJsonAsync("/api/remoto/catalogo", Catalogo(titulo: "Outro"))).StatusCode);
        (await bia.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).EnsureSuccessStatusCode();
        (await bia.PutAsJsonAsync("/api/remoto/catalogo", Catalogo(titulo: "Item da Bia"))).EnsureSuccessStatusCode();
        Assert.Equal("Handshake do chat remoto",
            (await ana.GetFromJsonAsync<CatalogoDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/catalogo"))!.Catalogo.WorkItems![0].Titulo);
    }

    [Fact]
    public async Task O_catalogo_mais_velho_e_aceito_sem_voltar_o_gravado_e_o_mais_novo_o_substitui()
    {
        var ana = await DevAsync("ana");
        (await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).EnsureSuccessStatusCode();
        (await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo())).EnsureSuccessStatusCode();

        var velho = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo(Gerado.AddMinutes(-5), "Velho"));
        Assert.False((await velho.Content.ReadFromJsonAsync<CatalogoRecebidoV1>())!.Aplicado);
        Assert.Equal("Handshake do chat remoto",
            (await ana.GetFromJsonAsync<CatalogoDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/catalogo"))!.Catalogo.WorkItems![0].Titulo);

        var novo = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo(Gerado.AddMinutes(5), "Novo") with { WorkItems = [] });
        Assert.True((await novo.Content.ReadFromJsonAsync<CatalogoRecebidoV1>())!.Aplicado);
        var lido = (await ana.GetFromJsonAsync<CatalogoDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/catalogo"))!;
        Assert.Empty(lido.Catalogo.WorkItems!);
        Assert.Equal(Gerado.AddMinutes(5), lido.Catalogo.GeradoEm);
    }

    /// <summary>
    /// (US #867) Os MODELOS que o dev escolhe dentro do fluxo e o agente padrão de cada fluxo vão e voltam com o catálogo; o
    /// catálogo sem eles (o dev.kit antigo) continua valendo, e o valor que quebraria o <c>--agente</c> é recusado.
    /// </summary>
    [Fact]
    public async Task Os_modelos_do_fluxo_vao_e_voltam_e_o_valor_invalido_e_recusado()
    {
        var ana = await DevAsync("ana");
        (await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).EnsureSuccessStatusCode();
        var comModelos = Catalogo() with
        {
            Fluxos = [new FluxoNoCatalogoV1("Tarefa avulsa", "Implementação avaliada", "claude:opus"), new FluxoNoCatalogoV1("Tela")],
            Modelos = [new ModeloNoCatalogoV1("claude:opus", "Claude · Opus"), new ModeloNoCatalogoV1("codex:gpt-5[1m]", "Codex · GPT-5")],
        };
        (await ana.PutAsJsonAsync("/api/remoto/catalogo", comModelos)).EnsureSuccessStatusCode();

        var lido = (await ana.GetFromJsonAsync<CatalogoDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/catalogo"))!.Catalogo;
        Assert.Equal(["claude:opus", "codex:gpt-5[1m]"], lido.Modelos!.Select(m => m.Valor));
        Assert.Equal(["claude:opus", null], lido.Fluxos!.Select(f => f.Agente));

        var quebrado = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo(Gerado.AddMinutes(1)) with { Modelos = [new ModeloNoCatalogoV1("claude:opus | rm", "Claude")] });
        Assert.Equal(HttpStatusCode.BadRequest, quebrado.StatusCode);
        Assert.Contains("modelos[0]", await quebrado.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var semModelos = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo(Gerado.AddMinutes(2)));
        Assert.Equal(HttpStatusCode.OK, semModelos.StatusCode);
    }

    [Fact]
    public async Task As_recusas_sao_as_do_contrato_e_o_dado_sensivel_nao_volta_na_resposta()
    {
        var ana = await DevAsync("ana");
        (await ana.PostAsJsonAsync("/api/remoto/heartbeat", Heartbeat())).EnsureSuccessStatusCode();

        var contrato = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo() with { Contrato = 2 });
        Assert.Equal((HttpStatusCode.BadRequest, RemotoV1.CodigoContratoDesconhecido), (contrato.StatusCode, await CodigoAsync(contrato)));

        var forma = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo() with { WorkItems = [new WorkItemNoCatalogoV1(0, "Sem id")] });
        Assert.Equal(HttpStatusCode.BadRequest, forma.StatusCode);
        Assert.Contains("workItems[0]", await forma.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var cheio = await ana.PutAsJsonAsync("/api/remoto/catalogo",
            Catalogo() with { Fluxos = Enumerable.Range(1, RemotoV1.MaximoNoCatalogo + 1).Select(i => new FluxoNoCatalogoV1($"Fluxo {i}")).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, cheio.StatusCode);

        var sensivel = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo(titulo: "Ligar para ana@cliente.com.br"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, RemotoV1.CodigoDadoSensivel), (sensivel.StatusCode, await CodigoAsync(sensivel)));
        var corpo = await sensivel.Content.ReadAsStringAsync();
        Assert.Contains("workItems[0].titulo", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("ana@cliente.com.br", corpo, StringComparison.Ordinal);

        // O carimbo e o slug com faixa de anos não são dado sensível.
        var limpo = await ana.PutAsJsonAsync("/api/remoto/catalogo", Catalogo(titulo: "Sprint 11 2026-2027 · 20261102-095333")
            with { Projetos = [new ProjetoNoCatalogoV1("sprint-11-2026-2027", "Sprint 11")] });
        Assert.Equal(HttpStatusCode.OK, limpo.StatusCode);

        var texto = await ana.PutAsync("/api/remoto/catalogo", new StringContent("{}", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, texto.StatusCode);
        var grande = await ana.PutAsync("/api/remoto/catalogo",
            new StringContent($$"""{"contrato":1,"maquinaId":"{{Maquina}}","x":"{{new string('a', RemotoV1.TamanhoMaximoDoCatalogo)}}"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
    }
}
