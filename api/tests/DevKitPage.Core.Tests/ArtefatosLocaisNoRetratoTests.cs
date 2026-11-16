using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

public sealed partial class ArtefatosLocaisNoRetratoTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DateTimeOffset Agora = DateTimeOffset.Parse("2026-11-16T08:00:00Z");
    private static string Hex(char c, int tamanho = 64) => new(c, tamanho);
    private static ArtefatosLocaisNoRetratoV1 Conjunto()
    {
        var repo = Hex('a');
        var fontes = new[] { new FonteNoRetratoV1(repo, "portal", "feature/dev/projeto-demo", Hex('b', 40), "develop", Hex('c', 40)) };
        var componentes = new[] { new ComponenteNoRetratoV1("front", repo, "pronto", 4317, Agora, "", "Front iniciado."),
            new ComponenteNoRetratoV1("api", repo, "pronto", 4318, Agora, "", "GET fictício: 200.") };
        var preview = new PreviewLocalNoRetratoV1(repo, "site", Hex('d', 32), 3, "iniciar", "pronto", true,
            "http://127.0.0.1:4317/", "Jornada confirmada.", Agora, new(true, true, true, true), componentes);
        var artefato = new ArtefatoLocalNoRetratoV1(7, "pronto", "Pacote conferido.", Agora.AddMinutes(-5), Agora,
            Hex('e'), fontes, [new(repo, "Api.csproj", "dotnet", "net10.0", null)], Hex('f'), 1024,
            new(12, Hex('a'), Agora.AddMinutes(-5), Agora, null, 0, "Publicação concluída."), [preview]);
        return new(1, false, [artefato]);
    }
    private static RetratoV1 Retrato(ArtefatosLocaisNoRetratoV1? conjunto) => new(1, "host-demo", Agora,
        [new("demo", "Demonstração", "aprovado", 1, [], conjunto)], [], []);

    [Fact]
    public void Pacote_persiste_com_identidade_propria_em_falha_ou_sucesso_sem_alterar_conjunto_ou_prontidao()
    {
        var c = Conjunto();
        var a = c.Artefatos[0] with { Situacao = "falhou", Hash = "", Tamanho = 0, Previews = [],
            Pacote = new(Hex('a'), Hex('b', 40), "170", Hex('f'), 1024, 501, Agora.AddDays(1)) };
        var retrato = Retrato(c with { Artefatos = [a] });
        Assert.Equal("", RegrasDoRemoto.Validar(retrato));
        Assert.Empty(RegrasDoRemoto.CamposSensiveis(retrato));
        var copia = JsonSerializer.Deserialize<RetratoV1>(JsonSerializer.Serialize(retrato, Json), Json)!.Projetos![0].ArtefatosLocais!.Artefatos[0];
        Assert.Equal(a.Pacote, copia.Pacote);
        Assert.Equal(a.ConjuntoHash, copia.ConjuntoHash);
        Assert.Null(copia.Publicacao);
        Assert.Equal("falhou", copia.Situacao);
        Assert.Empty(copia.Previews);
        a = a with { Situacao = "pronto", Publicacao = new(502, "preview", Hex('a'), Hex('b', 40), "", 0) };
        Assert.Equal("", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a] })));
        a = a with { Publicacao = new(501, "pacote", Hex('a'), Hex('b', 40), Hex('f'), 1024), Hash = Hex('f'), Tamanho = 1024 };
        Assert.Equal("", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a] })));
        Assert.Contains("pacote conferido", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Pacote = a.Pacote! with { IdRemoto = 503 } }] })));
    }

    [Theory]
    [InlineData("versao")]
    [InlineData("hash")]
    [InlineData("hash-outro")]
    [InlineData("sha")]
    [InlineData("repo")]
    [InlineData("id")]
    [InlineData("tamanho")]
    [InlineData("prazo-local")]
    public void Pacote_invalido_nao_inventa_correlacao_com_publicacao(string erro)
    {
        var c = Conjunto();
        var pacote = new PacoteNoRetratoV1(Hex('a'), Hex('b', 40), "170", Hex('f'), 1024, 501, Agora.AddDays(1));
        pacote = erro switch
        {
            "versao" => pacote with { Versao = "" }, "hash" => pacote with { Hash = "invalido" },
            "hash-outro" => pacote with { Hash = Hex('e') },
            "sha" => pacote with { Sha = Hex('c', 40) }, "repo" => pacote with { Repositorio = Hex('d') },
            "id" => pacote with { IdRemoto = -1 }, "tamanho" => pacote with { Tamanho = 0 },
            _ => pacote with { IdRemoto = 0 },
        };
        Assert.Contains("pacote conferido", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [c.Artefatos[0] with { Pacote = pacote }] })));
    }

    [Fact]
    public void Publicacao_incompativel_e_segredo_na_versao_nao_entram_pelo_campo_novo()
    {
        var c = Conjunto();
        var a = c.Artefatos[0];
        var pub = new PublicacaoNoRetratoV1(501, "pacote", Hex('a'), Hex('b', 40), Hex('f'), 1024);
        Assert.Equal("", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Publicacao = pub }] })));
        Assert.Contains("publicação", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Publicacao = pub with { Hash = Hex('e') } }] })));
        const string token = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        var retrato = Retrato(c with { Artefatos = [a with { Pacote = new(Hex('a'), Hex('b', 40), token, Hex('f'), 1024, 501, null) }] });
        Assert.Equal("", RegrasDoRemoto.Validar(retrato));
        Assert.Contains(RegrasDoRemoto.CamposSensiveis(retrato), x => x.Campo.EndsWith("pacote.versao", StringComparison.Ordinal));
    }
    private static ArtefatosLocaisNoRetratoV1 Preview(Func<PreviewLocalNoRetratoV1, PreviewLocalNoRetratoV1> alterar)
    {
        var conjunto = Conjunto();
        var a = conjunto.Artefatos[0];
        return conjunto with { Artefatos = [a with { Previews = [alterar(a.Previews[0])] }] };
    }

    [Fact]
    public void Extensao_opcional_preserva_json_antigo_e_payload_de_aprovacoes()
    {
        var antigo = Retrato(null);
        var bytes = JsonSerializer.Serialize(antigo, Json);
        Assert.DoesNotContain("artefatosLocais", bytes, StringComparison.Ordinal);
        Assert.Null(JsonSerializer.Deserialize<RetratoV1>(bytes, Json)!.Projetos![0].ArtefatosLocais);
        Assert.Equal("", RegrasDoRemoto.Validar(antigo));
        Assert.Empty(RegrasDoRemoto.CamposSensiveis(antigo));
        var novo = JsonSerializer.Deserialize<RetratoV1>(JsonSerializer.Serialize(Retrato(Conjunto()), Json), Json)!;
        Assert.Equal("", RegrasDoRemoto.Validar(novo));
        Assert.Empty(RegrasDoRemoto.CamposSensiveis(novo));
        Assert.Equal("http://127.0.0.1:4317/", novo.Projetos![0].ArtefatosLocais!.Artefatos[0].Previews[0].Url);
    }

    [Theory]
    [InlineData("https://127.0.0.1:4317/")]
    [InlineData("http://localhost:4317/")]
    [InlineData("http://192.168.0.1:4317/")]
    [InlineData("https://portal.azurestaticapps.net/")]
    [InlineData("http://exemplo.test/")]
    [InlineData("http://nome:segredo@127.0.0.1:4317/")]
    [InlineData("http://127.0.0.1:4317/?token=segredo")]
    [InlineData("http://127.0.0.1:4317/#segredo")]
    [InlineData("file:///C:/dados/index.html")]
    public void Url_local_recusa_destino_externo_segredos_e_swa_sem_relaxar_publicacao(string url)
        => Assert.Contains("URL pronta", RegrasDoRemoto.Validar(Retrato(Preview(p => p with { Url = url }))));

    [Fact]
    public void Prontidao_exige_api_jornada_e_maquina_e_estado_nao_pronto_retira_url()
    {
        Assert.Contains("URL pronta", RegrasDoRemoto.Validar(Retrato(Preview(p => p with { Prontidao = p.Prontidao with { Api = false } }))));
        Assert.Contains("URL pronta", RegrasDoRemoto.Validar(Retrato(Preview(p => p with { Prontidao = p.Prontidao with { Jornada = false } }))));
        Assert.Contains("URL pronta", RegrasDoRemoto.Validar(Retrato(Preview(p => p with { NestaMaquina = false }))));
        Assert.Contains("URL pronta", RegrasDoRemoto.Validar(Retrato(Preview(p => p with { Componentes = [p.Componentes[0]] }))));
        Assert.Contains("não pronto", RegrasDoRemoto.Validar(Retrato(Preview(p => p with { Situacao = "falhou" }))));
        Assert.Equal("", RegrasDoRemoto.Validar(Retrato(Preview(p => p with { Situacao = "falhou", Url = null,
            Prontidao = new(false, false, false, false), Diagnostico = "API indisponível." }))));
    }

    [Fact]
    public void Snapshot_e_referencias_nao_podem_ser_parciais_inventados_ou_ambiguos()
    {
        var c = Conjunto();
        var a = c.Artefatos[0];
        Assert.Contains("conjunto completo", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { ConjuntoHash = null }] })));
        Assert.Contains("fontes únicas", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Fontes = [a.Fontes[0], a.Fontes[0]] }] })));
        Assert.Contains("alvo não pertence", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Alvos = [a.Alvos[0] with { Repositorio = Hex('b') }] }] })));
        Assert.Contains("fontes únicas", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Fontes = [a.Fontes[0] with { Sha = "abc1234" }] }] })));
        Assert.Contains("aplicações únicas", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Previews = [a.Previews[0], a.Previews[0]] }] })));
        Assert.Contains("listas nulas", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Fontes = null! }] })));
        Assert.Contains("hash", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Hash = null! }] })));
    }

    [Fact]
    public void Timeout_preserva_correlacao_e_log_parcial_sem_virar_prontidao_ou_inferir_causa_pelo_exit()
    {
        var c = Conjunto();
        var a = c.Artefatos[0];
        var timeout = a with { Situacao = "falhou", Diagnostico = "Comando confirmou timeout.", Previews = [],
            Comando = a.Comando! with { Exit = 124, MotivoTermino = "timeout", LogParcial = "Publicação parcial." } };
        var retrato = Retrato(c with { Artefatos = [timeout] });
        Assert.Equal("", RegrasDoRemoto.Validar(retrato));
        var recebido = JsonSerializer.Deserialize<RetratoV1>(JsonSerializer.Serialize(retrato, Json), Json)!.Projetos![0].ArtefatosLocais!.Artefatos[0];
        Assert.Equal("falhou", recebido.Situacao);
        Assert.Equal(timeout.Comando, recebido.Comando);
        Assert.Equal(timeout.Fontes[0], recebido.Fontes[0]);
        Assert.Empty(recebido.Previews);
        Assert.Equal("", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [timeout with { Comando = timeout.Comando! with { MotivoTermino = null } }] })));
    }

    [Theory]
    [InlineData("comando")]
    [InlineData("componente")]
    [InlineData("abertura")]
    [InlineData("branch")]
    public void Campos_novos_passam_pela_mesma_deteccao_de_dado_sensivel(string campo)
    {
        var c = Conjunto();
        var a = c.Artefatos[0];
        var token = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        a = campo switch
        {
            "comando" => a with { Comando = a.Comando! with { LogParcial = token } },
            "componente" => a with { Previews = [a.Previews[0] with { Componentes = [a.Previews[0].Componentes[0] with { LogParcial = token }, a.Previews[0].Componentes[1]] }] },
            "abertura" => a with { Previews = [a.Previews[0] with { Abertura = new(Hex('b', 32), "falhou", token, Agora) }] },
            _ => a with { Fontes = [a.Fontes[0] with { Branch = token }] },
        };
        var retrato = Retrato(c with { Artefatos = [a] });
        Assert.Equal("", RegrasDoRemoto.Validar(retrato));
        var recusa = Assert.Single(RegrasDoRemoto.CamposSensiveis(retrato));
        Assert.Equal(RemotoV1.SensivelSegredo, recusa.Tipo);
        Assert.Contains("artefatosLocais", recusa.Campo);
        Assert.DoesNotContain(token, recusa.Campo);
    }
}
