using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// Os formatos do dev.kit portados para o site (US #461): o editor grava o MESMO JSON que o app (o <c>SkillJson</c>, o
/// <c>TechPlanJson</c>, o envelope <c>devkit-fluxo</c>) — os arquivos reais do <c>catalogo/base</c> do git.kit, lidos e
/// regravados aqui, voltam com o mesmo texto —, e a leitura recusa com as mensagens do app.
/// </summary>
public sealed class FormatosDoCatalogoTests
{
    public static TheoryData<string, string> ArquivosDoCatalogo()
    {
        var dados = new TheoryData<string, string>();
        var raiz = Path.Combine(Fixtures.Pasta("catalogo-v2"), "base");
        foreach (var tipo in TiposDeItem.Todos)
            foreach (var arquivo in Directory.EnumerateFiles(Path.Combine(raiz, FormatosDoCatalogo.Pasta(tipo)), "*.json"))
                dados.Add(tipo, Path.GetFileName(arquivo));
        return dados;
    }

    /// <summary>
    /// O arquivo do git.kit (escrito à mão ou por versões antigas do app: CRLF no conteúdo, plan sem <c>key</c>) volta
    /// no formato CANÔNICO do export do app, sem mudar o item: o hash do regravado é o do manifesto v2, e regravar de novo
    /// não muda mais nada.
    /// </summary>
    [Theory]
    [MemberData(nameof(ArquivosDoCatalogo))]
    public void O_arquivo_do_git_kit_regravado_pelo_site_e_o_mesmo_item(string tipo, string arquivo)
    {
        var raiz = Fixtures.Pasta("catalogo-v2");
        var manifesto = ManifestoJson.Ler(File.ReadAllText(Path.Combine(raiz, "manifesto.json")));
        var texto = File.ReadAllText(Path.Combine(raiz, "base", FormatosDoCatalogo.Pasta(tipo), arquivo));

        var (item, erro) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(tipo, texto));
        var (deNovo, _) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(tipo, item?.Conteudo ?? string.Empty));

        Assert.True(item is not null, erro);
        var nome = tipo == TiposDeItem.Fluxo ? Path.GetFileNameWithoutExtension(arquivo) : item.Nome;
        Assert.Equal(manifesto.Itens.Single(i => i.Tipo == tipo && i.Nome == nome).Hash, item.Hash);
        Assert.Equal(item.Conteudo, deNovo!.Conteudo);
        Assert.Equal(item.Hash, deNovo.Hash);
    }

    [Fact]
    public void A_skill_sai_com_os_campos_na_ordem_do_SkillJson_e_a_leitura_e_tolerante()
    {
        var lida = FormatosDoCatalogo.LerSkill("""{ "name": " minha-skill ", "content": "passo\r\n", "arguments": "a\nb\nA", "isPinned": "true", }""");

        var texto = FormatosDoCatalogo.ExportarSkill(lida);

        Assert.Equal(("minha-skill", "passo", true), (lida.Name, lida.Content, lida.IsPinned));
        Assert.Equal(new[] { "a", "b" }, lida.Arguments);
        var ordem = new[] { "\"name\"", "\"description\"", "\"content\"", "\"arguments\"", "\"repositories\"", "\"isCommand\"", "\"isBackground\"", "\"isPinned\"" }
            .Select(c => texto.IndexOf(c, StringComparison.Ordinal)).ToArray();
        Assert.Equal(ordem.Order(), ordem);
    }

    [Theory]
    [InlineData(TiposDeItem.Skill, """{ "description": "x", "content": "y" }""", "A skill está sem nome (campo \"name\").")]
    [InlineData(TiposDeItem.Skill, """{ "name": "x", "content": "  " }""", "A skill \"x\" veio sem conteúdo (campo \"content\").")]
    [InlineData(TiposDeItem.Plan, """{ "content": "y" }""", "O Tech Plan está sem descrição (campo \"description\").")]
    [InlineData(TiposDeItem.Fluxo, """{ "nome": "x" }""", "O arquivo não é um fluxo exportado pelo dev.kit (falta \"formato\": \"devkit-fluxo\").")]
    [InlineData(TiposDeItem.Fluxo, """{ "formato": "devkit-fluxo", "versao": 2, "nome": "x" }""", "Versão 2 do arquivo de fluxo não é conhecida por este dev.kit (ele lê a 1): atualize o dev.kit ou exporte de novo.")]
    [InlineData(TiposDeItem.Fluxo, """{ "formato": "devkit-fluxo", "versao": 1, "nome": "Entrega" }""", "O fluxo \"Entrega\" está sem o desenho (campo \"desenho\").")]
    public void A_leitura_recusa_com_a_mensagem_do_app(string tipo, string conteudo, string mensagem)
        => Assert.Equal(mensagem, RegrasDosCatalogos.Conferir(new ItemEditadoV1(tipo, conteudo)).Erro);

    [Fact]
    public void A_chave_do_plan_sem_key_e_o_slug_da_descricao_e_o_hash_ignora_crlf()
    {
        var crlf = FormatosDoCatalogo.LerPlan("{ \"description\": \"Build mínimo — rápido\", \"content\": \"linha 1\\r\\nlinha 2\" }");
        var lf = FormatosDoCatalogo.LerPlan("{ \"key\": \"build-minimo-rapido\", \"description\": \"Build mínimo — rápido\", \"content\": \"linha 1\\nlinha 2\" }");

        Assert.Equal("build-minimo-rapido", FormatosDoCatalogo.Chave(crlf));
        Assert.Equal(FormatosDoCatalogo.HashDoPlan(lf), FormatosDoCatalogo.HashDoPlan(crlf));
        Assert.Equal("fluxo", FormatosDoCatalogo.Slug("  —  ", "fluxo"));
        Assert.Equal("implementacao-de-work-item", FormatosDoCatalogo.Slug("Implementação de work_item", "fluxo"));
    }
}
