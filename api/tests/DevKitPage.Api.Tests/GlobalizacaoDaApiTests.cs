using System.Text.RegularExpressions;

namespace DevKitPage.Api.Tests;

/// <summary>
/// Arquitetura: a API roda com <c>InvariantGlobalization</c> (o <c>DevKitPage.Api.csproj</c>), e nela só existe a cultura
/// invariante — criar cultura com nome é <see cref="System.Globalization.CultureNotFoundException"/> em produção (o painel
/// Uso caiu assim em 27/10). A API formata com <c>CultureInfo.InvariantCulture</c> ou num formato explícito e devolve os
/// dados crus para a web formatar em pt-BR; este teste falha se alguém criar cultura com nome em <c>api/src</c>. Falha
/// também com a decomposição FormD/FormKD (o "tirar acento" pelo <c>Normalize</c>): no modo invariante ela não decompõe,
/// e a letra acentuada some do slug sem erro nenhum.
/// </summary>
public sealed partial class GlobalizacaoDaApiTests
{
    /// <summary>As formas de criar cultura (ou região) com nome, e a decomposição que depende do ICU.</summary>
    [GeneratedRegex(@"new\s+(System\.Globalization\.)?(CultureInfo|RegionInfo)\s*\(|CultureInfo\s*\.\s*(GetCultureInfo|GetCultureInfoByIetfLanguageTag|CreateSpecificCulture)\s*\(|\.Normalize\s*\(\s*(System\.Text\.)?NormalizationForm\.FormK?D\b")]
    private static partial Regex CulturaComNome();

    /// <summary>A pasta <c>api/src</c>, subindo da pasta dos binários do teste.</summary>
    private static string FontesDaApi()
    {
        for (var pasta = new DirectoryInfo(AppContext.BaseDirectory); pasta is not null; pasta = pasta.Parent)
        {
            if (File.Exists(Path.Combine(pasta.FullName, "DevKitPage.slnx")))
                return Path.Combine(pasta.FullName, "src");
        }
        throw new DirectoryNotFoundException("A raiz da API (DevKitPage.slnx) não foi achada acima dos binários do teste.");
    }

    /// <summary>A linha sem o comentário de fim de linha (a documentação pode citar o que é proibido).</summary>
    private static string SemComentario(string linha) => linha.Split("//", 2)[0];

    [Theory]
    [InlineData("var c = new CultureInfo(\"pt-BR\");")]
    [InlineData("x.ToString(\"0.0\", System.Globalization.CultureInfo.GetCultureInfo(\"pt-BR\"));")]
    [InlineData("var c = CultureInfo.CreateSpecificCulture(\"pt-BR\");")]
    [InlineData("var r = new System.Globalization.RegionInfo(\"BR\");")]
    [InlineData("var n = nome.ToLowerInvariant().Normalize(NormalizationForm.FormD);")]
    public void O_padrao_reconhece_a_cultura_com_nome(string codigo) => Assert.Matches(CulturaComNome(), SemComentario(codigo));

    [Fact]
    public void Nenhum_fonte_da_api_cria_cultura_com_nome()
    {
        var fontes = Directory.EnumerateFiles(FontesDaApi(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        var violacoes = fontes
            .SelectMany(f => File.ReadLines(f).Select((linha, i) => (Arquivo: f, Linha: i + 1, Codigo: SemComentario(linha))))
            .Where(l => CulturaComNome().IsMatch(l.Codigo))
            .Select(l => $"{Path.GetRelativePath(FontesDaApi(), l.Arquivo)}:{l.Linha}: {l.Codigo.Trim()}")
            .ToList();

        Assert.NotEmpty(fontes);
        Assert.True(violacoes.Count == 0,
            "A API roda com InvariantGlobalization: use CultureInfo.InvariantCulture ou um formato explícito, deixe a cultura para a web"
            + " e tire acento por tabela explícita (FormatosDoCatalogo.Slug), não pelo Normalize(FormD)."
            + Environment.NewLine + string.Join(Environment.NewLine, violacoes));
    }
}
