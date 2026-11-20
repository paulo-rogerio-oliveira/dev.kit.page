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
    public async Task Contrato_portatil_preserva_omissao_legada_e_limpeza_explicita_sem_reativar_board()
    {
        var cliente = await _api.AdminAsync();
        var envio = Envio() with { BaseDoBranch = "develop", DestinoDaPr = "release", Board = "nao",
            FluxoPadrao = "1", Agente = "codex", Modelo = "gpt-6.1-sol",
            Modelos = [new("codex", "gpt-6.1-sol"), new("claude", "opus")],
            Itens = [Envio().Itens[0] with { Agente = "codex", Modelo = "gpt-6.1-sol",
                Design = new(true, 1, [new("repo", "tela", 6, new string('a', 64))]) }] };
        var criada = await cliente.PutAsJsonAsync("/api/projetos", envio);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        var antigo = JsonSerializer.SerializeToNode(envio, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        antigo["versao"] = projeto.Versao;
        foreach (var campo in new[] { "baseDoBranch", "fluxoPadrao", "agente", "modelo", "modelos", "board", "featureId", "epicId", "iteracao", "branchesDasTasks" })
            antigo.Remove(campo);
        foreach (var campo in new[] { "agente", "modelo", "design", "branch", "horas", "atualizadoEm" })
            antigo["itens"]![0]!.AsObject().Remove(campo);
        var regravada = await cliente.PutAsJsonAsync("/api/projetos", antigo);
        Assert.Equal(HttpStatusCode.OK, regravada.StatusCode);
        projeto = (await regravada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        antigo["versao"] = projeto.Versao;
        antigo["board"] = null;
        antigo["iteracao"] = null;
        antigo["baseDoBranch"] = null;
        var nulos = await cliente.PutAsJsonAsync("/api/projetos", antigo);
        Assert.Equal(HttpStatusCode.OK, nulos.StatusCode);
        projeto = (await nulos.Content.ReadFromJsonAsync<ProjetoV1>())!;
        foreach (var leitura in new[] { projeto,
            (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{projeto.Id}"))!,
            Assert.Single((await cliente.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!) })
        {
            Assert.Equal(("develop", "release", "nao", 700, "1", "codex", "gpt-6.1-sol"),
                (leitura.BaseDoBranch, leitura.DestinoDaPr, leitura.Board, leitura.FeatureId,
                    leitura.FluxoPadrao, leitura.Agente, leitura.Modelo));
            Assert.Equal(envio.Modelos, leitura.Modelos);
            Assert.Equal(envio.Itens[0].Design!.Propostas, leitura.Itens[0].Design!.Propostas);
            Assert.Equal(("codex", "gpt-6.1-sol", 14d), (leitura.Itens[0].Agente, leitura.Itens[0].Modelo, leitura.Itens[0].Horas));
        }
        var limpeza = await cliente.PutAsJsonAsync("/api/projetos", envio with { Versao = projeto.Versao,
            Agente = "", Modelo = "", FluxoPadrao = "", Modelos = [],
            Itens = [envio.Itens[0] with { Agente = "", Modelo = "" }] });
        Assert.Equal(HttpStatusCode.OK, limpeza.StatusCode);
        var limpa = (await limpeza.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal("", limpa.Agente);
        Assert.Empty(limpa.Modelos!);
        Assert.Equal("", limpa.Itens[0].Modelo);
        Assert.Equal("nao", limpa.Board);
    }

    [Fact]
    public async Task Design_fixado_sobrevive_ao_transporte_e_a_cliente_antigo_sem_trocar_a_versao()
    {
        var cliente = await _api.AdminAsync();
        var referencia = new ReferenciaDePropostaV1("repo-123", "plano", 6, new string('a', 64));
        var design = new DesignDoItemV1(true, 1, [referencia]);
        var envio = Envio() with { Situacao = "aguardando", Itens = [Envio().Itens[0] with { Design = design }] };
        var criada = await cliente.PutAsJsonAsync("/api/projetos", envio);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(referencia, Assert.Single(projeto.Itens[0].Design!.Propostas));
        var antigo = await cliente.PutAsJsonAsync($"/api/projetos/{projeto.Id}", envio with
            { Versao = projeto.Versao, Itens = [envio.Itens[0] with { Design = null, Titulo = "Cliente anterior" }] });
        Assert.Equal(HttpStatusCode.OK, antigo.StatusCode);
        projeto = (await antigo.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(referencia, Assert.Single(projeto.Itens[0].Design!.Propostas));
        var semRevisao = envio with { Versao = projeto.Versao,
            Itens = [envio.Itens[0] with { Design = design with { Propostas = [referencia with { Versao = 7 }] } }] };
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync($"/api/projetos/{projeto.Id}", semRevisao)).StatusCode);
        var novaRevisao = semRevisao with { Itens = [semRevisao.Itens[0] with { Design = semRevisao.Itens[0].Design! with { Revisao = 2 } }] };
        var revisada = await cliente.PutAsJsonAsync($"/api/projetos/{projeto.Id}", novaRevisao);
        Assert.Equal(HttpStatusCode.OK, revisada.StatusCode);
        var lida = (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{projeto.Id}"))!;
        Assert.Equal(7, Assert.Single(lida.Itens[0].Design!.Propostas).Versao);
        Assert.Equal(2, lida.Itens[0].Design!.Revisao);
        var invalida = novaRevisao with { Versao = lida.Versao, Itens = [novaRevisao.Itens[0] with
            { Design = new(true, 3, [referencia with { Hash = "inválido" }]) }] };
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync($"/api/projetos/{projeto.Id}", invalida)).StatusCode);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Reordenar_preserva_design_por_identidade_com_referencias_explicitas_ou_omitidas(bool omitirDesign, bool porWorkItem)
    {
        var cliente = await _api.AdminAsync();
        var propostaA = new ReferenciaDePropostaV1("repo-123", "tela-a", 6, new string('a', 64));
        var propostaB = new ReferenciaDePropostaV1("repo-123", "tela-b", 10, new string('b', 64));
        var itemA = Envio().Itens[0] with { Numero = 1, Titulo = "A", WorkItemId = porWorkItem ? 747 : 0,
            Design = new(true, 1, [propostaA]), Agente = "codex", Modelo = "gpt-6.1-sol" };
        var itemB = itemA with { Numero = 2, Titulo = "B", WorkItemId = porWorkItem ? 748 : 0,
            Design = new(true, 1, [propostaB]), Agente = "claude", Modelo = "opus" };
        var envio = Envio() with { Itens = [itemA, itemB] };
        var criada = await cliente.PutAsJsonAsync("/api/projetos", envio);
        Assert.Equal(HttpStatusCode.Created, criada.StatusCode);
        var projeto = (await criada.Content.ReadFromJsonAsync<ProjetoV1>())!;

        // Títulos trocados junto com os números comprovam a prioridade do WorkItemId.
        // Sem work item, a identidade é o título normalizado, não a posição no plano.
        var reordenados = new[]
        {
            itemB with { Numero = 1, Titulo = porWorkItem ? "A" : " b " },
            itemA with { Numero = 2, Titulo = porWorkItem ? "B" : " a " },
        };
        foreach (var itens in new[] { reordenados, new[] { itemA, itemB } })
        {
            var resposta = await cliente.PutAsJsonAsync($"/api/projetos/{projeto.Id}", envio with
                { Versao = projeto.Versao, Itens = itens.Select(i => omitirDesign ? i with { Design = null, Agente = null, Modelo = null } : i).ToArray() });
            Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
            projeto = (await resposta.Content.ReadFromJsonAsync<ProjetoV1>())!;
            var lida = (await cliente.GetFromJsonAsync<ProjetoV1>($"/api/projetos/{projeto.Id}"))!;
            var listada = Assert.Single((await cliente.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!);
            foreach (var leitura in new[] { projeto, lida, listada })
            {
                Assert.Equal("aprovado", leitura.Situacao);
                foreach (var esperado in itens)
                {
                    var atual = Assert.Single(leitura.Itens, i => i.Numero == esperado.Numero);
                    Assert.Equal(esperado.WorkItemId, atual.WorkItemId);
                    Assert.Equal((esperado.Agente, esperado.Modelo), (atual.Agente, atual.Modelo));
                    Assert.Equal(1, atual.Design!.Revisao);
                    Assert.Equal(Assert.Single(esperado.Design!.Propostas), Assert.Single(atual.Design.Propostas));
                }
            }
        }
    }

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
        Assert.Empty((await bia.GetFromJsonAsync<ProjetoV1[]>("/api/projetos"))!);
        Assert.Empty((await bia.GetFromJsonAsync<ProjetoV1[]>("/api/projetos?time=outro"))!);
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

public sealed class ProjetosCompatibilidadeApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();

    public void Dispose() => _api.Dispose();

    private static ItemDoProjetoV1 Item(int numero, params int[] dependencias)
        => new(numero, $"Item {numero}", "o que fazer", "planejar-e-executar", ["https://github.com/acme/portal"], ["Core/Data"],
            dependencias, 1, 0, ProjetosV1.ItemPlanejado);

    private static EnvioDeProjetoV1 Projeto(string slug, string? nivel = null, string? time = null, int? versao = null,
        IReadOnlyList<ItemDoProjetoV1>? itens = null)
        => new(slug, $"Projeto {slug}", nivel, time, ["https://github.com/acme/portal", "https://github.com/acme/api"],
            $"feature/dev/projeto-{slug}", false, "O que o dev quer.", ["executando v004"], true, 3, "feature/dev/semana5",
            ProjetosV1.AguardandoAprovacao, versao, itens ?? [Item(1), Item(2, 1)]);

    private async Task<(HttpClient Ana, HttpClient Bia, HttpClient Gestor, HttpClient Caio)> EmpresasAsync()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        return (ana, bia, gestor, caio);
    }

    private static async Task<ProjetoV1[]> ListarAsync(HttpClient cliente, string consulta = "")
        => (await cliente.GetFromJsonAsync<ProjetoV1[]>("/api/projetos" + consulta))!;

    [Fact]
    public async Task O_projeto_criado_volta_com_o_plano_e_a_versao_lida_substitui_e_a_velha_e_conflito()
    {
        var (ana, bia, _, _) = await EmpresasAsync();

        var criacao = await ana.PutAsJsonAsync("/api/projetos", Projeto("orquestrador"));
        Assert.Equal(HttpStatusCode.Created, criacao.StatusCode);
        var criado = (await criacao.Content.ReadFromJsonAsync<ProjetoV1>())!;
        Assert.Equal(("empresa", 1, 2, "feature/dev/projeto-orquestrador"), (criado.Nivel, criado.Versao, criado.Itens.Count, criado.Branch));
        Assert.Equal([1], criado.Itens.Single(i => i.Numero == 2).Dependencias);
        Assert.Equal($"/api/projetos/{criado.Id}", criacao.Headers.Location!.ToString());

        await ana.PostAsJsonAsync($"/api/projetos/{criado.Id}/acessos", new ConcessaoDeAcessoV1("bia@a.com", "editor"));
        // Bia, editora, lê e regrava; Ana, com a versão velha, recebe conflito.
        var lido = Assert.Single(await ListarAsync(bia));
        var regravacao = await bia.PutAsJsonAsync("/api/projetos", Projeto("orquestrador", versao: lido.Versao, itens: [Item(1)]));
        Assert.Equal(HttpStatusCode.OK, regravacao.StatusCode);
        Assert.Equal(2, (await regravacao.Content.ReadFromJsonAsync<ProjetoV1>())!.Versao);

        var velha = await ana.PutAsJsonAsync("/api/projetos", Projeto("orquestrador", versao: 1));
        Assert.Equal(HttpStatusCode.Conflict, velha.StatusCode);

        // Criar de novo o mesmo slug, sem versão, também é conflito: o dev.kit relê antes.
        Assert.Equal(HttpStatusCode.Conflict, (await ana.PutAsJsonAsync("/api/projetos", Projeto("orquestrador"))).StatusCode);
    }

    [Fact]
    public async Task A_visibilidade_segue_o_escopo_usuario_equipe_empresa_e_outra_empresa()
    {
        var (ana, bia, gestor, caio) = await EmpresasAsync();
        await ana.PutAsJsonAsync("/api/projetos", Projeto("da-empresa", nivel: "empresa"));
        await ana.PutAsJsonAsync("/api/projetos", Projeto("de-vendas", nivel: "time", time: "Vendas"));
        await ana.PutAsJsonAsync("/api/projetos", Projeto("meu", nivel: "usuario"));

        Assert.Equal(["da-empresa", "de-vendas", "meu"], (await ListarAsync(ana)).Select(p => p.Slug).Order());
        Assert.Empty(await ListarAsync(bia));
        foreach (var p in await ListarAsync(ana))
            if (p.Nivel != "usuario")
                Assert.Equal(HttpStatusCode.OK, (await ana.PostAsJsonAsync($"/api/projetos/{p.Id}/acessos", new ConcessaoDeAcessoV1("bia@a.com", "leitor"))).StatusCode);
        Assert.Equal(["da-empresa", "de-vendas"], (await ListarAsync(bia)).Select(p => p.Slug).Order());
        Assert.Equal(["da-empresa"], (await ListarAsync(bia, "?time=Suporte")).Select(p => p.Slug));
        Assert.Empty(await ListarAsync(gestor));
        Assert.Empty(await ListarAsync(caio));

        // O projeto de outra empresa é 404, sem revelar que existe.
        var daEmpresa = (await ListarAsync(bia)).Single(p => p.Slug == "da-empresa");
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync($"/api/projetos/{daEmpresa.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bia.GetAsync($"/api/projetos/{daEmpresa.Id}")).StatusCode);
    }

    [Fact]
    public async Task Projeto_de_time_sem_time_e_recusado_e_quem_nao_tem_empresa_guarda_no_nivel_usuario()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (solo, _) = await _api.UsuarioAsync(admin, "solo", Papeis.Dev);
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/api/projetos", Projeto("sem-time", nivel: "time"))).StatusCode);

        // Pediu empresa, mas não tem: fica no nível usuário, e ninguém da empresa o vê.
        var criado = await solo.PutAsJsonAsync("/api/projetos", Projeto("sozinho", nivel: "empresa"));
        Assert.Equal(HttpStatusCode.Created, criado.StatusCode);
        Assert.Equal("usuario", (await criado.Content.ReadFromJsonAsync<ProjetoV1>())!.Nivel);
        Assert.Empty(await ListarAsync(ana));
    }

    [Fact]
    public async Task So_o_dono_apaga_o_projeto()
    {
        var (ana, bia, gestor, _) = await EmpresasAsync();
        var a = (await (await ana.PutAsJsonAsync("/api/projetos", Projeto("um"))).Content.ReadFromJsonAsync<ProjetoV1>())!;
        var b = (await (await ana.PutAsJsonAsync("/api/projetos", Projeto("dois"))).Content.ReadFromJsonAsync<ProjetoV1>())!;
        await ana.PostAsJsonAsync($"/api/projetos/{a.Id}/acessos", new ConcessaoDeAcessoV1("bia@a.com", "editor"));
        Assert.False((await ListarAsync(bia)).Single(p => p.Id == a.Id).PodeApagar);

        Assert.Equal(HttpStatusCode.Forbidden, (await bia.DeleteAsync($"/api/projetos/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync($"/api/projetos/{a.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestor.DeleteAsync($"/api/projetos/{b.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.DeleteAsync($"/api/projetos/{b.Id}")).StatusCode);
        Assert.Empty(await ListarAsync(ana));
    }

    [Fact]
    public async Task O_plano_com_ciclo_ou_slug_invalido_e_recusado_e_sem_login_a_rota_nao_responde()
    {
        var (ana, _, _, _) = await EmpresasAsync();

        var ciclo = await ana.PutAsJsonAsync("/api/projetos", Projeto("ciclo", itens: [Item(1, 2), Item(2, 1)]));
        Assert.Equal(HttpStatusCode.BadRequest, ciclo.StatusCode);
        Assert.Contains("ciclo", await ciclo.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync("/api/projetos", Projeto("Com Espaco"))).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().GetAsync("/api/projetos")).StatusCode);
    }
}
