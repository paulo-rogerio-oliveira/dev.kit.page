using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevKitPage.Api;

/// <summary>Clientes matrix-v1 estritos continuam recebendo somente os campos que conhecem.</summary>
internal static class CompatibilidadeMatrix
{
    public static bool Avaliacao(HttpRequest request) => request.Headers["X-DevKit-Matrix-Avaliacao"] == "1";

    public static IResult Responder(HttpRequest request, object? valor)
    {
        if (Avaliacao(request)) return Results.Ok(valor);
        var json = JsonSerializer.SerializeToNode(valor, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Limpar(json);
        return Results.Json(json);
    }

    private static void Limpar(JsonNode? node)
    {
        if (node is JsonArray lista)
        {
            foreach (var item in lista.ToArray())
            {
                if (item is JsonObject o && o["tipo"]?.GetValue<string>() == "evento") lista.Remove(item);
                else Limpar(item);
            }
        }
        else if (node is JsonObject objeto)
        {
            if (objeto["registro"] is JsonObject registro)
                foreach (var campo in new[] { "evento", "sessaoAvaliada", "avaliador", "revisaoEfetiva", "revisaoDoContexto", "revisaoDaEntrega" }) registro.Remove(campo);
            if (objeto["matriz"] is JsonObject matriz) matriz.Remove("coberturas");
            objeto.Remove("evento");
            foreach (var item in objeto.ToArray()) Limpar(item.Value);
        }
    }
}
