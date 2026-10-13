using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O GERADOR do manifesto do catálogo (US #461) — o porte, para o site, do <c>GeradorDoManifesto</c> do git.kit, que é
/// quem gerava o <c>manifesto.json</c> das releases <c>catalogo-vN</c> antes de o site publicar:
/// <list type="bullet">
/// <item>hash novo é a versão do item + 1 (e o item entra na delta da versão nova do catálogo);</item>
/// <item>o item que sai vai para os retirados; o que volta continua a numeração;</item>
/// <item>nada mudou, o manifesto volta IGUAL (e o site não publica release vazia).</item>
/// </list>
/// A igualdade com o git.kit é travada pelo teste de contrato (<c>ContratoDoManifesto</c>, o mesmo conjunto nos dois
/// repositórios) e pela paridade com o <c>catalogo/base</c> v2: mudar a regra de um lado obriga a mudar o outro.
/// </summary>
public static partial class GeradorDoManifesto
{
    /// <summary>Um item lido: o que entra no manifesto, sem as versões. <paramref name="Tecnologia"/> nula é a derivada do nome.</summary>
    public sealed record ItemLido(
        string Tipo, string Nome, string Hash, string Arquivo, string Descricao, IReadOnlyList<string> Requer, IReadOnlyList<string> Dependencias,
        string? Tecnologia = null);

    /// <summary>O que a geração fez: o manifesto (o mesmo objeto, sem mudança) e as linhas do relatório.</summary>
    public sealed record Resultado(ManifestoDoCatalogoV1 Manifesto, bool Mudou, IReadOnlyList<string> Relatorio);

    /// <summary>O manifesto de um catálogo ainda sem release.</summary>
    public static ManifestoDoCatalogoV1 Vazio { get; } = new(0, 0, [], []);

    /// <summary>As ferramentas cujo token o item cita viram o <c>requer</c> (a mesma lista do git.kit).</summary>
    private static readonly string[] TokensDeFerramenta = ["{DEVCLI}", "{COMPILADOR}", "{COMPILADOR_1006}", "{MSBUILD}", "{AZ}"];

    /// <summary>
    /// Lê o item a partir do CONTEÚDO (o JSON no formato do dev.kit), como o <c>LerItem</c> do git.kit lê o arquivo. O
    /// <paramref name="nome"/> só vale para o fluxo (no git.kit é o nome do arquivo): a skill é pelo <c>name</c> e o plan
    /// pela chave fixa.
    /// </summary>
    /// <exception cref="FormatException">O conteúdo não é um item desse tipo.</exception>
    public static ItemLido Ler(string tipo, string nome, string arquivo, string conteudo, string? tecnologia = null)
    {
        switch (tipo)
        {
            case TiposDeItem.Skill:
            {
                var skill = FormatosDoCatalogo.LerSkill(conteudo);
                return new ItemLido(tipo, skill.Name, FormatosDoCatalogo.HashDaSkill(skill), arquivo, skill.Description, Requer(skill.Content), [], tecnologia);
            }

            case TiposDeItem.Plan:
            {
                var plano = FormatosDoCatalogo.LerPlan(conteudo);
                return new ItemLido(tipo, FormatosDoCatalogo.Chave(plano), FormatosDoCatalogo.HashDoPlan(plano), arquivo, plano.Description, Requer(plano.Content), [], tecnologia);
            }

            case TiposDeItem.Fluxo:
            {
                var raiz = JsonNode.Parse(conteudo) as JsonObject ?? throw new FormatException("o fluxo não é um objeto JSON.");
                var descricao = raiz["descricao"]?.GetValue<string>() is { Length: > 0 } d ? d : raiz["nome"]?.GetValue<string>() ?? string.Empty;
                return new ItemLido(tipo, nome, FormatosDoCatalogo.HashDoFluxo(conteudo), arquivo, descricao.Trim(), [], DependenciasDoFluxo(raiz), tecnologia);
            }

            default:
                throw new FormatException($"Tipo de item desconhecido: {tipo}.");
        }
    }

    /// <summary>
    /// O manifesto novo a partir do <paramref name="anterior"/> e dos itens lidos. Sem mudança nenhuma, devolve o
    /// <paramref name="anterior"/> como está (e <see cref="Resultado.Mudou"/> falso). O <paramref name="versaoDoDevKit"/>
    /// é o <c>minDevKit</c> dos itens novos e alterados (e do manifesto).
    /// </summary>
    public static Resultado Gerar(ManifestoDoCatalogoV1 anterior, IReadOnlyList<ItemLido> lidos, int versaoDoDevKit)
    {
        var proxima = anterior.Versao + 1;
        var relatorio = new List<string>();
        var porChave = anterior.Itens.ToDictionary(i => (i.Tipo, i.Nome));
        var itens = new List<ItemDoCatalogoV1>();
        foreach (var lido in lidos)
        {
            var tecnologia = string.IsNullOrWhiteSpace(lido.Tecnologia) ? Tecnologia(lido.Tipo, lido.Nome) : lido.Tecnologia.Trim();
            if (!porChave.TryGetValue((lido.Tipo, lido.Nome), out var antes))
            {
                // O item que VOLTA depois de retirado continua a numeração: a versão de um item nunca anda para trás.
                var versao = (anterior.Retirados.FirstOrDefault(r => r.Tipo == lido.Tipo && r.Nome == lido.Nome)?.Versao ?? 0) + 1;
                itens.Add(new ItemDoCatalogoV1(lido.Tipo, lido.Nome, versao, lido.Hash, proxima, lido.Arquivo, lido.Descricao, tecnologia,
                    versaoDoDevKit, lido.Requer, lido.Dependencias, [lido.Hash]));
                relatorio.Add($"+ {lido.Tipo}:{lido.Nome} v{versao}");
            }
            else if (antes.Hash != lido.Hash)
            {
                itens.Add(new ItemDoCatalogoV1(lido.Tipo, lido.Nome, antes.Versao + 1, lido.Hash, proxima, lido.Arquivo, lido.Descricao, tecnologia,
                    versaoDoDevKit, lido.Requer, lido.Dependencias, [.. antes.Hashes.Where(h => h != lido.Hash), lido.Hash]));
                relatorio.Add($"~ {lido.Tipo}:{lido.Nome} v{antes.Versao + 1}");
            }
            else if (antes.Arquivo != lido.Arquivo || antes.Descricao != lido.Descricao || antes.Tecnologia != tecnologia
                     || !antes.Requer.SequenceEqual(lido.Requer) || !antes.Dependencias.SequenceEqual(lido.Dependencias))
            {
                // O mesmo conteúdo com outro arquivo, ou dados da vitrine diferentes: não é versão nova do item,
                // mas entra na delta (a máquina precisa do caminho certo).
                itens.Add(antes with { Catalogo = proxima, Arquivo = lido.Arquivo, Descricao = lido.Descricao, Tecnologia = tecnologia, Requer = lido.Requer, Dependencias = lido.Dependencias });
                relatorio.Add($"· {lido.Tipo}:{lido.Nome} (dados da vitrine)");
            }
            else
            {
                itens.Add(antes);
            }
        }

        var presentes = lidos.Select(l => (l.Tipo, l.Nome)).ToHashSet();
        var retirados = anterior.Retirados.Where(r => !presentes.Contains((r.Tipo, r.Nome))).ToList();
        foreach (var saiu in anterior.Itens.Where(i => !presentes.Contains((i.Tipo, i.Nome))))
        {
            retirados.Add(new ItemRetiradoV1(saiu.Tipo, saiu.Nome, saiu.Versao, proxima));
            relatorio.Add($"- {saiu.Tipo}:{saiu.Nome} (retirado)");
        }

        if (relatorio.Count == 0 && retirados.Count == anterior.Retirados.Count)
            return new Resultado(anterior, false, ["Sem mudança: o manifesto continua na versão " + anterior.Versao + "."]);

        var manifesto = new ManifestoDoCatalogoV1(
            proxima, versaoDoDevKit,
            itens.OrderBy(i => i.Tipo, StringComparer.Ordinal).ThenBy(i => i.Nome, StringComparer.Ordinal).ToArray(),
            retirados.OrderBy(r => r.Tipo, StringComparer.Ordinal).ThenBy(r => r.Nome, StringComparer.Ordinal).ToArray());
        return new Resultado(manifesto, true, [$"Catálogo {anterior.Versao} → {proxima}:", .. relatorio]);
    }

    /// <summary>
    /// O grupo da vitrine pelo NOME do item, como no git.kit: o Angular, o .NET, o Git e o Azure DevOps têm skills
    /// próprias; os fluxos e o resto (as que operam o próprio dev.kit) vão para "dev.kit"; os plans, para "Tech Plans".
    /// </summary>
    public static string Tecnologia(string tipo, string nome)
    {
        if (tipo == TiposDeItem.Plan)
            return "Tech Plans";
        if (tipo == TiposDeItem.Fluxo)
            return "Fluxos";
        if (nome.Contains("angular", StringComparison.OrdinalIgnoreCase))
            return "Angular";
        if (nome.Contains("dotnet", StringComparison.OrdinalIgnoreCase) || nome.Contains("net-framework", StringComparison.OrdinalIgnoreCase))
            return ".NET";
        if (Git().IsMatch(nome))
            return "Git";
        if (AzureDevOps().IsMatch(nome))
            return "Azure DevOps";
        return "dev.kit";
    }

    /// <summary>As ferramentas que o texto usa, pelos tokens: <c>{DEVCLI}</c> vira <c>devcli</c>.</summary>
    public static IReadOnlyList<string> Requer(string texto)
        => TokensDeFerramenta.Where(t => texto.Contains(t, StringComparison.Ordinal))
            .Select(t => t.Trim('{', '}').ToLowerInvariant())
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>O que o fluxo usa: as skills das etapas e os Tech Plans embutidos (pela chave fixa).</summary>
    public static IReadOnlyList<string> DependenciasDoFluxo(JsonObject envelope)
    {
        var deps = new SortedSet<string>(StringComparer.Ordinal);
        if (envelope["desenho"]?["nodes"] is JsonArray nos)
        {
            foreach (var no in nos.OfType<JsonObject>())
                if (no["skillName"] is JsonValue v && v.TryGetValue<string>(out var skill) && skill.Trim().Length > 0)
                    deps.Add($"{TiposDeItem.Skill}:{skill.Trim()}");
        }

        foreach (var chave in PlansEmbutidos(envelope))
            deps.Add($"{TiposDeItem.Plan}:{chave}");
        return deps.ToArray();
    }

    /// <summary>As chaves dos Tech Plans embutidos no envelope (<c>planos[]</c>): a importação do fluxo os cria na máquina.</summary>
    public static IReadOnlyList<string> PlansEmbutidos(JsonObject envelope)
        => (envelope["planos"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(item => item["plano"] as JsonObject)
            .Where(plano => plano is not null)
            .Select(plano => FormatosDoCatalogo.Chave(FormatosDoCatalogo.LerPlan(plano!.ToJsonString())))
            .ToArray();

    [GeneratedRegex("commit|cherry|replicar|pull-request|repositorio|conflito|revisar|arquivos-alterados", RegexOptions.IgnoreCase)]
    private static partial Regex Git();

    [GeneratedRegex("workitem|user-story|bug|sprint|pipelines|testplans|tfs|horas|lancamentos|planejar|plano-tecnico|revisao|roi", RegexOptions.IgnoreCase)]
    private static partial Regex AzureDevOps();
}

/// <summary>O arquivo do manifesto como o git.kit o grava: camelCase, indentado, LF e uma quebra no fim.</summary>
public static class ManifestoJson
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serializar(ManifestoDoCatalogoV1 manifesto)
        => FormatosDoCatalogo.Normalizar(JsonSerializer.Serialize(manifesto, Opcoes)) + "\n";

    /// <exception cref="FormatException">O texto não é um manifesto.</exception>
    public static ManifestoDoCatalogoV1 Ler(string texto)
    {
        try
        {
            var manifesto = JsonSerializer.Deserialize<ManifestoDoCatalogoV1>(texto, Opcoes);
            return manifesto is { Itens: not null, Retirados: not null } ? manifesto : throw new FormatException("o manifesto do catálogo está incompleto.");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"O manifesto do catálogo não é um JSON válido: {ex.Message}");
        }
    }
}
