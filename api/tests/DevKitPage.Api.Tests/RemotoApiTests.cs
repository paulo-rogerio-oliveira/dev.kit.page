using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevKitPage.Api;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api.Tests;

/// <summary>Os envios de exemplo do controle remoto, compartilhados pelos testes da API.</summary>
internal static class DadosDoRemoto
{
    public const string Maquina = "3f2b1c9e5d7a4b60a1c2d3e4f5061728";
    public const string Heartbeat = RemotoV1.Rotas + "/heartbeat";
    public const string Retrato = RemotoV1.Rotas + "/retrato";
    public const string Maquinas = RemotoV1.Rotas + "/maquinas";

    public static HeartbeatV1 Sinal(string maquina = Maquina, string apelido = "Notebook")
        => new(RemotoV1.Versao, maquina, apelido, "1.61.0", true, RemotoV1.AgenteAtivo, 1);

    public static RetratoV1 Foto(string maquina = Maquina, string nome = "projeto", DateTimeOffset? geradoEm = null, string titulo = "Item 20 · layout do plano")
        => new(RemotoV1.Versao, maquina, geradoEm ?? DateTimeOffset.UtcNow.AddMinutes(-1),
            [new("projeto", nome, ProjetosV1.Aprovado, 21, 20, 1, 10, 10)],
            [new("a7d04c1e", titulo, "Fluxo simples", "Implementação", RemotoV1.FluxoRodando, "projeto", 20, null)],
            [new("integracao-item-20", RemotoV1.PendenciaAprovacao, "Integrar o item 20 no projeto?", "Nota 100", ["Revisar e aprovar", "Pedir ajuste"], "projeto", 20, null)]);

    public static async Task<PainelRemotoV1> PainelAsync(HttpClient cliente, string maquina = Maquina)
        => (await cliente.GetFromJsonAsync<PainelRemotoV1>($"{Maquinas}/{maquina}"))!;
}

/// <summary>Quem entra nas rotas do controle remoto: só o JWT do usuário, nunca a chave da máquina.</summary>
public sealed class RemotoAutenticacaoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private static async Task<HttpStatusCode[]> TodasAsRotasAsync(HttpClient cliente) =>
    [
        (await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal())).StatusCode,
        (await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto())).StatusCode,
        (await cliente.GetAsync(DadosDoRemoto.Maquinas)).StatusCode,
        (await cliente.GetAsync($"{DadosDoRemoto.Maquinas}/{DadosDoRemoto.Maquina}")).StatusCode,
    ];

    [Fact]
    public async Task Sem_token_toda_rota_e_401()
        => Assert.All(await TodasAsRotasAsync(_api.CreateClient()), s => Assert.Equal(HttpStatusCode.Unauthorized, s));

    [Fact]
    public async Task A_chave_da_maquina_nao_publica_nem_le()
        => Assert.All(await TodasAsRotasAsync(await _api.MaquinaAsync(DadosDoRemoto.Maquina)), s => Assert.Equal(HttpStatusCode.Unauthorized, s));

    [Fact]
    public async Task Token_vencido_ou_assinado_por_outro_segredo_e_401()
    {
        var opcoes = _api.Services.GetRequiredService<IOptions<OpcoesDeAutenticacao>>();
        var ana = new Usuario { Id = 1, Login = "admin", EhAdmin = true };

        var vencido = _api.CreateClient();
        vencido.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            new EmissorDeToken(opcoes, new RelogioFixo(DateTimeOffset.UtcNow.AddHours(-2))).Emitir(ana).Token);
        Assert.All(await TodasAsRotasAsync(vencido), s => Assert.Equal(HttpStatusCode.Unauthorized, s));

        var outroSegredo = Options.Create(new OpcoesDeAutenticacao { Segredo = new string('x', 48), Emissor = opcoes.Value.Emissor, Audiencia = opcoes.Value.Audiencia });
        var forjado = _api.CreateClient();
        forjado.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new EmissorDeToken(outroSegredo, TimeProvider.System).Emitir(ana).Token);
        Assert.All(await TodasAsRotasAsync(forjado), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
    }

    [Fact]
    public async Task Com_a_troca_de_senha_pendente_e_403()
    {
        var cliente = _api.CreateClient();
        var primeiro = await _api.LoginAsync(cliente);
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", primeiro.Token);

        Assert.All(await TodasAsRotasAsync(cliente), s => Assert.Equal(HttpStatusCode.Forbidden, s));
    }

    [Fact]
    public async Task O_limite_de_envios_e_por_usuario_e_a_leitura_nao_conta()
    {
        using var api = new ApiDeTeste(remotoPorMinuto: 2);
        var admin = await api.AdminAsync();
        var empresa = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var (ana, _) = await api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await api.DevPorAdesaoAsync(empresa.CodigoDeAdesao, "bia@a.com");

        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ana.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto())).StatusCode);

        var terceiro = await ana.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal());
        Assert.Equal(HttpStatusCode.TooManyRequests, terceiro.StatusCode);
        Assert.True(terceiro.Headers.Contains("Retry-After"));

        Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync(DadosDoRemoto.Maquinas)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bia.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal())).StatusCode);
    }

    private sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => agora;
    }
}

