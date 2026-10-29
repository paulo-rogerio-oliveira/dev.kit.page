using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Uma skill no formato do dev.kit (o <c>AgentSkill</c> do git.kit, sem o Id do banco local).</summary>
public sealed record SkillDoCatalogo(
    string Name, string Description, string Content, IReadOnlyList<string> Arguments, IReadOnlyList<string> Repositories,
    bool IsCommand, bool IsBackground, bool IsPinned);

/// <summary>Um Tech Plan no formato do dev.kit (o <c>TechPlan</c> do git.kit, sem o Id).</summary>
/// <param name="Stack">
/// (US #687) As stacks da Rule (<c>vb6, dotnet</c>) — o campo da US #657 do git.kit, que o site perdia ao regravar. Só vai
/// ao arquivo quando existe: a Rule sem stack sai byte a byte como antes, e o hash dela no manifesto não muda.
/// </param>
/// <param name="Tipo">(US #687) O tipo da Rule (<c>impacto</c>, a da US #675), com a mesma regra da stack.</param>
public sealed record PlanDoCatalogo(
    string Key, string Description, string Content, string Repository, bool OnlyWithWorkItem, bool IsRequired, bool IsAgentInstruction,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Stack = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tipo = null)
{
    /// <summary>O tipo da Rule de impacto (o <c>TechPlan.TipoImpacto</c> do git.kit).</summary>
    public const string TipoImpacto = "impacto";

    /// <summary>A Rule de impacto do negócio do cliente: nunca vai no catálogo básico. Fora do arquivo (é derivada do tipo).</summary>
    [JsonIgnore]
    public bool EhImpacto => string.Equals(Tipo, TipoImpacto, StringComparison.Ordinal);
}

/// <summary>
/// Os FORMATOS de arquivo do catálogo (US #461) — o porte, para o site, do que o git.kit grava e lê: o <c>SkillJson</c>, o
/// <c>TechPlanJson</c>, a chave fixa do plan (<c>TechPlanKeys</c>/<c>Slugify</c>) e o envelope <c>devkit-fluxo</c> do
/// <c>FlowCatalogFiles</c>. O editor do site grava EXATAMENTE este JSON (mesmos campos e ordem) — a importação na máquina
/// continua sendo a do app —, e as mensagens de erro são as do app, para o admin ler no site o que leria nele.
/// <para>
/// São dois repositórios sem pacote comum: a igualdade é travada por teste de paridade (os arquivos de
/// <c>catalogo/base</c> do git.kit lidos e regravados aqui voltam com o MESMO hash do manifesto) e pelo teste de contrato
/// do manifesto, que tem o mesmo conjunto de fixtures nos dois repositórios.
/// </para>
/// </summary>
public static class FormatosDoCatalogo
{
    /// <summary>A marca do envelope do fluxo.</summary>
    public const string FormatoDoFluxo = "devkit-fluxo";

    /// <summary>A versão do envelope que o dev.kit lê.</summary>
    public const int VersaoDoFluxo = 1;

    /// <summary>As pastas do zip do catálogo, por tipo (as do pacote do dev.kit).</summary>
    public static string Pasta(string tipo) => tipo switch
    {
        TiposDeItem.Skill => "skills",
        TiposDeItem.Plan => "plans",
        _ => "fluxos",
    };

    // O SkillJson/TechPlanJson do git.kit: indentado, camelCase, sem escapar acentuação.
    private static readonly JsonSerializerOptions Arquivo = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // O envelope do fluxo: indentado, com os nomes como estão (o FlowCatalogFiles escreve o JsonObject à mão).
    private static readonly JsonSerializerOptions Envelope = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonSerializerOptions Compacto = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly JsonDocumentOptions Tolerante = new() { AllowTrailingCommas = true };

    // ----- Skill -----

    /// <summary>O JSON da skill, como o <c>SkillJson.Export</c>.</summary>
    public static string ExportarSkill(SkillDoCatalogo skill) => JsonSerializer.Serialize(skill, Arquivo);

