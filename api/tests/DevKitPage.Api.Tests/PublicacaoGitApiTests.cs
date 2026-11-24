using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DevKitPage.Api;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace DevKitPage.Api.Tests;

public sealed class PublicacaoGitApiTests : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly ApiDeTeste _api;
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Motor = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string Raiz = "/api/catalogos/basico/automacao";
    private bool _falharArmazem;

    public PublicacaoGitApiTests()
    {
        _api = new ApiDeTeste(configurarServicos: s =>
        {
            s.PostConfigure<OpcoesAutomacaoCatalogo>(o =>
            { o.Repositorio = "dono/catalogo"; o.RepositorioId = "42"; o.Ambiente = "catalogo-producao"; o.Audiencia = "catalogo-test"; });
            s.PostConfigure<JwtBearerOptions>(AutomacaoDoCatalogo.Esquema, o =>
            {
                var cfg = new OpenIdConnectConfiguration { Issuer = AutomacaoDoCatalogo.Emissor };
                cfg.SigningKeys.Add(new RsaSecurityKey(_rsa) { KeyId = "isolado" });
                o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(cfg);
            });
            s.AddScoped<IArmazemDePacotes>(sp => new ArmazemFalhavel(new ArmazemDePacotesNoBanco(sp.GetRequiredService<DevKitPageDb>()), () => _falharArmazem));
        });
    }

    public void Dispose() { _api.Dispose(); _rsa.Dispose(); }

    private static string Skill(string texto, string nome = "exemplo") => FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo(nome, "Exemplo", texto, [], [], false, false, false));
    private static GeradorDoManifesto.ItemLido Lido(string texto, string nome = "exemplo") => GeradorDoManifesto.Ler("skill", nome, $"skills/{nome}.json", Skill(texto, nome));
    private static ManifestoDoCatalogoV1 Inicial => GeradorDoManifesto.Gerar(GeradorDoManifesto.Vazio, [Lido("v1")], 151).Manifesto;

    /// <summary>A release publicada do basico, e um rascunho divergente que o CD jamais pode publicar nem sobrescrever.</summary>
    private async Task SemearAsync(ManifestoDoCatalogoV1 publicada)
    {
        _api.CreateClient();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevKitPageDb>();
        var basico = await db.Catalogos.SingleAsync(c => c.Basico);
        var v = new VersaoDoCatalogo { CatalogoId = basico.Id, Versao = publicada.Versao, Tag = $"catalogo-v{publicada.Versao}", Manifesto = ManifestoJson.Serializar(publicada),
            Sha256 = new string('0', 64), PublicadaEmUtc = DateTime.UtcNow, PublicadaPor = "semente-isolada" };
        db.VersoesDoCatalogo.Add(v);
        db.ItensEditaveis.Add(new ItemDoCatalogoEditavel { CatalogoId = basico.Id, Tipo = "skill", Nome = "rascunho",
            Arquivo = "skills/rascunho.json", Conteudo = "rascunho-preservado", AlteradoEmUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task<PublicacaoGitV1> PrepararAsync()
    {
        await SemearAsync(Inicial);
        return Pacote(GeradorDoManifesto.Gerar(Inicial, [Lido("v2")], 151).Manifesto, Skill("v2"));
    }

    /// <summary>O motivo da recusa como o devcli o le: o detail do ProblemDetails.</summary>
    private static async Task<string> MotivoAsync(HttpResponseMessage resposta)
    {
        Assert.Equal("application/problem+json", resposta.Content.Headers.ContentType?.MediaType);
        return (await resposta.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>())!.Detail!;
    }

    /// <summary>A recusa nao deixa release nem recibo: so as <paramref name="releases"/> semeadas.</summary>
    private async Task SemPublicacaoAsync(HttpClient cliente, int releases)
    {
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync(Raiz + "/recibos/" + Sha)).StatusCode);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevKitPageDb>();
        Assert.Equal(releases, await db.VersoesDoCatalogo.CountAsync());
        Assert.Equal(0, await db.Set<ReciboDoCatalogoGit>().CountAsync());
    }

    private static PublicacaoGitV1 Pacote(ManifestoDoCatalogoV1 manifesto, string texto, string? extra = null, IReadOnlyDictionary<string, string>? adicionais = null, string arquivo = "skills/exemplo.json")
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
        {
            foreach (var (nome, conteudo) in new[] { ("manifesto.json", ManifestoJson.Serializar(manifesto)), (arquivo, texto) }
                .Concat(extra is null ? [] : new[] { (extra, "extra") }).Concat(adicionais?.Select(p => (p.Key, p.Value)) ?? []))
            {
                using var escrita = new StreamWriter(zip.CreateEntry(nome).Open(), new UTF8Encoding(false));
                escrita.Write(conteudo);
            }
        }
        var bytes = memoria.ToArray();
        return new(Sha, $"refs/tags/catalogo-v{manifesto.Versao}", Motor, Convert.ToHexStringLower(SHA256.HashData(bytes)), manifesto.Versao, bytes);
    }

    private HttpClient Cliente(string? alterar = null, int versao = 2)
    {
        var referencia = $"refs/tags/catalogo-v{versao}";
        var claims = new Dictionary<string, object> { ["repository"] = "dono/catalogo", ["repository_id"] = "42", ["environment"] = "catalogo-producao",
            ["event_name"] = "release", ["ref"] = referencia, ["sha"] = Sha,
            ["workflow_ref"] = "dono/catalogo/.github/workflows/catalogo.yml@" + referencia };
        if (alterar is not null && claims.ContainsKey(alterar)) claims[alterar] = "nao-autorizado";
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = alterar == "issuer" ? "https://intruso.test" : AutomacaoDoCatalogo.Emissor,
            Audience = alterar == "audience" ? "outra" : "catalogo-test", Claims = claims,
            NotBefore = DateTime.UtcNow.AddMinutes(-10), Expires = DateTime.UtcNow.AddMinutes(alterar == "expired" ? -1 : 5),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "isolado" }, SecurityAlgorithms.RsaSha256),
        });
        var cliente = _api.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    [Fact]
    public async Task Post_autorizado_serve_bytes_exatos_recibo_idempotente_e_painel_coerente()
    {
        var pedido = await PrepararAsync();
        using var cliente = Cliente();
        var simulado = await cliente.PostAsJsonAsync(Raiz + "/conferir", pedido);
        simulado.EnsureSuccessStatusCode();
        Assert.True((await simulado.Content.ReadFromJsonAsync<ConferenciaGitV1>())!.Simulacao);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync(Raiz + "/recibos/" + Sha)).StatusCode);
        var resposta = await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido);
        resposta.EnsureSuccessStatusCode();
        var primeira = (await resposta.Content.ReadFromJsonAsync<ConferenciaGitV1>())!;
        var replay = await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido);
        replay.EnsureSuccessStatusCode();
        var segunda = (await replay.Content.ReadFromJsonAsync<ConferenciaGitV1>())!;
        Assert.True(segunda.Repeticao);
        Assert.Equal(primeira.Recibo, segunda.Recibo);
        Assert.Equal(primeira.Recibo, await cliente.GetFromJsonAsync<ReciboPublicacaoGitV1>(Raiz + "/recibos/" + Sha));
        using var anonimo = _api.CreateClient();
        var bytes = await anonimo.GetByteArrayAsync(primeira.Recibo.Download);
        Assert.Equal(pedido.Pacote, bytes);
        Assert.Equal(pedido.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var manifesto = (await anonimo.GetFromJsonAsync<ManifestoDoCatalogoV1>(primeira.Recibo.Manifesto))!;
        Assert.Equal(2, manifesto.Versao);
        Assert.Equal("exemplo", Assert.Single(manifesto.Itens).Nome);
        using var admin = await _api.AdminAsync();
        var releases = (await admin.GetFromJsonAsync<ReleaseDoCatalogoV1[]>("/api/catalogos/basico/releases"))!;
        Assert.Equal((2, pedido.Sha256), (releases[0].Versao, releases[0].Sha256));
        var painel = (await admin.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!;
        Assert.Equal(2, Assert.Single(painel).Versao);
        using var scope = _api.Services.CreateScope();
        Assert.Equal("rascunho-preservado", (await scope.ServiceProvider.GetRequiredService<DevKitPageDb>().ItensEditaveis.SingleAsync()).Conteudo);
        Console.WriteLine($"RECIBO ISOLADO sha={Sha} motor={Motor} versao={primeira.Recibo.Versao} hash={pedido.Sha256} GET=conferido");
    }

    [Theory]
    [InlineData("repository")][InlineData("repository_id")][InlineData("environment")][InlineData("event_name")]
    [InlineData("ref")][InlineData("sha")][InlineData("workflow_ref")][InlineData("audience")][InlineData("issuer")][InlineData("expired")]
    public async Task Identidade_audiencia_ref_evento_e_validade_sao_restritos(string alterar)
    {
        var pedido = await PrepararAsync();
        using var cliente = Cliente(alterar);
        Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido)).StatusCode);
    }

    [Fact]
    public async Task Admin_pessoal_e_anonimo_nao_publicam_por_automacao_e_oidc_nao_administra_empresa()
    {
        var pedido = await PrepararAsync();
        using var admin = await _api.AdminAsync();
        using var anonimo = _api.CreateClient();
        foreach (var cliente in new[] { admin, anonimo })
            Assert.Equal(HttpStatusCode.Unauthorized, (await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido)).StatusCode);
        using var oidc = Cliente();
        Assert.False((await oidc.PostAsJsonAsync("/api/catalogos", new CatalogoNovoV1("outro", "Outro", null))).IsSuccessStatusCode);
    }

    /// <summary>
    /// O pacote adulterado continua recusado por ConferirPacote (400), inclusive o hash do manifesto divergente do arquivo
    /// do zip ("conteudo") e o do pacote ("hash"); o item que retrocede e recusado pela sequencia (409).
    /// </summary>
    [Theory]
    [InlineData("hash", HttpStatusCode.BadRequest)][InlineData("conteudo", HttpStatusCode.BadRequest)][InlineData("versao", HttpStatusCode.BadRequest)]
    [InlineData("minimo", HttpStatusCode.BadRequest)][InlineData("extra", HttpStatusCode.BadRequest)][InlineData("path", HttpStatusCode.BadRequest)]
    [InlineData("retrocesso", HttpStatusCode.Conflict)]
    public async Task Adulteracao_recusada_sem_release_ou_recibo(string caso, HttpStatusCode esperado)
    {
        var pedido = await PrepararAsync();
        var m = GeradorDoManifesto.Gerar(Inicial, [Lido("v2")], 151).Manifesto;
        pedido = caso switch
        {
            "hash" => pedido with { Sha256 = new string('f', 64) },
            "conteudo" => Pacote(m, Skill("adulterado")),
            "versao" => pedido with { Versao = 3 },
            "minimo" => Pacote(m with { MinDevKit = 1 }, Skill("v2")),
            "retrocesso" => Pacote(m with { Itens = [m.Itens[0] with { Versao = 0 }] }, Skill("v2")),
            "extra" => Pacote(m, Skill("v2"), "skills/extra.json"),
            _ => Pacote(m, Skill("v2"), "../fora.json"),
        };
        using var cliente = Cliente();
        Assert.Equal(esperado, (await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido)).StatusCode);
        await SemPublicacaoAsync(cliente, 1);
    }

    /// <summary>
    /// O caso que antes esperava a recusa por salto: o manifesto do Git e a fonte, e a versao do catalogo ou do item pode
    /// saltar. A release v1 tem o item na v1; o pacote chega com o catalogo e o item em versoes acumuladas.
    /// </summary>
    [Theory]
    [InlineData(2, 77)]
    [InlineData(5, 3)]
    public async Task Versao_maior_com_salto_e_item_com_versao_acumulada_sao_aceitos(int versao, int versaoDoItem)
    {
        await SemearAsync(Inicial);
        var m = GeradorDoManifesto.Gerar(Inicial, [Lido("v2")], 151).Manifesto;
        var pedido = Pacote(m with { Versao = versao, Itens = [m.Itens[0] with { Versao = versaoDoItem, Catalogo = versao + 40 }] }, Skill("v2"));
        using var cliente = Cliente(versao: versao);
        var resposta = await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido);
        resposta.EnsureSuccessStatusCode();
        Assert.Equal(versao, (await resposta.Content.ReadFromJsonAsync<ConferenciaGitV1>())!.Recibo.Versao);
        Assert.Equal(pedido.Pacote, await cliente.GetByteArrayAsync($"/api/catalogos/basico/releases/{versao}/download"));
        Assert.Equal(versaoDoItem, Assert.Single((await cliente.GetFromJsonAsync<ManifestoDoCatalogoV1>($"/api/catalogos/basico/releases/{versao}/manifesto"))!.Itens).Versao);
    }

    /// <summary>
    /// O par do CD real (run 45313544507), sintetico e isolado: a release 28 publicada e o manifesto 29 do Git com as versoes
    /// acumuladas do repositorio (controlar-devkit v20 para v26, item novo ja na v4, catalogo do Git acima de 29) e o
    /// retirado mantido. A regra antiga recusava (409); agora publica, a versao publicada vai de 28 a 29 e o recibo fica.
    /// </summary>
    [Fact]
    public async Task Par_28_publicada_e_manifesto_29_com_versoes_acumuladas_do_git_publica()
    {
        var antiga = new ItemRetiradoV1("skill", "antiga", 2, 10);
        var publicada = Inicial with { Versao = 28, Itens = [Inicial.Itens[0] with { Nome = "controlar-devkit", Versao = 20, Catalogo = 27, Arquivo = "skills/controlar-devkit.json" }], Retirados = [antiga] };
        await SemearAsync(publicada);
        var m = GeradorDoManifesto.Gerar(publicada, [Lido("v26", "controlar-devkit"), Lido("nova", "fechar-entrega")], 151).Manifesto;
        Assert.Equal((29, 21, 1), (m.Versao, m.Itens[0].Versao, m.Itens[1].Versao)); // o passo unico que a regra antiga exigia
        var manifesto = m with { Itens = [m.Itens[0] with { Versao = 26, Catalogo = 68 }, m.Itens[1] with { Versao = 4, Catalogo = 60 }] };
        Assert.Equal(antiga, Assert.Single(manifesto.Retirados));
        var pedido = Pacote(manifesto, Skill("v26", "controlar-devkit"), arquivo: "skills/controlar-devkit.json",
            adicionais: new Dictionary<string, string> { ["skills/fechar-entrega.json"] = Skill("nova", "fechar-entrega") });
        using var cliente = Cliente(versao: 29);
        var resposta = await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido);
        resposta.EnsureSuccessStatusCode();
        var recibo = (await resposta.Content.ReadFromJsonAsync<ConferenciaGitV1>())!.Recibo;
        Assert.Equal(recibo, await cliente.GetFromJsonAsync<ReciboPublicacaoGitV1>(Raiz + "/recibos/" + Sha));
        using var admin = await _api.AdminAsync();
        Assert.Equal(new[] { 29, 28 }, (await admin.GetFromJsonAsync<ReleaseDoCatalogoV1[]>("/api/catalogos/basico/releases"))!.Select(r => r.Versao).Take(2));
        Assert.Equal(29, Assert.Single((await admin.GetFromJsonAsync<CatalogoV1[]>("/api/catalogos"))!).Versao);
        Assert.Equal(ManifestoJson.Serializar(manifesto), ManifestoJson.Serializar((await cliente.GetFromJsonAsync<ManifestoDoCatalogoV1>(recibo.Manifesto))!));
        // A maquina que esta na 28 recebe a 29 com os dois itens (o catalogo do Git, 68 e 60, passa do desde).
        using var anonimo = _api.CreateClient();
        var delta = (await anonimo.GetFromJsonAsync<CatalogoBaseV1>("/api/catalogo/base?desde=28"))!;
        Assert.Equal((29, 2, pedido.Sha256), (delta.Manifesto.Versao, delta.Manifesto.Itens.Count, delta.Sha256));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(2)]
    public async Task Versao_igual_ou_menor_que_a_publicada_responde_409_com_as_duas_versoes(int recebida)
    {
        var publicada = Inicial with { Versao = 3, Itens = [Inicial.Itens[0] with { Catalogo = 3 }] };
        await SemearAsync(publicada);
        var m = GeradorDoManifesto.Gerar(Inicial, [Lido("v2")], 151).Manifesto;
        using var cliente = Cliente(versao: recebida);
        var resposta = await cliente.PostAsJsonAsync(Raiz + "/publicar", Pacote(m with { Versao = recebida }, Skill("v2")));
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal($"A versao recebida ({recebida}) deve ser maior que a release publicada (3); igual ou menor substituiria uma release.", await MotivoAsync(resposta));
        await SemPublicacaoAsync(cliente, 1);
    }

    /// <summary>A recusa da sequencia nomeia o item e as duas versoes, sem gravar release nem recibo.</summary>
    [Theory]
    [InlineData("item", "O item skill:exemplo retrocede da v5 (release 1) para a v4 (manifesto 2).")]
    [InlineData("saiu", "O item skill:antiga (v3 na release 1) saiu do manifesto 2 sem entrar nos retirados.")]
    [InlineData("retirado", "O retirado skill:velha retrocede da v6 (release 1) para a v2 (manifesto 2).")]
    public async Task Item_ou_retirado_que_retrocede_ou_some_responde_409_com_o_motivo(string caso, string motivo)
    {
        var publicada = Inicial with
        {
            Itens = caso == "saiu"
                ? [Inicial.Itens[0] with { Versao = 5 }, Inicial.Itens[0] with { Nome = "antiga", Versao = 3, Arquivo = "skills/antiga.json" }]
                : [Inicial.Itens[0] with { Versao = 5 }],
            Retirados = [new ItemRetiradoV1("skill", "velha", 6, 1)],
        };
        await SemearAsync(publicada);
        var m = GeradorDoManifesto.Gerar(Inicial, [Lido("v2")], 151).Manifesto;
        var manifesto = m with
        {
            Itens = [m.Itens[0] with { Versao = caso == "item" ? 4 : 5 }],
            Retirados = [new ItemRetiradoV1("skill", "velha", caso == "retirado" ? 2 : 6, 1)],
        };
        using var cliente = Cliente();
        var resposta = await cliente.PostAsJsonAsync(Raiz + "/publicar", Pacote(manifesto, Skill("v2")));
        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal(motivo, await MotivoAsync(resposta));
        await SemPublicacaoAsync(cliente, 1);
    }

    [Fact]
    public async Task Mesmo_sha_com_outro_pacote_recusa_sem_substituir()
    {
        var pedido = await PrepararAsync();
        using var cliente = Cliente();
        (await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido)).EnsureSuccessStatusCode();
        var outro = Pacote(GeradorDoManifesto.Gerar(Inicial, [Lido("outro")], 151).Manifesto, Skill("outro"));
        Assert.Equal(HttpStatusCode.Conflict, (await cliente.PostAsJsonAsync(Raiz + "/publicar", outro)).StatusCode);
        Assert.Equal(pedido.Pacote, await cliente.GetByteArrayAsync("/api/catalogos/basico/releases/2/download"));
    }

    [Fact]
    public async Task Nome_da_skill_com_espaco_preserva_caminho_portatil_declarado_no_manifesto()
    {
        await PrepararAsync();
        const string arquivo = "skills/resolver-conflitos.json";
        var texto = FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo("resolver conflitos", "Resolver conflitos", "passos", [], [], false, false, false));
        var lido = GeradorDoManifesto.Ler("skill", "resolver conflitos", arquivo, texto);
        var manifesto = GeradorDoManifesto.Gerar(Inicial, [lido], 151).Manifesto;
        var pedido = Pacote(manifesto, texto, arquivo: arquivo);
        using var cliente = Cliente();
        (await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido)).EnsureSuccessStatusCode();
        Assert.Equal(pedido.Pacote, await cliente.GetByteArrayAsync("/api/catalogos/basico/releases/2/download"));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Pacote_atual_com_gatilho_e_referencias_do_motor_publica_ou_recusa_dependencia_ausente(bool dependenciaAusente)
    {
        await PrepararAsync();
        // Arquivo avaliador.json e hash do catalogo Git v65, motor 2a7288592c3f3bbb4f04ecdf19c515a805f15e45.
        const string gatilho = """
            { "formato":"devkit-gatilho", "versao":1, "nome":"Avaliador", "eventos":["responder"], "repositorios":[], "branch":"",
              "plano":"Avalie o que foi feito e dê uma nota de 10 a 100 e sempre notifique na task que está avaliando",
              "prompt":"", "agente":"", "modelo":"", "somenteEscolhido":false, "avaliador":false, "distribuir":"base" }
            """;
        var hook = GeradorDoManifesto.Ler(GatilhoDoCatalogo.Tipo, "avaliador", "gatilhos/avaliador.json", gatilho);
        Assert.Equal("e671bfee0633b476904e16ca43ea90904ca088abfbf5f58e1a51b8a05f6b9daa", hook.Hash);
        var fluxo = $$"""
            { "formato":"devkit-fluxo", "versao":1, "nome":"Avulsa", "descricao":"Avulsa",
              "desenho":{"nodes":[{"id":"avaliador","kind":"Agent","title":"Avaliador","createIfMissing":true}]},
              "agentes":[], "planos":[{{(dependenciaAusente ? "{\"referencia\":\"ausente\"}" : "")}}] }
            """;
        var lido = GeradorDoManifesto.Ler("fluxo", "avulsa", "fluxos/avulsa.json", fluxo);
        Assert.Contains("gatilho:avaliador", lido.Dependencias);
        var m = GeradorDoManifesto.Gerar(Inicial, [Lido("v2"), hook, lido], 151).Manifesto;
        var pedido = Pacote(m, Skill("v2"), adicionais: new Dictionary<string, string> { ["gatilhos/avaliador.json"] = gatilho, ["fluxos/avulsa.json"] = fluxo });
        using var cliente = Cliente();
        var resposta = await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido);
        if (dependenciaAusente)
        {
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Contains("plan:ausente", await resposta.Content.ReadAsStringAsync());
        }
        else
        {
            resposta.EnsureSuccessStatusCode();
            Assert.Equal(pedido.Pacote, await cliente.GetByteArrayAsync("/api/catalogos/basico/releases/2/download"));
        }
    }

    [Fact]
    public async Task Duas_execucoes_concorrentes_devolvem_mesmo_recibo_e_uma_release()
    {
        var pedido = await PrepararAsync();
        using var a = Cliente(); using var b = Cliente();
        var respostas = await Task.WhenAll(Task.Run(() => a.PostAsJsonAsync(Raiz + "/publicar", pedido)), Task.Run(() => b.PostAsJsonAsync(Raiz + "/publicar", pedido)));
        foreach (var resposta in respostas) resposta.EnsureSuccessStatusCode();
        var recibos = await Task.WhenAll(respostas.Select(r => r.Content.ReadFromJsonAsync<ConferenciaGitV1>()));
        Assert.Equal(recibos[0]!.Recibo, recibos[1]!.Recibo);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevKitPageDb>();
        Assert.Equal(2, await db.VersoesDoCatalogo.CountAsync());
        Assert.Equal(1, await db.Set<ReciboDoCatalogoGit>().CountAsync());
    }

    [Fact]
    public async Task Erro_parcial_no_armazem_reverte_release_zip_e_recibo_e_permite_repetir()
    {
        var pedido = await PrepararAsync();
        _falharArmazem = true;
        using var cliente = Cliente();
        Assert.Equal(HttpStatusCode.InternalServerError, (await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido)).StatusCode);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DevKitPageDb>();
            Assert.Equal(1, await db.VersoesDoCatalogo.CountAsync());
            Assert.Equal(0, await db.PacotesDoCatalogo.CountAsync());
            Assert.Equal(0, await db.Set<ReciboDoCatalogoGit>().CountAsync());
        }
        _falharArmazem = false;
        (await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido)).EnsureSuccessStatusCode();
    }

    private sealed class ArmazemFalhavel(IArmazemDePacotes real, Func<bool> falhar) : IArmazemDePacotes
    {
        public async Task GuardarAsync(long id, byte[] bytes, CancellationToken ct)
        { await real.GuardarAsync(id, bytes, ct); if (falhar()) throw new IOException("Falha isolada apos gravar zip."); }
        public Task<byte[]?> AbrirAsync(long id, CancellationToken ct) => real.AbrirAsync(id, ct);
    }
}
