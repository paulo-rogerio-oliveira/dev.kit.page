using DevKitPage.Contracts.V1;
using System.Text.Json;
using DevKit.Matrix;

namespace DevKitPage.Core;

/// <summary>Verificação por obrigação da operação final. Não representa nota nem avaliação de implementação.</summary>
public static class RegrasVerificacaoFinalMatrix
{
    public const string Tipo = "verificacao-final";
    private static readonly JsonSerializerOptions JsonDaComposicao = new(RegrasMatrix.Json) { NewLine = "\n" };
    public static string Revisao(ReciboDaFinalizacaoV1 r)
        => JsonSerializer.Serialize(r with { Shas = r.Shas.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value.ToLowerInvariant(), StringComparer.Ordinal) }, JsonDaComposicao);

    /// <summary>Preserva os recibos anteriores com CRLF sem reescrever sua identidade ou aceitar outra composição.</summary>
    public static bool MesmaRevisao(ReciboDaFinalizacaoV1 r, string? revisao)
        => revisao is not null && revisao.Replace("\r\n", "\n", StringComparison.Ordinal) == Revisao(r);

    public static string Evento(ReciboDaFinalizacaoV1 r)
        => IdentidadeDoEvento(r, Revisao(r));

    private static string IdentidadeDoEvento(ReciboDaFinalizacaoV1 r, string revisao)
        => "finalizacao:" + r.Execucao + ":" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(revisao)));

    public static void Validar(DocumentoMatrix d, EventoMatrix e)
    {
        var r = e.Finalizacao;
        RegrasMatrix.Exigir(r is not null && r.Contrato == 1 && r.Execucao > 0 && r.RevisaoDaPolitica > 0
            && d.Projeto.Length > 0 && r.Projeto == d.Projeto && r.Sessao == e.SessaoAvaliada
            && new[] { r.Branch, r.Destino, r.Fluxo, r.RevisaoDoFluxo, r.Agente, r.Modelo }.All(s => !string.IsNullOrWhiteSpace(s))
            && r.Shas is { Count: > 0 } && r.Shas.All(s => s.Key is { Length: 64 } && s.Key.All(char.IsAsciiHexDigit)
                && s.Value is { Length: 40 } && s.Value.All(char.IsAsciiHexDigit)), "Verificação final exige recibo da composição confirmada pelo host.");
        RegrasMatrix.Exigir(e.Avaliador == 0 && e.Shas is not null && MesmaRevisao(r, e.RevisaoDaEntrega)
            && e.Evento == IdentidadeDoEvento(r, e.RevisaoDaEntrega)
            && RegrasAvaliacaoMatrix.MesmosShas(e.Shas, r.Shas), "Evento final difere da reserva/composição; não admite avaliador ou nota.");
    }
}