    /// <summary>Lê a skill como o <c>SkillJson.Read</c>: tolerante (lista em array ou linhas, booleano como texto).</summary>
    /// <exception cref="FormatException">O JSON não descreve uma skill — a mensagem é a do app.</exception>
    public static SkillDoCatalogo LerSkill(string json)
    {
        var raiz = Objeto(json, "O JSON da skill é inválido", "O JSON não descreve uma skill (era esperado um objeto).");
        var nome = Texto(raiz, "name").Trim();
        if (nome.Length == 0)
            throw new FormatException("A skill está sem nome (campo \"name\").");
        var conteudo = Texto(raiz, "content");
        if (conteudo.Trim().Length == 0)
            throw new FormatException($"A skill \"{nome}\" veio sem conteúdo (campo \"content\").");

        return new SkillDoCatalogo(
            nome, Texto(raiz, "description").Trim(), conteudo.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(),
            Linhas(raiz, "arguments"), Linhas(raiz, "repositories"), Flag(raiz, "isCommand"), Flag(raiz, "isBackground"), Flag(raiz, "isPinned"));
    }

    // ----- Rule (o tipo "plan": o nome interno segue o antigo Tech Plan) -----

    /// <summary>O JSON do plano, como o <c>TechPlanJson.Export</c> (a chave fixa abre o arquivo).</summary>
    public static string ExportarPlan(PlanDoCatalogo plano) => JsonSerializer.Serialize(plano with { Key = Chave(plano) }, Arquivo);

