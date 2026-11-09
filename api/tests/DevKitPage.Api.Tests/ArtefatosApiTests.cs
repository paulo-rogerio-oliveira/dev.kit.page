using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>
/// Os artefatos do projeto (controle-v4, item 2): o zip recebido com o link de download assinado e com prazo, o teto, a
/// permissão do projeto, o preview guardado (nunca produção) e a retenção. O armazém dos testes é a pasta temporária, e o link é o
/// token cifrado da rota anônima — o mesmo papel da SAS do Blob.
/// </summary>
public sealed class ArtefatosApiTests : IDisposable
{
    private readonly ApiDeTeste _api = new();
    public void Dispose() => _api.Dispose();

    private const string Sha = "3f9c2a1b4d5e6f708192a3b4c5d6e7f809123456";

    private static EnvioDeProjetoV1 Projeto(string slug = "controle-v4")
        => new(slug, $"Projeto {slug}", "empresa", null, ["https://github.com/acme/app-desktop", "https://github.com/acme/portal-web"],
            $"feature/dev/projeto-{slug}", false, "O que o dev quer.", [], true, 2, "feature/dev/semana7", ProjetosV1.Aprovado, null, []);

    private async Task<(HttpClient Ana, HttpClient Bia, HttpClient Caio, long Projeto)> CenarioAsync()
    {
        var admin = await _api.AdminAsync();
        var a = await ApiDeTeste.EmpresaAsync(admin, "Empresa A");
        var b = await ApiDeTeste.EmpresaAsync(admin, "Empresa B");
        var (ana, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "ana@a.com");
        var (bia, _) = await _api.DevPorAdesaoAsync(a.CodigoDeAdesao, "bia@a.com");
        var (caio, _) = await _api.DevPorAdesaoAsync(b.CodigoDeAdesao, "caio@b.com");
        var criado = (await (await ana.PutAsJsonAsync("/api/projetos", Projeto())).Content.ReadFromJsonAsync<ProjetoV1>())!;
        await ana.PostAsJsonAsync($"/api/projetos/{criado.Id}/acessos", new ConcessaoDeAcessoV1("bia@a.com", "leitor"));
        return (ana, bia, caio, criado.Id);
    }

    private static MultipartFormDataContent Pacote(byte[] zip, string? hash = null, string sha = Sha, string nome = "app-desktop-3f9c2a1.zip")
    {
        var corpo = new MultipartFormDataContent
        {
            { new StringContent("https://github.com/acme/app-desktop"), "repositorio" },
            { new StringContent(sha), "sha" },
            { new StringContent(hash ?? Convert.ToHexStringLower(SHA256.HashData(zip))), "hash" },
        };
        var arquivo = new ByteArrayContent(zip);
        arquivo.Headers.ContentType = new("application/zip");
        corpo.Add(arquivo, "arquivo", nome);
        return corpo;
    }

    private static byte[] Zip(int tamanho = 4096) => Enumerable.Range(0, tamanho).Select(i => (byte)(i % 251)).ToArray();

