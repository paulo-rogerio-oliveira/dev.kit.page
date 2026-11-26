using System.Text.Json;

namespace DevKitPage.Core;

/// <summary>Pedido tipado, sem comando de shell ou opção para ignorar portões.</summary>
public sealed record DisparoDeProjetoRemoto(string Acao, string Projeto, int? Item = null)
{
    public static DisparoDeProjetoRemoto? Ler(string texto)
    {
        using var json = JsonDocument.Parse(texto);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("acao", out var acao)) return null;
        if (acao.ValueKind != JsonValueKind.String) throw new JsonException("Ação deve ser texto.");
        if (acao.GetString() is not ("disparar" or "retomar")) return null;
        var pedido = JsonSerializer.Deserialize<DisparoDeProjetoRemoto>(texto, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
        RegrasMatrix.Exigir(pedido is not null && RegrasMatrix.Identificador(pedido.Projeto), "Informe o projeto (slug).");
        RegrasMatrix.Exigir(pedido.Item is null or > 0 && (pedido.Acao != "retomar" || pedido.Item is null), "Item inválido para a ação.");
        return pedido;
    }
}
