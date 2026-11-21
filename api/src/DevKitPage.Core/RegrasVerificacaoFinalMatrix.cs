using DevKitPage.Contracts.V1;
using System.Text.Json;
using DevKit.Matrix;

namespace DevKitPage.Core;

/// <summary>Verificação por obrigação da operação final. Não representa nota nem avaliação de implementação.</summary>
public static class RegrasVerificacaoFinalMatrix
{
    public const string Tipo = "verificacao-final";
    public static string Revisao(ReciboDaFinalizacaoV1 r)
        => JsonSerializer.Serialize(r with { Shas = r.Shas.OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value.ToLowerInvariant(), StringComparer.Ordinal) }, RegrasMatrix.Json);

    public static string Evento(ReciboDaFinalizacaoV1 r)
        => "finalizacao:" + r.Execucao + ":" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Revisao(r))));

    public static void Validar(DocumentoMatrix d, EventoMatrix e)
    {
        var r = e.Finalizacao;
        RegrasMatrix.Exigir(r is not null && r.Contrato == 1 && r.Execucao > 0 && r.RevisaoDaPolitica > 0
            && d.Projeto.Length > 0 && r.Projeto == d.Projeto && r.Sessao == e.SessaoAvaliada
            && new[] { r.Branch, r.Destino, r.Fluxo, r.RevisaoDoFluxo, r.Agente, r.Modelo }.All(s => !string.IsNullOrWhiteSpace(s))
            && r.Shas is { Count: > 0 } && r.Shas.All(s => s.Key is { Length: 64 } && s.Key.All(char.IsAsciiHexDigit)
                && s.Value is { Length: 40 } && s.Value.All(char.IsAsciiHexDigit)), "Verificação final exige recibo da composição confirmada pelo host.");
        RegrasMatrix.Exigir(e.Avaliador == 0 && e.Shas is not null && e.Evento == Evento(r) && e.RevisaoDaEntrega == Revisao(r)
            && RegrasAvaliacaoMatrix.MesmosShas(e.Shas, r.Shas), "Evento final difere da reserva/composição; não admite avaliador ou nota.");
    }
}
