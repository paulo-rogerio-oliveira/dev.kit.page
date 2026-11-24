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

    private static string Skill(string texto) => FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo("exemplo", "Exemplo", texto, [], [], false, false, false));
    private static GeradorDoManifesto.ItemLido Lido(string texto) => GeradorDoManifesto.Ler("skill", "exemplo", "skills/exemplo.json", Skill(texto));
    private static ManifestoDoCatalogoV1 Inicial => GeradorDoManifesto.Gerar(GeradorDoManifesto.Vazio, [Lido("v1")], 151).Manifesto;

    private async Task<PublicacaoGitV1> PrepararAsync()
    {
        _api.CreateClient();
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DevKitPageDb>();
        var basico = await db.Catalogos.SingleAsync(c => c.Basico);
        var v = new VersaoDoCatalogo { CatalogoId = basico.Id, Versao = 1, Tag = "catalogo-v1", Manifesto = ManifestoJson.Serializar(Inicial),
            Sha256 = new string('0', 64), PublicadaEmUtc = DateTime.UtcNow, PublicadaPor = "semente-isolada" };
        db.VersoesDoCatalogo.Add(v);
        // Um rascunho divergente jamais pode entrar no pacote nem ser sobrescrito pelo CD.
        db.ItensEditaveis.Add(new ItemDoCatalogoEditavel { CatalogoId = basico.Id, Tipo = "skill", Nome = "rascunho",
            Arquivo = "skills/rascunho.json", Conteudo = "rascunho-preservado", AlteradoEmUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return Pacote(GeradorDoManifesto.Gerar(Inicial, [Lido("v2")], 151).Manifesto, Skill("v2"));
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

    private HttpClient Cliente(string? alterar = null)
    {
        var claims = new Dictionary<string, object> { ["repository"] = "dono/catalogo", ["repository_id"] = "42", ["environment"] = "catalogo-producao",
            ["event_name"] = "release", ["ref"] = "refs/tags/catalogo-v2", ["sha"] = Sha,
            ["workflow_ref"] = "dono/catalogo/.github/workflows/catalogo.yml@refs/tags/catalogo-v2" };
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

    [Theory]
    [InlineData("hash")][InlineData("conteudo")][InlineData("versao")][InlineData("minimo")][InlineData("historico")][InlineData("extra")][InlineData("path")]
    public async Task Adulteracao_recusada_sem_release_ou_recibo(string caso)
    {
        var pedido = await PrepararAsync();
        var m = GeradorDoManifesto.Gerar(Inicial, [Lido("v2")], 151).Manifesto;
        pedido = caso switch
        {
            "hash" => pedido with { Sha256 = new string('f', 64) },
            "conteudo" => Pacote(m, Skill("adulterado")),
            "versao" => pedido with { Versao = 3 },
            "minimo" => Pacote(m with { MinDevKit = 1 }, Skill("v2")),
            "historico" => Pacote(m with { Itens = [m.Itens[0] with { Versao = 77 }] }, Skill("v2")),
            "extra" => Pacote(m, Skill("v2"), "skills/extra.json"),
            _ => Pacote(m, Skill("v2"), "../fora.json"),
        };
        using var cliente = Cliente();
        Assert.False((await cliente.PostAsJsonAsync(Raiz + "/publicar", pedido)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await cliente.GetAsync(Raiz + "/recibos/" + Sha)).StatusCode);
        using var scope = _api.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<DevKitPageDb>().VersoesDoCatalogo.CountAsync());
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
