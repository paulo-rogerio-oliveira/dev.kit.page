using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

public sealed partial class ArtefatosLocaisNoRetratoTests
{
    private static ArtefatosLocaisNoRetratoV1 ComDetalhes()
    {
        var c = Conjunto();
        var a = c.Artefatos[0];
        var fontes = a.Fontes.Append(a.Fontes[0] with { Id = Hex('c'), Sha = Hex('d', 40), Nome = "catalogo" }).ToArray();
        return c with { Artefatos = [a with { Fontes = fontes,
            Alvos = [a.Alvos[0] with { Saida = "produtos/site/api", Assembly = "Api" }],
            Detalhes = new("conteudo-identico", [new(Hex('a'), "catalogo", Hex('c'), Hex('e', 40), Hex('d', 40),
                Hex('f', 40), Hex('f', 40), "catalogo/catalogo", "conteudo-identico")],
                [new(Hex('a'), "tools/Aux.csproj", "excluído explicitamente")], new(Hex('a'), "", new("", "", "", ""))) }] };
    }

    [Fact]
    public void Detalhes_preservam_roundtrip_de_pins_exclusoes_opcoes_e_caminhos_longos_sem_autorizar_deploy()
    {
        var c = ComDetalhes();
        var a = c.Artefatos[0];
        var caminho = "src/" + new string('x', 240) + "/Api.csproj";
        c = c with { Artefatos = [a with { Alvos = [a.Alvos[0] with { Nome = caminho }] }] };
        var retrato = Retrato(c);
        Assert.Equal("", RegrasDoRemoto.Validar(retrato));
        Assert.Empty(RegrasDoRemoto.CamposSensiveis(retrato));
        var json = JsonSerializer.Serialize(retrato, Json);
        var copia = JsonSerializer.Deserialize<RetratoV1>(json, Json)!;
        Assert.Equal(json, JsonSerializer.Serialize(copia, Json));
        Assert.Equal(caminho, copia.Projetos![0].ArtefatosLocais!.Artefatos[0].Alvos[0].Nome);
        Assert.False(c.OperacoesDisponiveis);
    }

    [Theory]
    [InlineData("origem-nula")]
    [InlineData("capturado")]
    [InlineData("arvore")]
    [InlineData("raiz")]
    [InlineData("opcao-nula")]
    [InlineData("destino-longo")]
    [InlineData("formato")]
    [InlineData("exclusao")]
    [InlineData("saida")]
    [InlineData("saida-absoluta")]
    [InlineData("saida-duplicada")]
    public void Detalhes_incoerentes_ou_nulos_retornam_recusa_concreta(string erro)
    {
        var c = ComDetalhes();
        var a = c.Artefatos[0];
        var d = a.Detalhes!;
        d = erro switch
        {
            "origem-nula" => d with { Pins = [d.Pins[0] with { Consumidor = null! }] },
            "capturado" => d with { Pins = [d.Pins[0] with { Capturado = Hex('e', 40) }] },
            "arvore" => d with { Pins = [d.Pins[0] with { ArvoreCapturada = Hex('e', 40) }] },
            "raiz" => d with { Pins = [d.Pins[0] with { Raiz = "../fora" }] },
            "opcao-nula" => d with { Opcoes = d.Opcoes! with { RepositorioPrincipal = null! } },
            "destino-longo" => d with { Opcoes = d.Opcoes! with { PreviewRemoto = new(new string('x', 257), "", "", "") } },
            "formato" => d with { Opcoes = d.Opcoes! with { FormatoSolicitado = "production" } },
            "exclusao" => d with { Exclusoes = [d.Exclusoes[0] with { Caminho = a.Alvos[0].Nome }] },
            _ => d,
        };
        if (erro == "saida") a = a with { Alvos = [a.Alvos[0] with { Saida = null }] };
        if (erro == "saida-absoluta") a = a with { Alvos = [a.Alvos[0] with { Saida = "C:/privado" }] };
        if (erro == "saida-duplicada") a = a with { Alvos = [a.Alvos[0], a.Alvos[0] with { Nome = "Cli.csproj" }] };
        Assert.NotEmpty(RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Detalhes = d }] })));
    }

    [Fact]
    public void Pin_divergente_exige_politica_explicita_e_rastreavel_no_conjunto()
    {
        var c = ComDetalhes();
        var a = c.Artefatos[0];
        var d = a.Detalhes!;
        d = d with { PoliticaDePins = "declarada-por-consumidor", Pins = [d.Pins[0] with {
            ArvoreCapturada = Hex('e', 40), Politica = "sha-do-projeto" }] };
        Assert.Equal("", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Detalhes = d }] })));
        Assert.Contains("contradiz", RegrasDoRemoto.Validar(Retrato(c with { Artefatos = [a with { Detalhes = d with { PoliticaDePins = "conteudo-identico" } }] })));
    }

    [Theory]
    [InlineData("saida")]
    [InlineData("pin")]
    [InlineData("exclusao")]
    [InlineData("destino")]
    public void Novos_campos_participam_da_inspecao_de_segredos(string campo)
    {
        const string token = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
        var c = ComDetalhes();
        var a = c.Artefatos[0];
        var d = a.Detalhes!;
        if (campo == "saida") a = a with { Alvos = [a.Alvos[0] with { Saida = "produtos/" + token }] };
        if (campo == "pin") d = d with { Pins = [d.Pins[0] with { Papel = token }] };
        if (campo == "exclusao") d = d with { Exclusoes = [d.Exclusoes[0] with { Motivo = token }] };
        if (campo == "destino") d = d with { Opcoes = d.Opcoes! with { PreviewRemoto = new(token, "", "", "") } };
        var retrato = Retrato(c with { Artefatos = [a with { Detalhes = d }] });
        Assert.Equal("", RegrasDoRemoto.Validar(retrato));
        Assert.NotEmpty(RegrasDoRemoto.CamposSensiveis(retrato));
    }
}
