using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// A edição de artefato do catálogo (US #687), as regras puras: a Rule com a stack e o tipo (o campo que o site perdia ao
/// regravar, sem mudar o hash das Rules comuns), o escopo base/empresa/projeto, a Rule de impacto que nunca vai para o
/// básico, o texto do editor (o SKILL.md) e as diferenças linha a linha.
/// </summary>
public sealed class EdicaoDeArtefatoTests
{
    private const string RuleComum = """{"key":"build-minimo","description":"Build mínimo","content":"Compile só o que mudou.","repository":"","onlyWithWorkItem":false,"isRequired":true,"isAgentInstruction":false}""";

    private const string RuleDeImpacto = """{"key":"licenca","description":"Licença","content":"GATILHO: licença","repository":"","onlyWithWorkItem":false,"isRequired":false,"isAgentInstruction":false,"stack":"DotNet; VB6","tipo":"Impacto"}""";

    [Fact]
    public void A_rule_comum_sai_byte_a_byte_como_antes_e_a_de_impacto_leva_stack_e_tipo()
    {
        var comum = FormatosDoCatalogo.LerPlan(RuleComum);
        var impacto = FormatosDoCatalogo.LerPlan(RuleDeImpacto);

        Assert.DoesNotContain("stack", FormatosDoCatalogo.ExportarPlan(comum));
        Assert.DoesNotContain("tipo", FormatosDoCatalogo.ExportarPlan(comum));
        Assert.DoesNotContain("ehImpacto", FormatosDoCatalogo.ExportarPlan(impacto));
        Assert.Equal(("dotnet, vb6", "impacto", true), (impacto.Stack, impacto.Tipo, impacto.EhImpacto));
        Assert.Contains("\"stack\": \"dotnet, vb6\"", FormatosDoCatalogo.ExportarPlan(impacto));
        Assert.Contains("\"tipo\": \"impacto\"", FormatosDoCatalogo.ExportarPlan(impacto));
        Assert.NotEqual(FormatosDoCatalogo.HashDoPlan(comum), FormatosDoCatalogo.HashDoPlan(comum with { Stack = "vb6" }));
    }

    [Fact]
    public void O_escopo_do_item()
    {
        var presa = RuleComum.Replace("\"repository\":\"\"", "\"repository\":\"gbl-2000\"", StringComparison.Ordinal);
        const string skill = """{"name":"testar-dotnet","description":"Testes","content":"dotnet test","repositories":["dev.kit.page"]}""";

        Assert.Equal(EscoposDoItem.Base, RegrasDosCatalogos.Escopo(basico: true, TiposDeItem.Plan, presa));
        Assert.Equal(EscoposDoItem.Empresa, RegrasDosCatalogos.Escopo(basico: false, TiposDeItem.Plan, RuleComum));
        Assert.Equal(EscoposDoItem.Projeto, RegrasDosCatalogos.Escopo(basico: false, TiposDeItem.Plan, presa));
        Assert.Equal(EscoposDoItem.Projeto, RegrasDosCatalogos.Escopo(basico: false, TiposDeItem.Skill, skill));
        Assert.Equal(EscoposDoItem.Empresa, RegrasDosCatalogos.Escopo(basico: false, TiposDeItem.Fluxo, "{}"));
    }

    [Fact]
    public void A_rule_de_impacto_e_a_stack_saem_do_conteudo()
    {
        Assert.True(RegrasDosCatalogos.RuleDeImpacto(TiposDeItem.Plan, RuleDeImpacto));
        Assert.False(RegrasDosCatalogos.RuleDeImpacto(TiposDeItem.Plan, RuleComum));
        Assert.False(RegrasDosCatalogos.RuleDeImpacto(TiposDeItem.Skill, RuleDeImpacto));
        Assert.False(RegrasDosCatalogos.RuleDeImpacto(TiposDeItem.Plan, "não abre"));
        Assert.Equal("dotnet, vb6", RegrasDosCatalogos.Stack(TiposDeItem.Plan, RuleDeImpacto));
        Assert.Equal(string.Empty, RegrasDosCatalogos.Stack(TiposDeItem.Skill, RuleDeImpacto));
    }

    [Fact]
    public void O_texto_do_editor_e_o_skill_md()
    {
        const string skill = """{"name":"compilar-net-framework","description":"Compilar .NET Framework (MSBuild)","content":"# Compilar\n\n1. Ache o MSBuild."}""";

        Assert.Equal("---\nname: compilar-net-framework\ndescription: Compilar .NET Framework (MSBuild)\n---\n\n# Compilar\n\n1. Ache o MSBuild.",
            RegrasDosCatalogos.TextoEditavel(TiposDeItem.Skill, skill));
        Assert.Equal("Compile só o que mudou.", RegrasDosCatalogos.TextoEditavel(TiposDeItem.Plan, RuleComum));
        Assert.Equal("{\"a\":1}", RegrasDosCatalogos.TextoEditavel(TiposDeItem.Fluxo, "{\"a\":1}"));
    }

    [Fact]
    public void As_diferencas_linha_a_linha()
    {
        var linhas = RegrasDosCatalogos.Diferencas("a\nb\nc", "a\nB\nc\nd");

        Assert.Equal(new[] { "igual:a", "adicionada:B", "removida:b", "igual:c", "adicionada:d" }, linhas.Select(l => $"{l.Tipo}:{l.Texto}"));
        Assert.Equal(new int?[] { 1, 2, null, 3, 4 }, linhas.Select(l => l.Numero));
        Assert.All(RegrasDosCatalogos.Diferencas(string.Empty, "x\ny"), l => Assert.Equal(TiposDeLinha.Adicionada, l.Tipo));
        Assert.Empty(RegrasDosCatalogos.Diferencas(string.Empty, string.Empty));
    }
}
