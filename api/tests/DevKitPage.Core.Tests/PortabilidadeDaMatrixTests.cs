using DevKit.Matrix;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

public sealed class PortabilidadeDaMatrixTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Transferencia_aceita_a_formatacao_historica_e_recusa_mudanca_no_texto_do_item(string novaLinha)
    {
        var item = new ItemDaTransferenciaMatrix(6, "Validacao", "Linha um\r\nLinha dois", "item",
            ["repo-a"], [], [1], 2, 0, "planejado", "feature/projeto-item-6", "", "", "", 1, 1, "");
        var canonico = RegrasDaTransferenciaMatrix.Snapshot(item);
        Assert.DoesNotContain("\r", canonico);
        var snapshot = canonico.Replace("\n", novaLinha, StringComparison.Ordinal);
        var transferencia = new TransferenciaDeValidacaoMatrix("origem", 6, 1, "finalizacao",
            "Conjunto", "Plano", snapshot, DateTimeOffset.UtcNow, []);
        RegrasDaTransferenciaMatrix.ConferirFonte(transferencia, [item]);
        Assert.Throws<ArgumentException>(() => RegrasDaTransferenciaMatrix.ConferirFonte(transferencia,
            [item with { Descricao = "Linha um\nLinha dois" }]));
        Assert.Equal(snapshot, transferencia.SnapshotDoItem);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Recibo_historico_mantem_hash_original_e_exige_a_composicao_exata(string novaLinha)
    {
        var r = new ReciboDaFinalizacaoV1(1, "sessao", 1, "projeto", "feature/projeto", "develop",
            1, "fluxo", "rev", "claude", "sonnet", new Dictionary<string, string> { [new('b', 64)] = new('a', 40) });
        var canonico = RegrasVerificacaoFinalMatrix.Revisao(r);
        Assert.DoesNotContain("\r", canonico);
        var revisao = canonico.Replace("\n", novaLinha, StringComparison.Ordinal);
        var evento = "finalizacao:1:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(revisao)));
        var e = new EventoMatrix(evento, "demanda", "maquina", r.Sessao, 0, 1, 1, 1,
            ["finalizacao"], ["c1"], r.Shas.ToDictionary(p => p.Key, p => p.Value), revisao) { Finalizacao = r };
        var documento = new DocumentoMatrix { Projeto = "projeto" };
        RegrasVerificacaoFinalMatrix.Validar(documento, e);
        Assert.True(RegrasVerificacaoFinalMatrix.MesmaRevisao(r, revisao));
        Assert.False(RegrasVerificacaoFinalMatrix.MesmaRevisao(r with { Modelo = "outro" }, revisao));
        Assert.Throws<ArgumentException>(() => RegrasVerificacaoFinalMatrix.Validar(documento,
            e with { Evento = "finalizacao:1:" + new string('0', 64) }));
        Assert.Throws<ArgumentException>(() => RegrasVerificacaoFinalMatrix.Validar(documento,
            e with { Finalizacao = r with { Execucao = 2 } }));
        Assert.Equal(revisao, e.RevisaoDaEntrega);
        Assert.Equal(evento, e.Evento);
    }
}
