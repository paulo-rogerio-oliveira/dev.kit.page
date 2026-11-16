using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Api.Tests;

/// <summary>HTTP TestServer e SQLite temporário reais; estados enviados representam o host, sem servidores de preview.</summary>
public sealed class ArtefatosLocaisApiTests : IDisposable
{
    private const string Maquina = "3f2b9c0e5d7a";
    private readonly ApiDeTeste _api = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string Hex(char c, int tamanho = 64) => new(c, tamanho);

    private static RetratoV1 Retrato(DateTimeOffset agora)
    {
        var fontes = new[] { 'a', 'b', 'c', 'd' }.Select(c => new FonteNoRetratoV1(Hex(c), "repo-" + c,
            "feature/dev/projeto-demo", Hex(c, 40), "develop", Hex(c, 40))).ToArray();
        var componentes = new[] { new ComponenteNoRetratoV1("front", Hex('a'), "pronto", 4317, agora, "", "Front iniciado."),
            new ComponenteNoRetratoV1("api", Hex('b'), "pronto", 4318, agora, "", "GET fictício: 200.") };
        var preview = new PreviewLocalNoRetratoV1(Hex('a'), "site", Hex('d', 32), 3, "iniciar", "pronto", true,
            "http://127.0.0.1:4317/", "Jornada representativa confirmada pelo host.", agora, new(true, true, true, true), componentes);
        var artefato = new ArtefatoLocalNoRetratoV1(7, "pronto", "Pacote conferido.", agora.AddMinutes(-5), agora,
            Hex('e'), fontes, [new(Hex('b'), "Api.csproj", "dotnet-publish", "net10.0", null, "produtos/api", "Api")], Hex('f'), 1024,
            new(12, Hex('a'), agora.AddMinutes(-5), agora, null, 0, "Publicação concluída."), [preview],
            new(501, "pacote", fontes[0].Id, fontes[0].Sha, Hex('f'), 1024),
            new(fontes[0].Id, fontes[0].Sha, "170", Hex('f'), 1024, 501, agora.AddDays(1)),
            new("conteudo-identico", [new(fontes[0].Id, "catalogo", fontes[2].Id, Hex('e', 40), fontes[2].Sha,
                Hex('f', 40), Hex('f', 40), "catalogo/catalogo", "conteudo-identico")],
                [new(fontes[0].Id, "tools/Aux.csproj", "exclusão explícita")], new(fontes[0].Id, "pacote", new("", "", "", ""))));
        return new(1, Maquina, agora, [new("demo", "Demonstração", "aprovado", 1, [], new(1, true, [artefato]))], [], []);
    }

    private async Task<HttpClient> DevAsync(HttpClient admin, string login)
    {
        var (dev, _) = await _api.UsuarioAsync(admin, login, Papeis.Dev);
        return dev;
    }

    private static async Task HeartbeatAsync(HttpClient dev)
        => (await dev.PostAsJsonAsync("/api/remoto/heartbeat", new HeartbeatV1(1, Maquina, "Host de demonstração",
            "170", "servico", true, "parado", false))).EnsureSuccessStatusCode();

    private static async Task<RetratoV1> LerAsync(HttpClient dev)
        => (await dev.GetFromJsonAsync<RetratoDaMaquinaV1>($"/api/remoto/maquinas/{Maquina}/retrato"))!.Retrato;

