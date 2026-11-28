using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// (boards-estados, item 5) As regras puras do espelho no Azure Boards e do pedido remoto do projeto: o nível e os estados
/// no formato do contrato, a preservação dos campos que o cliente antigo não envia, e o aprovar separado do disparar —
/// sem item, na sua própria situação.
/// </summary>
public sealed class EspelhoEDisparoDosProjetosTests
{
    private static EnvioDeProjetoV1 Envio(string? nivel = null, EstadosNoBoardV1? estados = null, DateTimeOffset? disparadoEm = null)
        => new("boards-estados", "Projetos em qualquer nível", null, null, ["https://github.com/acme/portal"],
            "feature/dev/projeto-boards-estados", false, "Definição", [], true, 2, "main", ProjetosV1.Aprovado, null,
            [new(1, "Espelho configurável", "", "", [], [], [], 1, 813, ProjetosV1.ItemPlanejado)],
            NivelNoBoard: nivel, EstadosNoBoard: estados, DisparadoEm: disparadoEm);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("epic")]
    [InlineData("feature")]
    [InlineData("requisito")]
    [InlineData(" Feature ")]
    public void O_nivel_do_pai_e_epic_feature_ou_requisito_e_vazio_e_feature(string? nivel)
        => Assert.Empty(RegrasDosProjetos.Validar(Envio(nivel)));

    [Theory]
    [InlineData("task")]
    [InlineData("portfolio")]
    [InlineData("User Story")]
    public void Nivel_fora_do_contrato_e_recusado_com_os_validos(string nivel)
    {
        var erro = RegrasDosProjetos.Validar(Envio(nivel));

        Assert.Contains("nível do projeto no Azure Boards", erro, StringComparison.Ordinal);
        Assert.Contains("epic, feature, requisito", erro, StringComparison.Ordinal);
    }

    [Fact]
    public void Estados_com_nome_vazio_ou_ausente_valem_e_o_nome_longo_ou_de_varias_linhas_e_recusado()
    {
        var configurados = new EstadosNoBoardV1(new("", "", "Resolved"), new("Proposed", null, "Review"));
        Assert.Empty(RegrasDosProjetos.Validar(Envio(estados: configurados)));
        Assert.Empty(RegrasDosProjetos.Validar(Envio(estados: new EstadosNoBoardV1())));
        Assert.Empty(RegrasDosProjetos.Validar(Envio(estados: new(Itens: new(Finalizacao: new string('x', ProjetosV1.TamanhoMaximoDoEstado))))));

        var longo = RegrasDosProjetos.Validar(Envio(estados: new(Itens: new(Finalizacao: new string('x', ProjetosV1.TamanhoMaximoDoEstado + 1)))));
        Assert.Contains("(itens)", longo, StringComparison.Ordinal);
        var quebrado = RegrasDosProjetos.Validar(Envio(estados: new(Pai: new(Execucao: "Active\nClosed"))));
        Assert.Contains("(pai)", quebrado, StringComparison.Ordinal);
    }

    [Fact]
    public void A_marca_de_disparo_e_qualquer_data_ou_nula()
    {
        Assert.Empty(RegrasDosProjetos.Validar(Envio(disparadoEm: new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero))));
        Assert.Empty(RegrasDosProjetos.Validar(Envio(disparadoEm: null)));
    }

    [Fact]
    public void Preservar_mantem_o_espelho_e_a_marca_quando_o_cliente_antigo_nao_os_envia()
    {
        var gravado = new ProjetoV1(1, "boards-estados", "Projetos", "usuario", "", ["repo"], "feature/x", false, "", [], true, 2,
            "main", ProjetosV1.Aprovado, 3, "Dev", DateTimeOffset.UnixEpoch, [], true,
            NivelNoBoard: "epic", EstadosNoBoard: new(new("", "", "Resolved")), DisparadoEm: DateTimeOffset.UnixEpoch.AddDays(1));

        var preservado = EscolhasDosProjetos.Preservar(Envio(), gravado);
        Assert.Equal("epic", preservado.NivelNoBoard);
        Assert.Equal("Resolved", preservado.EstadosNoBoard?.Pai?.Finalizacao);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), preservado.DisparadoEm);

        // Explícito vale sobre o gravado: o nível vazio é a feature, e os estados enviados substituem os anteriores.
        var trocado = EscolhasDosProjetos.Preservar(Envio("", new EstadosNoBoardV1()), gravado);
        Assert.Equal("", trocado.NivelNoBoard);
        Assert.Null(trocado.EstadosNoBoard?.Pai);
    }

    [Theory]
    [InlineData("""{"acao":"aprovar","projeto":"boards-estados"}""", "aprovar", null)]
    [InlineData("""{"acao":"disparar","projeto":"boards-estados"}""", "disparar", null)]
    [InlineData("""{"acao":"disparar","projeto":"boards-estados","item":5}""", "disparar", 5)]
    [InlineData("""{"acao":"retomar","projeto":"boards-estados"}""", "retomar", null)]
    public void O_pedido_remoto_le_aprovar_disparar_e_retomar(string texto, string acao, int? item)
    {
        var pedido = DisparoDeProjetoRemoto.Ler(texto);

        Assert.NotNull(pedido);
        Assert.Equal((acao, "boards-estados", item), (pedido.Acao, pedido.Projeto, pedido.Item));
        Assert.Empty(RegrasDaCriacaoRemota.Validar(ConversaV1.ComandoProjeto, texto));
    }

    [Theory]
    [InlineData("""{"acao":"aprovar","projeto":"boards-estados","item":1}""")]
    [InlineData("""{"acao":"retomar","projeto":"boards-estados","item":1}""")]
    [InlineData("""{"acao":"disparar","projeto":"boards-estados","item":0}""")]
    public void Aprovar_e_retomar_nao_levam_item_e_o_item_do_disparo_e_positivo(string texto)
    {
        var erro = Assert.Throws<ArgumentException>(() => DisparoDeProjetoRemoto.Ler(texto));

        Assert.Equal("Item inválido para a ação.", erro.Message);
        Assert.Equal("Item inválido para a ação.", RegrasDaCriacaoRemota.Validar(ConversaV1.ComandoProjeto, texto));
    }

    [Fact]
    public void Pedido_sem_projeto_com_campo_a_mais_ou_acao_nao_textual_e_recusado_e_acao_desconhecida_nao_e_disparo()
    {
        Assert.Throws<ArgumentException>(() => DisparoDeProjetoRemoto.Ler("""{"acao":"aprovar","projeto":""}"""));
        Assert.ThrowsAny<JsonException>(() => DisparoDeProjetoRemoto.Ler("""{"acao":"aprovar","projeto":"p","forcar":true}"""));
        Assert.ThrowsAny<JsonException>(() => DisparoDeProjetoRemoto.Ler("""{"acao":1,"projeto":"p"}"""));
        Assert.Null(DisparoDeProjetoRemoto.Ler("""{"acao":"aprovar-e-disparar","projeto":"p"}"""));
        Assert.Null(DisparoDeProjetoRemoto.Ler("""{"acao":"artefato","projeto":"p"}"""));
    }

    [Theory]
    [InlineData("aprovar", ProjetosV1.AguardandoAprovacao)]
    [InlineData("disparar", ProjetosV1.Aprovado)]
    [InlineData("retomar", ProjetosV1.Pausado)]
    public void Cada_acao_exige_a_sua_situacao(string acao, string situacao)
        => Assert.Equal(situacao, DisparoDeProjetoRemoto.SituacaoExigida(acao));
}
