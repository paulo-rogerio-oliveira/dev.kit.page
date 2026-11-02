using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras do controle remoto: o id da máquina, o que conta como dado de cliente e o que recusa o heartbeat e o
/// retrato. Os textos do contrato estão em <c>DevKitPage.Api.Tests/ContratoDoRemoto/textos.json</c>, pela rota de verdade.
/// </summary>
public sealed class RegrasDoRemotoTests
{
    private const string Maquina = "3f2b1c9e5d7a4b60a1c2d3e4f5061728";

    private static HeartbeatV1 Sinal() => new(RemotoV1.Versao, Maquina, "Notebook", "1.61.0", true, RemotoV1.AgenteAtivo, 0);

    private static ProjetoDoRetratoV1 Projeto(string slug = "portal") => new(slug, "Portal", ProjetosV1.Aprovado, 3, 1, 1, 1, 2);

    private static FluxoDoRetratoV1 Fluxo(string id = "a7d04c1e") => new(id, "Item 1", "Fluxo", "Etapa", RemotoV1.FluxoRodando, "portal", 1, null);

    private static PendenciaDoRetratoV1 Pendencia(string id = "p1") => new(id, RemotoV1.PendenciaPergunta, "Seguir?", "", ["Sim", "Não"], null, null, null);

    private static RetratoV1 Foto(IReadOnlyList<ProjetoDoRetratoV1>? projetos = null, IReadOnlyList<FluxoDoRetratoV1>? fluxos = null, IReadOnlyList<PendenciaDoRetratoV1>? pendencias = null)
        => new(RemotoV1.Versao, Maquina, new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero), projetos ?? [Projeto()], fluxos ?? [Fluxo()], pendencias ?? [Pendencia()]);

    [Theory]
    [InlineData(Maquina, true)]
    [InlineData("3f2b1c9e-5d7a-4b60-a1c2-d3e4f5061728", true)]
    [InlineData("3F2B1C9E5D7A4B60A1C2D3E4F5061728", true)]
    [InlineData("{3f2b1c9e-5d7a-4b60-a1c2-d3e4f5061728}", false)]
    [InlineData("DESKTOP-ANA", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void O_id_da_maquina_e_um_guid_e_nunca_o_hostname(string? id, bool valido) => Assert.Equal(valido, RegrasDoRemoto.MaquinaValida(id));

    [Fact]
    public void O_mesmo_guid_escrito_de_outro_jeito_e_a_mesma_maquina()
        => Assert.Equal(Maquina, RegrasDoRemoto.NormalizarMaquina("3F2B1C9E-5D7A-4B60-A1C2-D3E4F5061728"));

    [Theory]
    [InlineData(@"C:\Users\ana\acme.txt")]
    [InlineData("https://acme.visualstudio.com/x")]
    [InlineData("ana@acme.com")]
    [InlineData("123.456.789-09")]
    [InlineData("12345678909")]
    [InlineData("12.345.678/0001-95")]
    [InlineData("(11) 91234-5678")]
    [InlineData("11 3456-7890")]
    public void Reconhece_o_dado_de_cliente(string texto) => Assert.True(RegrasDoRemoto.ContemDadoDeCliente(texto));

    [Theory]
    [InlineData("Item 20 · layout do plano")]
    [InlineData("  Nota 100 · regressão sem falhas  ")]
    [InlineData("Onda 10 de 10")]
    [InlineData("Versão 1.61.0 em 02/11/2026")]
    [InlineData("Work item 123456")]
    [InlineData("")]
    [InlineData(null)]
    public void Nao_barra_o_texto_comum_do_retrato(string? texto) => Assert.False(RegrasDoRemoto.ContemDadoDeCliente(texto));

    [Fact]
    public void O_heartbeat_e_o_retrato_validos_passam() => Assert.Multiple(
        () => Assert.Null(RegrasDoRemoto.Validar(Sinal())),
        () => Assert.Null(RegrasDoRemoto.Validar(Foto())),
        () => Assert.Null(RegrasDoRemoto.Validar(Foto([], [], []))));

    [Fact]
    public void O_heartbeat_fora_do_contrato_e_400() => Assert.All(
        new HeartbeatV1?[]
        {
            null,
            Sinal() with { Versao = "v2" },
            Sinal() with { MaquinaId = "DESKTOP-ANA" },
            Sinal() with { Agente = "Em limite" },
            Sinal() with { TurnosEmAndamento = -1 },
            Sinal() with { VersaoDevKit = "" },
            Sinal() with { Apelido = new string('a', RemotoV1.TamanhoMaximoDoApelido + 1) },
            Sinal() with { Apelido = "duas\nlinhas" },
        },
        h => Assert.Equal(400, RegrasDoRemoto.Validar(h)?.Status));

    [Fact]
    public void O_retrato_fora_do_contrato_e_400() => Assert.All(
        new RetratoV1?[]
        {
            null,
            Foto() with { Versao = "v2" },
            Foto() with { MaquinaId = "DESKTOP-ANA" },
            Foto() with { GeradoEm = default },
            Foto() with { Projetos = null! },
            Foto() with { Fluxos = null! },
            Foto() with { Pendencias = null! },
            Foto(Enumerable.Range(0, RemotoV1.MaximoDeProjetos + 1).Select(i => Projeto($"p{i}")).ToArray()),
            Foto(fluxos: Enumerable.Range(0, RemotoV1.MaximoDeFluxos + 1).Select(i => Fluxo($"f{i}")).ToArray()),
            Foto(pendencias: Enumerable.Range(0, RemotoV1.MaximoDePendencias + 1).Select(i => Pendencia($"p{i}")).ToArray()),
            Foto([Projeto("Com Espaco")]),
            Foto([Projeto() with { Situacao = "Em andamento" }]),
            Foto([Projeto() with { Integrados = 3, Rodando = 1 }]),
            Foto([Projeto() with { Onda = 3 }]),
            Foto([Projeto(), Projeto()]),
            Foto([Projeto() with { Nome = "" }]),
            Foto(fluxos: [Fluxo("id com espaço")]),
            Foto(fluxos: [Fluxo() with { Situacao = "" }]),
            Foto(fluxos: [Fluxo() with { Projeto = null }]),
            Foto(fluxos: [Fluxo() with { Item = 0 }]),
            Foto(fluxos: [Fluxo(), Fluxo()]),
            Foto(fluxos: [Fluxo() with { Titulo = new string('a', RemotoV1.TamanhoMaximoDoTitulo + 1) }]),
            Foto(pendencias: [Pendencia() with { Tipo = "Pergunta" }]),
            Foto(pendencias: [Pendencia() with { Opcoes = null! }]),
            Foto(pendencias: [Pendencia() with { Opcoes = Enumerable.Repeat("x", RemotoV1.MaximoDeOpcoes + 1).ToArray() }]),
            Foto(pendencias: [Pendencia() with { Opcoes = [""] }]),
            Foto(pendencias: [Pendencia() with { Resumo = new string('a', RemotoV1.TamanhoMaximoDoResumo + 1) }]),
            Foto(pendencias: [Pendencia(), Pendencia()]),
        },
        r => Assert.Equal(400, RegrasDoRemoto.Validar(r)?.Status));

    [Fact]
    public void A_recusa_diz_o_campo_e_o_motivo_sem_ecoar_o_valor()
    {
        var recusa = RegrasDoRemoto.Validar(Foto(fluxos: [Fluxo(), Fluxo("b") with { Etapa = "senha: hunter2" }]))!;

        Assert.Equal((422, "fluxos[1].etapa", RemotoV1.MotivoSegredo), (recusa.Status, recusa.Campo, recusa.Motivo));
        Assert.DoesNotContain("hunter2", recusa.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void O_identificador_pode_ser_um_guid_mas_nao_um_token()
    {
        Assert.Null(RegrasDoRemoto.Validar(Foto(fluxos: [Fluxo("3f2b1c9e-5d7a-4b60-a1c2-d3e4f5061728")])));

        var recusa = RegrasDoRemoto.Validar(Foto(fluxos: [Fluxo("ghp_abcdefghijklmnopqrstuvwxyz0123456789")]))!;
        Assert.Equal((422, "fluxos[0].id", RemotoV1.MotivoSegredo), (recusa.Status, recusa.Campo, recusa.Motivo));
    }

    [Fact]
    public void O_segredo_tem_prioridade_e_o_formato_vem_antes_dos_dois()
    {
        var comSegredo = Foto(pendencias: [Pendencia() with { Titulo = "token=abc", Resumo = "ana@acme.com" }]);

        Assert.Equal(RemotoV1.MotivoSegredo, RegrasDoRemoto.Validar(comSegredo)!.Motivo);
        Assert.Equal(400, RegrasDoRemoto.Validar(comSegredo with { Versao = "v2" })!.Status);
    }

    [Fact]
    public void A_conexao_vale_ate_a_tolerancia_inclusive()
    {
        var ultimo = new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

        Assert.True(RegrasDoRemoto.Conectada(ultimo, ultimo.AddSeconds(180), 180));
        Assert.False(RegrasDoRemoto.Conectada(ultimo, ultimo.AddSeconds(181), 180));
    }

    [Theory]
    [InlineData("""{"versao":"v1","maquinaId":"x","apelido":"","versaoDevKit":"1","devKitAberto":true,"agente":"ativo","turnosEmAndamento":0,"token":"x"}""", typeof(HeartbeatV1))]
    [InlineData("""{"slug":"p","nome":"P","situacao":"aprovado","itens":0,"integrados":0,"rodando":0,"onda":0,"ondas":0,"definicao":"x"}""", typeof(ProjetoDoRetratoV1))]
    [InlineData("""{"id":"f","titulo":"T","fluxo":"","etapa":"","situacao":"rodando","projeto":null,"item":null,"desde":null,"pasta":"x"}""", typeof(FluxoDoRetratoV1))]
    [InlineData("""{"id":"p","tipo":"pergunta","titulo":"T","resumo":"","opcoes":[],"projeto":null,"item":null,"desde":null,"prompt":"x"}""", typeof(PendenciaDoRetratoV1))]
    public void O_envio_e_fechado_e_campo_fora_do_contrato_nao_passa_pelo_json(string json, Type tipo)
        => Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, tipo, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
}