    /// <summary>Lê o plano como o <c>TechPlanJson.Read</c>: sem <c>key</c>, a chave é derivada da descrição.</summary>
    /// <exception cref="FormatException">O JSON não descreve uma Rule — a mensagem é a do app, com o nome que a tela mostra.</exception>
    public static PlanDoCatalogo LerPlan(string json)
    {
        var raiz = Objeto(json, "O JSON da Rule é inválido", "O JSON não descreve uma Rule (era esperado um objeto).", tolerante: false);
        var descricao = Texto(raiz, "description").Trim();
        if (descricao.Length == 0)
            throw new FormatException("A Rule está sem descrição (campo \"description\").");
        var conteudo = Texto(raiz, "content");
        if (conteudo.Trim().Length == 0)
            throw new FormatException($"A Rule \"{descricao}\" veio sem conteúdo (campo \"content\").");

        // A stack e o tipo (US #687) como o TechPlanJson.Read os lê: a stack normalizada (StacksDoClone), o tipo em
        // minúsculas; vazios são nulos — e não vão ao arquivo.
        var stack = string.Join(", ", Texto(raiz, "stack").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => !s.StartsWith('#')).Select(s => s.ToLowerInvariant()).Distinct());
        var tipo = Texto(raiz, "tipo").Trim().ToLowerInvariant();
        return new PlanDoCatalogo(
            Texto(raiz, "key").Trim() is { Length: > 0 } chave ? chave : ChaveDe(descricao), descricao,
            conteudo.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd(), Texto(raiz, "repository").Trim(),
            Flag(raiz, "onlyWithWorkItem"), Flag(raiz, "isRequired"), Flag(raiz, "isAgentInstruction"),
            stack.Length > 0 ? stack : null, tipo.Length > 0 ? tipo : null);
    }

    /// <summary>A chave fixa do plano: a dele, ou a derivada da descrição (o <c>TechPlanKeys.Do</c>).</summary>
    public static string Chave(PlanDoCatalogo plano) => plano.Key.Trim().Length > 0 ? plano.Key.Trim() : ChaveDe(plano.Description);

    /// <summary>A chave derivada da descrição (o <c>TechPlanKeys.De</c>).</summary>
    public static string ChaveDe(string descricao) => Slug(descricao, "plano");

    /// <summary>
    /// A letra latina acentuada (minúscula) e a base dela — o que a decomposição FormD do git.kit tira. A tabela é
    /// EXPLÍCITA porque a API roda com <c>InvariantGlobalization</c>, e nela o <c>Normalize(FormD)</c> não decompõe: o
    /// "í" de "mínimo" sumia, e a chave saía "build-mnimo-rpido".
    /// </summary>
    private static readonly Dictionary<char, char> SemAcento = new (string Acentuadas, char Base)[]
    {
        ("àáâãäåāăą", 'a'), ("çćĉċč", 'c'), ("ď", 'd'), ("èéêëēĕėęě", 'e'), ("ĝğġģ", 'g'), ("ĥ", 'h'), ("ìíîïĩīĭį", 'i'),
        ("ĵ", 'j'), ("ķ", 'k'), ("ĺļľ", 'l'), ("ñńņň", 'n'), ("òóôõöōŏő", 'o'), ("ŕŗř", 'r'), ("śŝşš", 's'), ("ţť", 't'),
        ("ùúûüũūŭůűų", 'u'), ("ŵ", 'w'), ("ýÿŷ", 'y'), ("źżž", 'z'),
    }.SelectMany(g => g.Acentuadas.Select(c => (c, g.Base))).ToDictionary(p => p.c, p => p.Base);

    /// <summary>
    /// O slug do git.kit (<c>AgentSkillsWriter.Slugify</c>): minúsculas sem acento, letras e números ASCII, e espaço,
    /// hífen ou sublinhado viram um hífen só.
    /// </summary>
    public static string Slug(string nome, string reserva)
    {
        var normalizado = (nome ?? string.Empty).Trim().ToLowerInvariant();
        var sb = new StringBuilder(normalizado.Length);
        foreach (var letra in normalizado)
        {
            var c = SemAcento.TryGetValue(letra, out var semAcento) ? semAcento : letra;
            if (char.IsLetterOrDigit(c) && c < 128)
                sb.Append(c);
            else if ((c == ' ' || c == '-' || c == '_') && sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? reserva : slug;
    }

    // ----- Fluxo -----

    /// <summary>O envelope do fluxo já lido: o nome (a identidade é o slug dele), a descrição e o objeto.</summary>
    public sealed record FluxoLido(string Nome, string Descricao, JsonObject Raiz);

    /// <summary>
    /// Confere o envelope como o <c>FlowCatalogFiles.Ler</c> do app: o formato, a versão, o nome, o desenho e os plans
    /// embutidos legíveis — com as mesmas mensagens.
    /// </summary>
    /// <exception cref="FormatException">O texto não é um fluxo exportado pelo dev.kit.</exception>
    public static FluxoLido LerFluxo(string texto)
    {
        JsonObject raiz;
        try
        {
            raiz = JsonNode.Parse(texto ?? string.Empty) as JsonObject
                   ?? throw new FormatException("O arquivo não descreve um fluxo (era esperado um objeto JSON).");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"O arquivo não é um JSON válido: {ex.Message}");
        }

        if (!string.Equals(Texto(raiz, "formato"), FormatoDoFluxo, StringComparison.Ordinal))
            throw new FormatException($"O arquivo não é um fluxo exportado pelo dev.kit (falta \"formato\": \"{FormatoDoFluxo}\").");
        var versao = raiz["versao"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
        if (versao != VersaoDoFluxo)
            throw new FormatException($"Versão {versao} do arquivo de fluxo não é conhecida por este dev.kit (ele lê a {VersaoDoFluxo}): atualize o dev.kit ou exporte de novo.");
        var nome = Texto(raiz, "nome").Trim();
        if (nome.Length == 0)
            throw new FormatException("O fluxo do arquivo está sem nome (campo \"nome\").");
        if (raiz["desenho"] is not JsonObject desenho)
            throw new FormatException($"O fluxo \"{nome}\" está sem o desenho (campo \"desenho\").");
        if (desenho["nodes"] is not JsonArray)
            throw new FormatException($"O desenho do fluxo \"{nome}\" está sem as etapas (campo \"desenho.nodes\").");
        foreach (var item in (raiz["planos"] as JsonArray ?? []).OfType<JsonObject>())
            if (item["plano"] is JsonObject plano)
                LerPlan(plano.ToJsonString());

        var descricao = Texto(raiz, "descricao").Trim();
        return new FluxoLido(nome, descricao, raiz);
    }

    /// <summary>O envelope regravado indentado (o arquivo que vai no zip), sem mudar o conteúdo.</summary>
    public static string ExportarFluxo(JsonObject raiz) => raiz.ToJsonString(Envelope);

    // ----- Hash (o HashDoCatalogo do git.kit) -----

    /// <summary>O hash da skill: o JSON do <c>SkillJson.Export</c> da skill com os campos de texto livre normalizados.</summary>
    public static string HashDaSkill(SkillDoCatalogo skill)
        => Sha256(ExportarSkill(skill with
        {
            Name = skill.Name.Trim(),
            Description = skill.Description.Trim(),
            Content = Normalizar(skill.Content).TrimEnd(),
            Arguments = Lista(skill.Arguments),
            Repositories = Lista(skill.Repositories),
        }));

    /// <summary>O hash do Tech Plan (com a chave fixa) e o conteúdo em LF.</summary>
    public static string HashDoPlan(PlanDoCatalogo plano) => Sha256(ExportarPlan(plano with { Content = Normalizar(plano.Content) }));

    /// <summary>O hash do fluxo pelo texto do envelope: compacto, com os plans embutidos regravados pelo formato atual.</summary>
    /// <exception cref="FormatException">O texto não é um JSON.</exception>
    public static string HashDoFluxo(string envelope)
    {
        JsonNode? raiz;
        try
        {
            raiz = JsonNode.Parse(envelope);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"O fluxo não é um JSON válido: {ex.Message}");
        }

        if (raiz is JsonObject objeto && objeto["planos"] is JsonArray planos)
        {
            foreach (var item in planos.OfType<JsonObject>())
            {
                if (item["plano"] is not JsonObject plano)
                    continue;
                var lido = LerPlan(plano.ToJsonString());
                item["plano"] = JsonNode.Parse(ExportarPlan(lido with { Content = Normalizar(lido.Content) }));
            }
        }

        return Sha256(raiz?.ToJsonString(Compacto) ?? string.Empty);
    }

    /// <summary>O SHA-256 (hex minúsculo) do texto com as quebras de linha normalizadas.</summary>
    public static string Sha256(string texto) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalizar(texto))));

    /// <summary>CRLF e CR soltos viram LF.</summary>
    public static string Normalizar(string texto) => (texto ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    // ----- Leitura tolerante (o SkillJson.Text/Lines/Flag do app) -----

    private static JsonObject Objeto(string json, string invalido, string naoObjeto, bool tolerante = true)
    {
        JsonNode? raiz;
        try
        {
            raiz = JsonNode.Parse(json ?? string.Empty, documentOptions: tolerante ? Tolerante : default);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{invalido}: {ex.Message}");
        }

        return raiz as JsonObject ?? throw new FormatException(naoObjeto);
    }

    private static string Texto(JsonObject raiz, string campo)
        => raiz[campo] switch
        {
            null => string.Empty,
            JsonValue valor when valor.GetValueKind() == JsonValueKind.String => valor.GetValue<string>(),
            var outro => outro.ToJsonString(),
        };

    private static IReadOnlyList<string> Linhas(JsonObject raiz, string campo)
    {
        IEnumerable<string> bruto = raiz[campo] switch
        {
            JsonArray lista => lista.Select(e => e is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : e?.ToJsonString() ?? string.Empty),
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>().Split('\n'),
            _ => [],
        };

        return bruto.SelectMany(l => l.Split('\n')).Select(l => l.Trim().Trim('\r')).Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool Flag(JsonObject raiz, string campo)
        => raiz[campo] is JsonValue valor && valor.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => bool.TryParse(valor.GetValue<string>(), out var lido) && lido,
            JsonValueKind.Number => valor.TryGetValue<int>(out var n) && n != 0,
            _ => false,
        };

    /// <summary>A lista como o <c>ArgumentList</c> do app a devolve do texto: linhas aparadas e não vazias.</summary>
    private static IReadOnlyList<string> Lista(IReadOnlyList<string> itens)
        => itens.SelectMany(i => i.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Select(i => i.Trim()).Where(i => i.Length > 0).ToArray();
}
