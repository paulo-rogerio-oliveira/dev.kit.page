using System.Text.Json;

namespace DevKitPage.Core;

/// <summary>
/// Pedido tipado, sem comando de shell ou opção para ignorar portões. As ações (boards-estados, item 5): <c>aprovar</c>
/// o plano que aguarda aprovação (sem item: aprova o plano inteiro e não dispara nada), <c>disparar</c> o projeto aprovado
/// (a onda que espera) e <c>retomar</c> o pausado. O host repete os portões antes de executar.
/// </summary>
public sealed record DisparoDeProjetoRemoto(string Acao, string Projeto, int? Item = null)
{
    public const string Aprovar = "aprovar";
    public const string Disparar = "disparar";
    public const string Retomar = "retomar";

    /// <summary>As ações do pedido remoto no projeto, na ordem da vida dele.</summary>
    public static IReadOnlyList<string> Acoes { get; } = [Aprovar, Disparar, Retomar];

    public static DisparoDeProjetoRemoto? Ler(string texto)
    {
        using var json = JsonDocument.Parse(texto);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("acao", out var acao)) return null;
        if (acao.ValueKind != JsonValueKind.String) throw new JsonException("Ação deve ser texto.");
        if (acao.GetString() is not (Aprovar or Disparar or Retomar)) return null;
        var pedido = JsonSerializer.Deserialize<DisparoDeProjetoRemoto>(texto, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
        RegrasMatrix.Exigir(pedido is not null && RegrasMatrix.Identificador(pedido.Projeto), "Informe o projeto (slug).");
        RegrasMatrix.Exigir(pedido.Item is null or > 0 && (pedido.Acao == Disparar || pedido.Item is null), "Item inválido para a ação.");
        return pedido;
    }

    /// <summary>A situação do projeto que a ação exige: aguardando aprovação para aprovar, pausado para retomar, aprovado para disparar.</summary>
    public static string SituacaoExigida(string acao) => acao switch
    {
        Aprovar => Contracts.V1.ProjetosV1.AguardandoAprovacao,
        Retomar => Contracts.V1.ProjetosV1.Pausado,
        _ => Contracts.V1.ProjetosV1.Aprovado,
    };
}