/// <summary>A máquina tem dono: nem o colega de empresa, nem o gestor, nem o admin leem ou publicam nela.</summary>
public sealed class RemotoIsolamentoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private async Task<(HttpClient Ana, HttpClient Bia, HttpClient Gestor, HttpClient Caio, HttpClient Admin)> UsuariosAsync()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");
        var gestor = await _api.GestorAsync(admin, a.Id, "gestor.a");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        return (ana, bia, gestor, caio, admin);
    }

    [Fact]
    public async Task So_o_dono_le_a_maquina_e_para_os_outros_ela_nao_existe()
    {
        var (ana, bia, gestor, caio, admin) = await UsuariosAsync();
        await ana.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal());
        await ana.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto());

        Assert.Single((await ana.GetFromJsonAsync<MaquinaRemotaV1[]>(DadosDoRemoto.Maquinas))!);
        Assert.Equal("projeto", Assert.Single((await DadosDoRemoto.PainelAsync(ana)).Retrato!.Projetos).Nome);

        foreach (var outro in new[] { bia, gestor, caio, admin })
        {
            Assert.Empty((await outro.GetFromJsonAsync<MaquinaRemotaV1[]>(DadosDoRemoto.Maquinas))!);
            Assert.Equal(HttpStatusCode.NotFound, (await outro.GetAsync($"{DadosDoRemoto.Maquinas}/{DadosDoRemoto.Maquina}")).StatusCode);
        }
    }

    [Fact]
    public async Task O_mesmo_guid_em_dois_usuarios_sao_duas_maquinas_que_nao_se_enxergam()
    {
        var (ana, bia, _, _, _) = await UsuariosAsync();
        await ana.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal(apelido: "Da Ana"));
        await bia.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal(apelido: "Da Bia"));
        await ana.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto(nome: "Projeto da Ana"));
        await bia.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto(nome: "Projeto da Bia"));

        var deAna = await DadosDoRemoto.PainelAsync(ana);
        var deBia = await DadosDoRemoto.PainelAsync(bia);

        Assert.Equal(("Da Ana", "Projeto da Ana"), (deAna.Maquina.Apelido, deAna.Retrato!.Projetos[0].Nome));
        Assert.Equal(("Da Bia", "Projeto da Bia"), (deBia.Maquina.Apelido, deBia.Retrato!.Projetos[0].Nome));
    }

    [Fact]
    public async Task Publicar_na_maquina_de_outro_usuario_nao_mexe_nela()
    {
        var (ana, bia, _, _, _) = await UsuariosAsync();
        await ana.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal());
        await ana.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto(nome: "Projeto da Ana"));

        var invasao = await bia.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto(nome: "Invadido", geradoEm: DateTimeOffset.UtcNow));

        Assert.Equal(HttpStatusCode.NotFound, invasao.StatusCode);
        Assert.Equal("Projeto da Ana", (await DadosDoRemoto.PainelAsync(ana)).Retrato!.Projetos[0].Nome);
    }

    [Fact]
    public async Task O_corpo_nao_escolhe_o_dono()
    {
        var (ana, _, _, _, _) = await UsuariosAsync();
        var corpo = JsonSerializer.SerializeToNode(DadosDoRemoto.Sinal(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        corpo["usuarioId"] = 1;

        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PutAsJsonAsync(DadosDoRemoto.Heartbeat, corpo)).StatusCode);
    }
}