    [Fact]
    public async Task O_zip_recebido_volta_com_o_link_assinado_e_com_prazo_que_baixa_o_mesmo_conteudo_sem_login()
    {
        var (ana, _, _, projeto) = await CenarioAsync();
        var zip = Zip();

        var resposta = await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(zip));
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var artefato = (await resposta.Content.ReadFromJsonAsync<ArtefatoV1>())!;
        Assert.Equal((ArtefatosV1.TipoPacote, Sha, (long)zip.Length), (artefato.Tipo, artefato.Sha, artefato.Tamanho));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(zip)), artefato.Hash);
        Assert.StartsWith("http://localhost/api/artefatos/download/", artefato.Link, StringComparison.Ordinal);
        // O link vence no prazo (24 h), antes da retenção do zip (7 dias), e não leva o caminho do armazém nem a chave.
        Assert.InRange(artefato.ExpiraEm!.Value, DateTimeOffset.UtcNow.AddHours(23), DateTimeOffset.UtcNow.AddHours(25));
        Assert.True(artefato.RetidoAte > artefato.ExpiraEm);
        Assert.DoesNotContain("projetos/", artefato.Link, StringComparison.Ordinal);
        Assert.DoesNotContain(_api.PastaDosArtefatos, artefato.Link, StringComparison.OrdinalIgnoreCase);

        // O link é a autorização: baixa sem login, com o nome do zip; adulterado, 404.
        var anonimo = _api.CreateClient();
        var baixado = await anonimo.GetAsync(new Uri(artefato.Link).PathAndQuery);
        Assert.Equal(HttpStatusCode.OK, baixado.StatusCode);
        Assert.Equal(zip, await baixado.Content.ReadAsByteArrayAsync());
        Assert.Equal("app-desktop-3f9c2a1.zip", baixado.Content.Headers.ContentDisposition!.FileName?.Trim('"'));
        Assert.Equal(HttpStatusCode.NotFound, (await anonimo.GetAsync(new Uri(artefato.Link).PathAndQuery + "x")).StatusCode);

        // O card do Board lê os últimos do projeto, pelo slug do retrato.
        var recentes = (await ana.GetFromJsonAsync<ArtefatosDoProjetoV1[]>("/api/projetos/artefatos"))!;
        var doProjeto = Assert.Single(recentes);
        Assert.Equal(("controle-v4", artefato.Id), (doProjeto.Slug, Assert.Single(doProjeto.Artefatos).Id));
    }

    [Fact]
    public async Task O_zip_acima_do_teto_e_413_e_o_hash_que_nao_confere_e_400_sem_guardar_nada()
    {
        var (ana, _, _, projeto) = await CenarioAsync();

        var grande = await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip(1024 * 1024 + 1)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, grande.StatusCode);
        Assert.Contains("teto de 1 MB", await grande.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var corrompido = await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip(), hash: new string('0', 64)));
        Assert.Equal(HttpStatusCode.BadRequest, corrompido.StatusCode);
        Assert.Contains("não confere", await corrompido.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var semSha = await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip(), sha: "não-é-sha"));
        Assert.Equal(HttpStatusCode.BadRequest, semSha.StatusCode);

        Assert.Empty((await ana.GetFromJsonAsync<ArtefatoV1[]>($"/api/projetos/{projeto}/artefatos"))!);
        Assert.False(Directory.Exists(_api.PastaDosArtefatos) && Directory.EnumerateFiles(_api.PastaDosArtefatos, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public async Task Gerar_exige_editor_ou_dono_o_leitor_so_le_e_outra_empresa_nem_ve()
    {
        var (ana, bia, caio, projeto) = await CenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip()))).StatusCode);

        var doLeitor = await bia.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip()));
        Assert.Equal(HttpStatusCode.Forbidden, doLeitor.StatusCode);
        Assert.Contains("exige editor ou dono", await doLeitor.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single((await bia.GetFromJsonAsync<ArtefatoV1[]>($"/api/projetos/{projeto}/artefatos"))!);

        Assert.Equal(HttpStatusCode.NotFound, (await caio.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await caio.GetAsync($"/api/projetos/{projeto}/artefatos")).StatusCode);
        Assert.Empty((await caio.GetFromJsonAsync<ArtefatosDoProjetoV1[]>("/api/projetos/artefatos"))!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _api.CreateClient().PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip()))).StatusCode);
    }

    [Theory]
    [InlineData("https://portal-controle-v4.eastus2.azurestaticapps.net", "production", "nunca publica em produção")]
    [InlineData("http://portal-controle-v4.eastus2.azurestaticapps.net", "controle-v4", "HTTPS")]
    [InlineData("https://portal-controle-v4.eastus2.azurestaticapps.net/?token=segredo", "controle-v4", "não leva segredo")]
    [InlineData("https://usuario:senha@portal-controle-v4.eastus2.azurestaticapps.net", "controle-v4", "não leva segredo")]
    public async Task O_preview_e_https_sem_segredo_na_url_e_nunca_producao(string url, string ambiente, string motivo)
    {
        var (ana, _, _, projeto) = await CenarioAsync();
        var resposta = await ana.PostAsJsonAsync($"/api/projetos/{projeto}/artefatos",
            new PreviewDoProjetoV1("https://github.com/acme/portal-web", Sha, url, ambiente));
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains(motivo, await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task O_preview_fica_guardado_e_a_retencao_mantem_os_ultimos_do_projeto_apagando_o_zip_que_sai()
    {
        var (ana, _, _, projeto) = await CenarioAsync();
        Assert.Equal(HttpStatusCode.Created, (await ana.PostAsync($"/api/projetos/{projeto}/artefatos", Pacote(Zip()))).StatusCode);
        Assert.Single(Directory.EnumerateFiles(_api.PastaDosArtefatos, "*.zip", SearchOption.AllDirectories));

        const string url = "https://portal-controle-v4.eastus2.azurestaticapps.net";
        ArtefatoV1? ultimo = null;
        for (var i = 0; i < 3; i++)
        {
            var resposta = await ana.PostAsJsonAsync($"/api/projetos/{projeto}/artefatos",
                new PreviewDoProjetoV1("https://github.com/acme/portal-web", Sha[..(7 + i)], url, "controle-v4"));
            Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
            ultimo = await resposta.Content.ReadFromJsonAsync<ArtefatoV1>();
        }
        Assert.Equal((ArtefatosV1.TipoPreview, url, "controle-v4", (DateTimeOffset?)null), (ultimo!.Tipo, ultimo.Link, ultimo.Ambiente, ultimo.ExpiraEm));

        // O teto do teste é 3 por projeto: o pacote, o mais antigo, saiu do banco e do armazém.
        var lista = (await ana.GetFromJsonAsync<ArtefatoV1[]>($"/api/projetos/{projeto}/artefatos"))!;
        Assert.Equal(3, lista.Length);
        Assert.All(lista, a => Assert.Equal(ArtefatosV1.TipoPreview, a.Tipo));
        Assert.Empty(Directory.EnumerateFiles(_api.PastaDosArtefatos, "*.zip", SearchOption.AllDirectories));
        // O card mostra os 3 últimos, do mais novo para o mais velho.
        Assert.Equal(ultimo.Id, Assert.Single((await ana.GetFromJsonAsync<ArtefatosDoProjetoV1[]>("/api/projetos/artefatos"))!).Artefatos[0].Id);
    }

    [Fact]
    public async Task O_post_sem_zip_nem_json_e_415_e_o_formulario_sem_arquivo_e_400()
    {
        var (ana, _, _, projeto) = await CenarioAsync();
        Assert.Equal(HttpStatusCode.UnsupportedMediaType,
            (await ana.PostAsync($"/api/projetos/{projeto}/artefatos", new StringContent("zip", System.Text.Encoding.UTF8, "text/plain"))).StatusCode);
        var semArquivo = new MultipartFormDataContent { { new StringContent(Sha), "sha" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await ana.PostAsync($"/api/projetos/{projeto}/artefatos", semArquivo)).StatusCode);
    }
}