    [Fact]
    public async Task Snapshot_e_timeout_persistem_na_api_sem_prontidao_falsa_retrocesso_ou_acesso_de_outro_usuario()
    {
        var admin = await _api.AdminAsync();
        var ana = await DevAsync(admin, "ana");
        var bia = await DevAsync(admin, "bia");
        await HeartbeatAsync(ana);
        var pronto = Retrato(DateTimeOffset.UtcNow);
        (await ana.PutAsJsonAsync("/api/remoto/retrato", pronto)).EnsureSuccessStatusCode();
        Assert.Equal(JsonSerializer.Serialize(pronto, Json), JsonSerializer.Serialize(await LerAsync(ana), Json));
        Assert.Equal(HttpStatusCode.NotFound, (await bia.GetAsync($"/api/remoto/maquinas/{Maquina}/retrato")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/remoto/maquinas/{Maquina}/retrato")).StatusCode);

        // O PUT inicial cria registro próprio: a chave é usuário + máquina, não só o identificador anônimo.
        var conjuntoDeBia = pronto.Projetos![0].ArtefatosLocais!;
        var deBia = pronto with { Projetos = [pronto.Projetos[0] with { Nome = "Projeto isolado",
            ArtefatosLocais = conjuntoDeBia with { Artefatos = [conjuntoDeBia.Artefatos[0] with { Diagnostico = "Outro pacote." }] } }] };
        (await bia.PutAsJsonAsync("/api/remoto/retrato", deBia)).EnsureSuccessStatusCode();
        Assert.Equal(JsonSerializer.Serialize(deBia, Json), JsonSerializer.Serialize(await LerAsync(bia), Json));
        Assert.Equal(JsonSerializer.Serialize(pronto, Json), JsonSerializer.Serialize(await LerAsync(ana), Json));

        var conjunto = pronto.Projetos![0].ArtefatosLocais!;
        var artefato = conjunto.Artefatos[0];
        var falhou = artefato with { Situacao = "falhou", Diagnostico = "Comando confirmou timeout.", Previews = [],
            Comando = artefato.Comando! with { Exit = 124, MotivoTermino = "timeout", LogParcial = "Publicação parcial preservada." } };
        var timeout = pronto with { GeradoEm = pronto.GeradoEm.AddSeconds(1), Projetos =
            [pronto.Projetos[0] with { ArtefatosLocais = conjunto with { Artefatos = [falhou] } }] };
        (await ana.PutAsJsonAsync("/api/remoto/retrato", timeout)).EnsureSuccessStatusCode();
        var recebido = (await LerAsync(ana)).Projetos![0].ArtefatosLocais!.Artefatos[0];
        Assert.Equal(JsonSerializer.Serialize(falhou, Json), JsonSerializer.Serialize(recebido, Json));
        Assert.Equal(4, recebido.Fontes.Count);
        Assert.Equal(artefato.ConjuntoHash, recebido.ConjuntoHash);
        Assert.Empty(recebido.Previews);
        Assert.Equal(artefato.Publicacao, recebido.Publicacao);
        Assert.Equal(artefato.Pacote, recebido.Pacote);
        Assert.Equal(JsonSerializer.Serialize(artefato.Detalhes, Json), JsonSerializer.Serialize(recebido.Detalhes, Json));

        var atrasado = await ana.PutAsJsonAsync("/api/remoto/retrato", pronto);
        atrasado.EnsureSuccessStatusCode();
        Assert.False((await atrasado.Content.ReadFromJsonAsync<RetratoRecebidoV1>())!.Aplicado);
        Assert.Equal(JsonSerializer.Serialize(timeout, Json), JsonSerializer.Serialize(await LerAsync(ana), Json));
        Assert.Equal(JsonSerializer.Serialize(deBia, Json), JsonSerializer.Serialize(await LerAsync(bia), Json));
    }

    [Theory]
    [InlineData("jornada")]
    [InlineData("segredo")]
    public async Task Conteudo_invalido_e_recusado_sem_substituir_ultimo_retrato_valido(string erro)
    {
        var admin = await _api.AdminAsync();
        var ana = await DevAsync(admin, "ana");
        await HeartbeatAsync(ana);
        var original = Retrato(DateTimeOffset.UtcNow);
        (await ana.PutAsJsonAsync("/api/remoto/retrato", original)).EnsureSuccessStatusCode();
        var conjunto = original.Projetos![0].ArtefatosLocais!;
        var artefato = conjunto.Artefatos[0];
        var preview = artefato.Previews[0];
        artefato = erro == "jornada"
            ? artefato with { Previews = [preview with { Prontidao = preview.Prontidao with { Jornada = false } }] }
            : artefato with { Comando = artefato.Comando! with { LogParcial = "ghp_abcdefghijklmnopqrstuvwxyz0123456789" } };
        var invalido = original with { GeradoEm = original.GeradoEm.AddSeconds(1), Projetos =
            [original.Projetos[0] with { ArtefatosLocais = conjunto with { Artefatos = [artefato] } }] };
        var resposta = await ana.PutAsJsonAsync("/api/remoto/retrato", invalido);
        Assert.Equal(erro == "segredo" ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789", await resposta.Content.ReadAsStringAsync());
        Assert.Equal(JsonSerializer.Serialize(original, Json), JsonSerializer.Serialize(await LerAsync(ana), Json));
    }

    public void Dispose() => _api.Dispose();
}