/// <summary>
/// O contrato v1 pela rota de verdade, com os JSON de <c>ContratoDoRemoto/</c>: é por eles que o serviço do dev.kit e o Board
/// web falam com a API. E o que não pode sair da máquina: fora do contrato é 400, token ou dado de cliente é 422.
/// </summary>
public sealed class ContratoDoRemotoApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private sealed record Textos(
        string[] Aceitos, string[] Segredo, string[] DadoDeCliente, string[] IdentificadoresAceitos, string[] IdentificadoresComDado, string[] CodigosComSegredo);

    private static string Fixture(string nome)
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var arquivo = Path.Combine(pasta.FullName, "ContratoDoRemoto", nome);
            if (File.Exists(arquivo))
                return File.ReadAllText(arquivo);
        }

        throw new FileNotFoundException($"ContratoDoRemoto/{nome} não encontrado.");
    }

    private static StringContent Json(string corpo) => new(corpo, Encoding.UTF8, "application/json");

    /// <summary>Os caminhos e os tipos de um JSON: é a forma que o contrato trava, sem os valores.</summary>
    private static IReadOnlyList<string> Forma(JsonElement elemento, string caminho = "$")
    {
        var linhas = new List<string>();
        void Visitar(JsonElement e, string aqui)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var campo in e.EnumerateObject())
                        Visitar(campo.Value, $"{aqui}.{campo.Name}");
                    break;
                case JsonValueKind.Array:
                    if (e.GetArrayLength() > 0)
                        Visitar(e[0], $"{aqui}[]");
                    break;
                default:
                    linhas.Add($"{aqui}:{(e.ValueKind is JsonValueKind.True or JsonValueKind.False ? "bool" : e.ValueKind.ToString())}");
                    break;
            }
        }

        Visitar(elemento, caminho);
        return linhas.Order(StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task O_heartbeat_do_contrato_e_aceito_e_a_resposta_diz_o_proximo()
    {
        var cliente = await _api.AdminAsync();

        var resposta = await cliente.PutAsync(DadosDoRemoto.Heartbeat, Json(Fixture("heartbeat.json")));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["maquinaId", "proximoEmSegundos", "recebidoEm"], corpo.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(DadosDoRemoto.Maquina, corpo.GetProperty("maquinaId").GetString());
        Assert.Equal(60, corpo.GetProperty("proximoEmSegundos").GetInt32());
    }

    [Fact]
    public async Task O_retrato_do_contrato_volta_igual_e_a_leitura_tem_a_forma_do_contrato()
    {
        var cliente = await _api.AdminAsync();
        await cliente.PutAsync(DadosDoRemoto.Heartbeat, Json(Fixture("heartbeat.json")));

        var recebido = await cliente.PutAsync(DadosDoRemoto.Retrato, Json(Fixture("retrato.json")));
        Assert.Equal(HttpStatusCode.OK, recebido.StatusCode);
        var corpo = await recebido.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(["aplicado", "geradoEm", "maquinaId", "recebidoEm"], corpo.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.True(corpo.GetProperty("aplicado").GetBoolean());

        var painel = await (await cliente.GetAsync($"{DadosDoRemoto.Maquinas}/{DadosDoRemoto.Maquina}")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(Fixture("retrato.json")).RootElement, painel.GetProperty("retrato")));
        Assert.Equal(Forma(JsonDocument.Parse(Fixture("painel.json")).RootElement).Where(l => !l.Contains(":Null", StringComparison.Ordinal)),
            Forma(painel).Where(l => !l.Contains(":Null", StringComparison.Ordinal)));

        var lista = await cliente.GetFromJsonAsync<JsonElement>(DadosDoRemoto.Maquinas);
        Assert.Equal(Forma(JsonDocument.Parse(Fixture("painel.json")).RootElement.GetProperty("maquina"), "$[]"), Forma(lista));
    }

    [Fact]
    public async Task Cada_texto_do_contrato_e_aceito_ou_recusado_pelo_motivo_certo()
    {
        var textos = JsonSerializer.Deserialize<Textos>(Fixture("textos.json"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var cliente = await _api.AdminAsync();
        await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal());

        foreach (var texto in textos.Aceitos)
            Assert.True((await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto(titulo: texto))).IsSuccessStatusCode, texto);

        async Task RecusaAsync(string texto, string motivo)
        {
            var resposta = await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto(titulo: texto));
            Assert.True(resposta.StatusCode == HttpStatusCode.UnprocessableEntity, $"{texto}: {resposta.StatusCode}");
            var problema = await resposta.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal((motivo, "fluxos[0].titulo"), (problema.GetProperty("motivo").GetString(), problema.GetProperty("campo").GetString()));
            Assert.DoesNotContain(texto, problema.GetRawText(), StringComparison.Ordinal);
        }

        foreach (var texto in textos.Segredo)
            await RecusaAsync(texto, RemotoV1.MotivoSegredo);
        foreach (var texto in textos.DadoDeCliente)
            await RecusaAsync(texto, RemotoV1.MotivoDadoDeCliente);
    }

    [Fact]
    public async Task Cada_identificador_e_codigo_do_contrato_e_aceito_ou_recusado_e_nada_e_gravado()
    {
        var textos = JsonSerializer.Deserialize<Textos>(Fixture("textos.json"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var cliente = await _api.AdminAsync();
        await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal());
        var foto = DadosDoRemoto.Foto(nome: "Antes", geradoEm: DateTimeOffset.UtcNow.AddMinutes(-30));
        await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, foto);

        async Task<(HttpStatusCode Status, string? Motivo, string? Campo)> EnviarAsync(RetratoV1 retrato)
        {
            var resposta = await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, retrato with { GeradoEm = DateTimeOffset.UtcNow.AddMinutes(-20), Projetos = [retrato.Projetos[0] with { Nome = "Depois" }] });
            if (resposta.StatusCode != HttpStatusCode.UnprocessableEntity)
                return (resposta.StatusCode, null, null);
            var problema = await resposta.Content.ReadFromJsonAsync<JsonElement>();
            return (resposta.StatusCode, problema.GetProperty("motivo").GetString(), problema.GetProperty("campo").GetString());
        }

        foreach (var id in textos.IdentificadoresComDado)
            Assert.Equal((HttpStatusCode.UnprocessableEntity, RemotoV1.MotivoDadoDeCliente, "fluxos[0].id"), await EnviarAsync(foto with { Fluxos = [foto.Fluxos[0] with { Id = id }] }));
        foreach (var codigo in textos.CodigosComSegredo)
            Assert.Equal((HttpStatusCode.UnprocessableEntity, RemotoV1.MotivoSegredo, "fluxos[0].situacao"), await EnviarAsync(foto with { Fluxos = [foto.Fluxos[0] with { Situacao = codigo }] }));
        Assert.Equal("Antes", (await DadosDoRemoto.PainelAsync(cliente)).Retrato!.Projetos[0].Nome);

        foreach (var id in textos.IdentificadoresAceitos)
            Assert.Equal(HttpStatusCode.OK, (await EnviarAsync(foto with { Fluxos = [foto.Fluxos[0] with { Id = id }] })).Status);
    }

    [Fact]
    public async Task O_agente_com_token_nao_entra_pelo_heartbeat()
    {
        var cliente = await _api.AdminAsync();

        var resposta = await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal() with { Agente = "sk-abcdefghijklmnopqrstuvwxyz0" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("agente", (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("campo").GetString());
        Assert.Empty((await cliente.GetFromJsonAsync<MaquinaRemotaV1[]>(DadosDoRemoto.Maquinas))!);
    }

    [Theory]
    [InlineData("heartbeat.json", DadosDoRemoto.Heartbeat, "text/plain")]
    [InlineData("heartbeat.json", DadosDoRemoto.Heartbeat, null)]
    [InlineData("heartbeat.json", DadosDoRemoto.Heartbeat, "application/json; charset=nao-existe")]
    [InlineData("retrato.json", DadosDoRemoto.Retrato, "text/plain")]
    [InlineData("retrato.json", DadosDoRemoto.Retrato, null)]
    [InlineData("retrato.json", DadosDoRemoto.Retrato, "application/json; charset=nao-existe")]
    public async Task O_corpo_que_nao_e_json_e_415_e_nao_500(string arquivo, string rota, string? tipo)
    {
        var cliente = await _api.AdminAsync();
        var corpo = new ByteArrayContent(Encoding.UTF8.GetBytes(Fixture(arquivo)));
        if (tipo is not null)
            corpo.Headers.TryAddWithoutValidation("Content-Type", tipo);

        var resposta = await cliente.PutAsync(rota, corpo);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, resposta.StatusCode);
        Assert.Contains("application/json", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_corpo_que_nao_e_utf8_valido_ou_vazio_e_400_e_nao_500()
    {
        var cliente = await _api.AdminAsync();
        var invalido = new ByteArrayContent([0x7B, 0x22, 0x76, 0x65, 0x72, 0x73, 0x61, 0x6F, 0x22, 0x3A, 0x22, 0xFF, 0xFE, 0x22, 0x7D]);
        invalido.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsync(DadosDoRemoto.Heartbeat, invalido)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsync(DadosDoRemoto.Heartbeat, Json(""))).StatusCode);
    }

    [Fact]
    public async Task O_retrato_recusado_nao_grava_nada_e_o_anterior_continua_valendo()
    {
        var cliente = await _api.AdminAsync();
        await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal());
        await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto(nome: "Antes", geradoEm: DateTimeOffset.UtcNow.AddMinutes(-5)));

        var foto = DadosDoRemoto.Foto(nome: "Depois");
        var pendencia = foto.Pendencias[0] with { Opcoes = ["Aprovar", "senha: hunter2"] };
        var resposta = await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, foto with { Pendencias = [pendencia] });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("pendencias[0].opcoes[1]", (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("campo").GetString());
        Assert.Equal("Antes", (await DadosDoRemoto.PainelAsync(cliente)).Retrato!.Projetos[0].Nome);
    }

    [Fact]
    public async Task O_heartbeat_tambem_recusa_token_e_dado_de_cliente()
    {
        var cliente = await _api.AdminAsync();

        var token = await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal(apelido: "ghp_abcdefghijklmnopqrstuvwxyz0123456789"));
        var caminho = await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal(apelido: @"C:\Users\ana"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, token.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, caminho.StatusCode);
        Assert.Empty((await cliente.GetFromJsonAsync<MaquinaRemotaV1[]>(DadosDoRemoto.Maquinas))!);
    }

    [Theory]
    [InlineData("""{"token":"ghp_abcdefghijklmnopqrstuvwxyz0123456789"}""")]
    [InlineData("""{"definicao":"o texto da demanda do cliente"}""")]
    public async Task Campo_fora_do_contrato_e_400_sem_ecoar_o_valor_na_raiz_e_dentro_das_listas(string extra)
    {
        var cliente = await _api.AdminAsync();
        await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal());
        var valor = JsonDocument.Parse(extra).RootElement.EnumerateObject().Single();
        var opcoes = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var naRaiz = JsonSerializer.SerializeToNode(DadosDoRemoto.Foto(), opcoes)!;
        naRaiz[valor.Name] = valor.Value.GetString();
        var noProjeto = JsonSerializer.SerializeToNode(DadosDoRemoto.Foto(), opcoes)!;
        noProjeto["projetos"]![0]![valor.Name] = valor.Value.GetString();

        foreach (var corpo in new[] { naRaiz, noProjeto })
        {
            var resposta = await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, corpo);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.DoesNotContain(valor.Value.GetString()!, await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        var painel = await DadosDoRemoto.PainelAsync(cliente);
        Assert.Null(painel.Retrato);
    }

    [Fact]
    public async Task Fora_do_contrato_e_400()
    {
        var cliente = await _api.AdminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal() with { Versao = "v2" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsJsonAsync(DadosDoRemoto.Heartbeat, DadosDoRemoto.Sinal("DESKTOP-ANA"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsync(DadosDoRemoto.Heartbeat, Json("{ quebrado"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await cliente.PutAsync(DadosDoRemoto.Retrato, Json("null"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync($"{DadosDoRemoto.Maquinas}/DESKTOP-ANA")).StatusCode);
    }

    [Fact]
    public async Task O_corpo_acima_do_teto_e_413_antes_de_ser_lido()
    {
        var cliente = await _api.AdminAsync();
        var corpo = Json(Fixture("heartbeat.json") + new string(' ', 300 * 1024));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await cliente.PutAsync(DadosDoRemoto.Heartbeat, corpo)).StatusCode);
    }

    [Fact]
    public async Task Retrato_antes_do_heartbeat_e_404_com_o_que_fazer()
    {
        var cliente = await _api.AdminAsync();

        var resposta = await cliente.PutAsJsonAsync(DadosDoRemoto.Retrato, DadosDoRemoto.Foto());

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
        Assert.Contains("heartbeat", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
