namespace DevKitPage.Core.Tests;

/// <summary>
/// O CONTRATO do manifesto (US #461, a ressalva da task #463): o conjunto canônico de <c>ContratoDoManifesto/</c> — o
/// manifesto anterior, os itens de agora e o manifesto esperado — é o MESMO que o git.kit versiona em
/// <c>tests/GitKit.Core.Tests/Catalogo/ContratoDoManifesto/</c>, e lá o <c>GeradorDoManifestoTests</c> exige o mesmo
/// <c>esperado.json</c>. A paridade com o v2 congelado não pegaria uma divergência futura; este pega: mudar o gerador de
/// um lado quebra o teste até o fixture ser regenerado e copiado para o outro.
/// </summary>
public sealed class ContratoDoManifestoTests
{
    private static string Pasta()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var candidata = Path.Combine(pasta.FullName, "ContratoDoManifesto");
            if (File.Exists(Path.Combine(candidata, "esperado.json")))
                return candidata;
        }

        throw new DirectoryNotFoundException("ContratoDoManifesto/ não encontrado.");
    }

    [Fact]
    public void O_porte_do_gerador_produz_o_esperado_dos_fixtures_compartilhados_com_o_git_kit()
    {
        var raiz = Pasta();
        var anterior = ManifestoJson.Ler(File.ReadAllText(Path.Combine(raiz, "anterior.json")));

        var gerado = GeradorDoManifesto.Gerar(anterior, Fixtures.Ler(Path.Combine(raiz, "base")), 160);

        Assert.True(gerado.Mudou);
        Assert.Equal(Fixtures.Lf(File.ReadAllText(Path.Combine(raiz, "esperado.json"))), ManifestoJson.Serializar(gerado.Manifesto));
    }
}
