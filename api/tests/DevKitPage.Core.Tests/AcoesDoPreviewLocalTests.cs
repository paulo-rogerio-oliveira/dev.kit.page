using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

public sealed class AcoesDoPreviewLocalTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static AcaoNoProjetoRemotoV1 Pedido(string operacao = "iniciar", long? versao = 0)
        => new(ArtefatosV1.AcaoPreviewLocal, "demo", Artefato: 7, Receita: new string('a', 64),
            Aplicacao: "site", Operacao: operacao, Versao: versao, Conjunto: new string('b', 64));
    private static string Validar(AcaoNoProjetoRemotoV1 pedido)
        => RegrasDaCriacaoRemota.Validar(ConversaV1.ComandoProjeto, JsonSerializer.Serialize(pedido, Json));

    [Theory]
    [InlineData("iniciar")]
    [InlineData("consultar")]
    [InlineData("abrir")]
    [InlineData("parar")]
    public void Mesmo_formulario_percorre_validacao_da_fila_sem_converter_preview_em_geracao(string operacao)
    {
        var pedido = Pedido(operacao);
        Assert.Equal("", Validar(pedido));
        var recebido = JsonSerializer.Deserialize<AcaoNoProjetoRemotoV1>(JsonSerializer.Serialize(pedido, Json), Json);
        Assert.Equal(pedido, recebido);
        Assert.Null(recebido!.Repositorio);
        Assert.Null(recebido.Sha);
        Assert.Contains(RemotoV1.RecursoOperacoesDePreview, RemotoV1.RecursosDaApi);
    }

    [Fact]
    public void Consulta_dispensa_versao_mas_escritas_exigem_versao_consultada()
    {
        Assert.Equal("", Validar(Pedido("consultar", null)));
        foreach (var operacao in new[] { "iniciar", "abrir", "parar" })
            Assert.Contains("versão consultada", Validar(Pedido(operacao, null)));
        Assert.Contains("versão consultada", Validar(Pedido("consultar", -1)));
    }

    [Theory]
    [InlineData("projeto")]
    [InlineData("artefato")]
    [InlineData("receita-url")]
    [InlineData("receita-ausente")]
    [InlineData("snapshot-curto")]
    [InlineData("snapshot-ausente")]
    [InlineData("aplicacao-caminho")]
    [InlineData("aplicacao-maiuscula")]
    [InlineData("aplicacao-vazia")]
    [InlineData("operacao")]
    [InlineData("repositorio")]
    [InlineData("sha")]
    public void Alvo_incompleto_ou_contexto_misturado_e_recusado_antes_da_fila(string campo)
    {
        var pedido = Pedido();
        pedido = campo switch
        {
            "projeto" => pedido with { Projeto = "../outro" },
            "artefato" => pedido with { Artefato = 0 },
            "receita-url" => pedido with { Receita = "https://github.com/demo/site" },
            "receita-ausente" => pedido with { Receita = null },
            "snapshot-curto" => pedido with { Conjunto = "abcdef" },
            "snapshot-ausente" => pedido with { Conjunto = null },
            "aplicacao-caminho" => pedido with { Aplicacao = "../site" },
            "aplicacao-maiuscula" => pedido with { Aplicacao = "Site" },
            "aplicacao-vazia" => pedido with { Aplicacao = "" },
            "operacao" => pedido with { Operacao = "deploy" },
            "repositorio" => pedido with { Repositorio = "https://github.com/demo/outro" },
            _ => pedido with { Sha = new string('c', 40) },
        };
        Assert.NotEmpty(Validar(pedido));
    }

    [Fact]
    public void Geracao_antiga_permanece_compativel_e_nao_aceita_campos_da_nova_operacao()
    {
        var antigo = new AcaoNoProjetoRemotoV1(ArtefatosV1.AcaoArtefato, "demo");
        var texto = JsonSerializer.Serialize(antigo, Json);
        Assert.Equal("", Validar(antigo));
        using var documento = JsonDocument.Parse(texto);
        Assert.Equal(new[] { "acao", "projeto", "repositorio", "sha" }, documento.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Contains("Não combine geração", Validar(Pedido() with { Acao = ArtefatosV1.AcaoArtefato }));
        Assert.NotEmpty(RegrasDosArtefatos.ValidarPreview(new("https://github.com/demo/site", "abcdef0",
            "http://127.0.0.1:4317/", "demo")));
    }
}
