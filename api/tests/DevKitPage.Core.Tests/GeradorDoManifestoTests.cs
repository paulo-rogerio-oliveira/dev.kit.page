using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>Os fixtures versionados ao lado dos testes (<c>Fixtures/</c>), achados subindo da pasta do binário.</summary>
internal static class Fixtures
{
    public static string Pasta(string nome)
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            var candidata = Path.Combine(pasta.FullName, "Fixtures", nome);
            if (Directory.Exists(candidata))
                return candidata;
        }

        throw new DirectoryNotFoundException($"Fixtures/{nome} não encontrado.");
    }

    /// <summary>Os itens de uma pasta <c>base/</c> (skills, plans, fluxos), como o <c>GeradorDoManifesto.Ler</c> do git.kit lê.</summary>
    public static IReadOnlyList<GeradorDoManifesto.ItemLido> Ler(string pastaBase)
    {
        var itens = new List<GeradorDoManifesto.ItemLido>();
        foreach (var tipo in TiposDeItem.Todos)
        {
            var pasta = Path.Combine(pastaBase, FormatosDoCatalogo.Pasta(tipo));
            if (!Directory.Exists(pasta))
                continue;
            foreach (var arquivo in Directory.EnumerateFiles(pasta, "*.json").OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
                itens.Add(GeradorDoManifesto.Ler(tipo, Path.GetFileNameWithoutExtension(arquivo), $"{FormatosDoCatalogo.Pasta(tipo)}/{Path.GetFileName(arquivo)}", File.ReadAllText(arquivo)));
        }

        return itens.OrderBy(i => i.Tipo, StringComparer.Ordinal).ThenBy(i => i.Nome, StringComparer.Ordinal).ToArray();
    }

    public static string Lf(string texto) => texto.Replace("\r\n", "\n", StringComparison.Ordinal);
}

/// <summary>
/// O gerador do manifesto portado do git.kit (US #461): a PARIDADE com o catálogo básico real (o <c>catalogo/base</c> e o
/// <c>manifesto.json</c> v2 do git.kit, copiados em <c>Fixtures/catalogo-v2</c>) e as regras de versão — hash novo é
/// versão + 1, o que sai vai para os retirados, o que volta continua a numeração, nada mudou não publica.
/// </summary>
public sealed class GeradorDoManifestoTests
{
    [Fact]
    public void Gerar_sobre_o_catalogo_v2_do_git_kit_da_os_mesmos_hashes_e_nao_muda_nada()
    {
        var raiz = Fixtures.Pasta("catalogo-v2");
        var texto = File.ReadAllText(Path.Combine(raiz, "manifesto.json"));
        var versionado = ManifestoJson.Ler(texto);
        var lidos = Fixtures.Ler(Path.Combine(raiz, "base"));

        var resultado = GeradorDoManifesto.Gerar(versionado, lidos, versionado.MinDevKit);

        Assert.Equal(versionado.Itens.Count, lidos.Count);
        Assert.All(lidos, l => Assert.Equal(versionado.Itens.Single(i => i.Tipo == l.Tipo && i.Nome == l.Nome).Hash, l.Hash));
        Assert.False(resultado.Mudou, string.Join(Environment.NewLine, resultado.Relatorio));
        // O arquivo do manifesto também sai igual: o site grava o mesmo texto que o build/catalogo.ps1 gravava.
        Assert.Equal(Fixtures.Lf(texto), ManifestoJson.Serializar(versionado));
    }

    [Fact]
    public void A_dependencia_do_fluxo_e_as_ferramentas_saem_como_no_git_kit()
    {
        var raiz = Fixtures.Pasta("catalogo-v2");
        var versionado = ManifestoJson.Ler(File.ReadAllText(Path.Combine(raiz, "manifesto.json")));
        var lidos = Fixtures.Ler(Path.Combine(raiz, "base"));

        foreach (var lido in lidos)
        {
            var publicado = versionado.Itens.Single(i => i.Tipo == lido.Tipo && i.Nome == lido.Nome);
            Assert.Equal(publicado.Dependencias, lido.Dependencias);
            Assert.Equal(publicado.Requer, lido.Requer);
            Assert.Equal(publicado.Descricao, lido.Descricao);
            Assert.Equal(publicado.Tecnologia, GeradorDoManifesto.Tecnologia(lido.Tipo, lido.Nome));
        }
    }

