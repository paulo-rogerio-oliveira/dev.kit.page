using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DevKitPage.Core;

/// <summary>Leitura do envelope portatil do motor; distribuicao nao cria nem ativa gatilho no site.</summary>
public static class GatilhoDoCatalogo
{
    public const string Tipo = "gatilho";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly IReadOnlyDictionary<string, string> Eventos = new Dictionary<string, string>
    {
        ["push"] = "ao enviar (push)", ["concluir"] = "ao concluir", ["revisar"] = "ao revisar", ["responder"] = "ao responder",
        ["objetivo"] = "ao cumprir o objetivo", ["aprovar"] = "ao aprovar", ["comentario-revisor"] = "ao comentar o revisor (PR)",
        ["falha-ci"] = "ao falhar o CI", ["comentario-workitem"] = "ao comentar o work item (/devkit, #dk)",
    };

    public static GeradorDoManifesto.ItemLido Ler(string arquivo, string texto)
    {
        var raiz = JsonNode.Parse(texto) as JsonObject ?? throw new FormatException("Envelope do gatilho invalido.");
        var nome = Texto(raiz, "nome").Trim();
        if (Texto(raiz, "formato") != "devkit-gatilho" || raiz["versao"]?.GetValue<int>() != 1 || nome.Length == 0
            || Texto(raiz, "distribuir").Trim() is not ("" or "base"))
            throw new FormatException("Gatilho sem nome, formato conhecido ou distribuicao base.");
        var eventos = Lista(raiz, "eventos").Select(e => e.ToLowerInvariant()).Where(Eventos.ContainsKey).Distinct().ToArray();
        if (eventos.Length == 0) throw new FormatException("Gatilho sem evento conhecido.");
        var repos = Lista(raiz, "repositorios");
        if (repos.Any(r => Regex.IsMatch(r, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://[^/@\s]+@")))
            throw new FormatException("Gatilho com credencial em repositorio nao e portatil.");
        var plano = FormatosDoCatalogo.Normalizar(Texto(raiz, "plano"));
        // Mesma ordem e normalizacao de GatilhoCatalogFiles.Exportar/HashDoCatalogo.Gatilho no motor fixado.
        var canonico = new JsonObject
        {
            ["formato"] = "devkit-gatilho", ["versao"] = 1, ["nome"] = nome,
            ["eventos"] = new JsonArray(eventos.Select(e => (JsonNode)JsonValue.Create(e)!).ToArray()),
            ["repositorios"] = new JsonArray(repos.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
            ["branch"] = Texto(raiz, "branch").Trim(), ["plano"] = plano,
            ["prompt"] = FormatosDoCatalogo.Normalizar(Texto(raiz, "prompt")), ["agente"] = Texto(raiz, "agente").Trim(),
            ["modelo"] = Texto(raiz, "modelo").Trim(), ["somenteEscolhido"] = Flag(raiz, "somenteEscolhido"), ["avaliador"] = Flag(raiz, "avaliador"),
        };
        return new(Tipo, FormatosDoCatalogo.Slug(nome, Tipo), FormatosDoCatalogo.Sha256(canonico.ToJsonString(Json)), arquivo,
            nome + " — " + string.Join(" · ", eventos.Select(e => Eventos[e])), GeradorDoManifesto.Requer(plano), [], "Gatilhos");
    }

    private static string Texto(JsonObject o, string campo) => o[campo] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    private static bool Flag(JsonObject o, string campo) => o[campo] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    private static string[] Lista(JsonObject o, string campo) => (o[campo] as JsonArray ?? []).OfType<JsonValue>()
        .Select(v => v.TryGetValue<string>(out var s) ? s.Trim() : "").Where(s => s.Length > 0).ToArray();
}
