using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>As regras puras: a validação do lote, a qualidade com divisor zero, a chave da máquina, a senha e o período.</summary>
public sealed class RegrasTests
{
    private static readonly DateTimeOffset Em = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static TelemetryBatchV1 Lote(int eventos, string versao = "v1", string maquina = "m1")
        => new(versao, maquina, "1.4.0", Enumerable.Range(1, eventos)
            .Select(i => new TelemetryEventV1($"e{i}", TiposDeEvento.TurnoExecutado, "s1", 1, 1000, "claude", Em)).ToArray());

    [Fact]
    public void Lote_valido_passa()
        => Assert.Null(ValidadorDeLote.Validar(Lote(3), "m1", 10));

    [Fact]
    public void Lote_acima_do_maximo_e_413()
        => Assert.Equal(413, ValidadorDeLote.Validar(Lote(11), "m1", 10)!.Status);

    [Theory]
    [InlineData("v2", "m1")]
    [InlineData("v1", "outra")]
    public void Versao_desconhecida_ou_outra_maquina_e_400(string versao, string maquina)
        => Assert.Equal(400, ValidadorDeLote.Validar(Lote(1, versao, maquina), "m1", 10)!.Status);

    [Fact]
    public void Evento_sem_id_ou_com_quantidade_negativa_e_400()
    {
        var semId = new TelemetryBatchV1("v1", "m1", "x", new[] { new TelemetryEventV1(" ", "TurnoExecutado", "s", 1, null, "", Em) });
        var negativo = new TelemetryBatchV1("v1", "m1", "x", new[] { new TelemetryEventV1("e1", "TurnoExecutado", "s", -1, null, "", Em) });

        Assert.Equal(400, ValidadorDeLote.Validar(semId, "m1", 10)!.Status);
        Assert.Equal(400, ValidadorDeLote.Validar(negativo, "m1", 10)!.Status);
        Assert.Equal(400, ValidadorDeLote.Validar(null, "m1", 10)!.Status);
    }

    [Fact]
    public void Qualidade_sem_denominador_e_nula_e_nao_nan()
    {
        var periodo = new Periodo(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3));

        var vazia = CalculoDeQualidade.Calcular(periodo, Array.Empty<SomaPorTipo>());

        Assert.Null(vazia.TaxaDeFalha);
        Assert.Null(vazia.NotaMedia);
        Assert.Null(vazia.RazaoCumpridosRecusados);
        Assert.Null(vazia.TurnosPorObjetivoCumprido);
        Assert.Null(vazia.DuracaoMediaDoTurnoMs);
        Assert.Empty(vazia.FalhasPorCausa);
    }

    [Fact]
    public void Qualidade_soma_as_razoes_e_as_falhas_por_causa()
    {
        var somas = new[]
        {
            new SomaPorTipo(TiposDeEvento.TurnoExecutado, "claude", 3, 3, 30000),
            new SomaPorTipo(TiposDeEvento.TurnoExecutado, "kiro", 1, 1, 10000),
            new SomaPorTipo(TiposDeEvento.TurnoFalhou, "limite-de-uso", 1, 1, 0),
            new SomaPorTipo(TiposDeEvento.ObjetivoAvaliado, "", 2, 2, 180),
            new SomaPorTipo(TiposDeEvento.ObjetivoCumprido, "", 2, 2, 0),
            new SomaPorTipo(TiposDeEvento.ObjetivoRecusado, "", 1, 1, 0),
        };

        var q = CalculoDeQualidade.Calcular(new Periodo(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)), somas);

        Assert.Equal(4, q.Turnos);
        Assert.Equal(0.25, q.TaxaDeFalha);
        Assert.Equal(10000, q.DuracaoMediaDoTurnoMs);
        Assert.Equal(90, q.NotaMedia);
        Assert.Equal(2.0, q.RazaoCumpridosRecusados);
        Assert.Equal(2.0, q.TurnosPorObjetivoCumprido);
        Assert.Equal(new CausaDeFalha("limite-de-uso", 1), Assert.Single(q.FalhasPorCausa));
    }

    [Fact]
    public void Chave_da_maquina_e_aleatoria_e_so_o_hash_confere()
    {
        var (chave, hash) = ChaveDeMaquina.Gerar();
        var (outra, _) = ChaveDeMaquina.Gerar();

        Assert.NotEqual(chave, outra);
        Assert.Equal(64, hash.Length);
        Assert.DoesNotContain(chave, hash);
        Assert.Equal(hash, ChaveDeMaquina.Hash(chave));
        Assert.True(ChaveDeMaquina.Iguais("abc", "abc"));
        Assert.False(ChaveDeMaquina.Iguais("abc", "abd"));
    }

    [Theory]
    [InlineData("curta1", false)]
    [InlineData("somenteletras", false)]
    [InlineData("1234567890", false)]
    [InlineData("senhaBoa2026", true)]
    public void Politica_de_senha(string senha, bool valida)
        => Assert.Equal(valida, PoliticaDeSenha.Validar(senha).Length == 0);

    [Fact]
    public void Periodo_padrao_sao_os_ultimos_30_dias_e_a_ordem_e_garantida()
    {
        var hoje = new DateOnly(2026, 10, 3);

        Assert.Equal(new Periodo(new DateOnly(2026, 9, 4), hoje), Periodo.Pedido(null, null, hoje));
        Assert.Equal(new Periodo(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5)), Periodo.Pedido(new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 1), hoje));
    }
}