    private static GeradorDoManifesto.ItemLido Skill(string nome, string conteudo)
        => GeradorDoManifesto.Ler(TiposDeItem.Skill, nome, $"skills/{nome}.json",
            FormatosDoCatalogo.ExportarSkill(new SkillDoCatalogo(nome, $"Sobre {nome}", conteudo, [], [], false, false, false)));

    [Fact]
    public void Hash_novo_e_versao_mais_um_o_que_sumiu_e_retirado_e_o_que_volta_continua_a_numeracao()
    {
        var v1 = GeradorDoManifesto.Gerar(GeradorDoManifesto.Vazio, [Skill("a-skill", "passo 1"), Skill("b-skill", "b")], 1).Manifesto;
        var hashAntigo = v1.Itens.Single(i => i.Nome == "a-skill").Hash;

        var v2 = GeradorDoManifesto.Gerar(v1, [Skill("a-skill", "passo 1 corrigido")], 2).Manifesto;
        var v3 = GeradorDoManifesto.Gerar(v2, [Skill("a-skill", "passo 1 corrigido"), Skill("b-skill", "b")], 2).Manifesto;

        var a = v2.Itens.Single();
        Assert.Equal((2, 2, 2, 2), (v2.Versao, a.Versao, a.Catalogo, a.MinDevKit));
        Assert.Equal(new[] { hashAntigo, a.Hash }, a.Hashes);
        Assert.Equal(new ItemRetiradoV1(TiposDeItem.Skill, "b-skill", 1, 2), Assert.Single(v2.Retirados));
        Assert.Empty(v3.Retirados);
        Assert.Equal((2, 3), (v3.Itens.Single(i => i.Nome == "b-skill").Versao, v3.Itens.Single(i => i.Nome == "b-skill").Catalogo));
        Assert.Equal(2, v3.Itens.Single(i => i.Nome == "a-skill").Catalogo); // fora da delta de quem já tem a 2
    }

    [Fact]
    public void Sem_mudanca_o_manifesto_volta_o_mesmo_e_a_categoria_informada_vira_dado_da_vitrine()
    {
        var v1 = GeradorDoManifesto.Gerar(GeradorDoManifesto.Vazio, [Skill("revisar-alteracoes", "use o {DEVCLI} e o {MSBUILD}")], 1).Manifesto;

        var igual = GeradorDoManifesto.Gerar(v1, [Skill("revisar-alteracoes", "use o {DEVCLI} e o {MSBUILD}")], 9);
        var categoria = GeradorDoManifesto.Gerar(v1, [Skill("revisar-alteracoes", "use o {DEVCLI} e o {MSBUILD}") with { Tecnologia = "Qualidade" }], 9);

        Assert.False(igual.Mudou);
        Assert.Same(v1, igual.Manifesto);
        Assert.Equal(new[] { "devcli", "msbuild" }, v1.Itens.Single().Requer);
        Assert.Equal("Git", v1.Itens.Single().Tecnologia);
        Assert.True(categoria.Mudou);
        Assert.Equal(("Qualidade", 1, 2), (categoria.Manifesto.Itens.Single().Tecnologia, categoria.Manifesto.Itens.Single().Versao, categoria.Manifesto.Itens.Single().Catalogo));
        Assert.Contains("· skill:revisar-alteracoes (dados da vitrine)", categoria.Relatorio);
    }

    [Theory]
    [InlineData(TiposDeItem.Skill, "compilar-angular", "Angular")]
    [InlineData(TiposDeItem.Skill, "testar-dotnet", ".NET")]
    [InlineData(TiposDeItem.Skill, "commitar-e-enviar", "Git")]
    [InlineData(TiposDeItem.Skill, "criar-user-story", "Azure DevOps")]
    [InlineData(TiposDeItem.Skill, "controlar-devkit", "dev.kit")]
    [InlineData(TiposDeItem.Plan, "geral", "Tech Plans")]
    [InlineData(TiposDeItem.Fluxo, "entrega", "Fluxos")]
    public void A_tecnologia_da_vitrine_sai_do_nome(string tipo, string nome, string esperada)
        => Assert.Equal(esperada, GeradorDoManifesto.Tecnologia(tipo, nome));
}
