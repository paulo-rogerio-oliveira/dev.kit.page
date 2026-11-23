using System.Security.Cryptography;
using System.Text;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

public static class RegrasDaFinalizacao
{
    public static string ValidarFormato(ReciboDaFinalizacaoV1 r, string sessao)
    {
        if (r.Contrato != 1 || r.Execucao <= 0 || r.RevisaoDaPolitica <= 0 || r.Sessao != sessao
            || !RegrasDosProjetos.SlugValido(r.Projeto)
            || new[] { r.Branch, r.Destino, r.Fluxo, r.RevisaoDoFluxo, r.Agente, r.Modelo }.Any(v => string.IsNullOrWhiteSpace(v) || v.Length > 300)
            || r.Shas is not { Count: > 0 and <= 100 }
            || r.Shas.Any(p => p.Key.Length != 64 || !p.Key.All(Uri.IsHexDigit) || p.Value is not { Length: 40 } || !p.Value.All(Uri.IsHexDigit)))
            return "Recibo da finalização sem identidade, revisão ou composição confirmada.";
        return string.Empty;
    }

    public static string IdDoRepositorio(string origem)
    {
        var normal = origem.Trim().TrimEnd('/', '\\').ToLowerInvariant();
        if (normal.StartsWith("git@", StringComparison.Ordinal) && normal.IndexOf(':') is var separador && separador > 4)
            normal = "https://" + normal[4..separador] + "/" + normal[(separador + 1)..];
        if (normal.StartsWith("ssh://git@", StringComparison.Ordinal)) normal = "https://" + normal[10..];
        if (normal.EndsWith(".git", StringComparison.Ordinal)) normal = normal[..^4];
        // Mesma identidade portátil usada pelo ModeloDeBranches/GitHubRepo no host.
        if (Uri.TryCreate(normal, UriKind.Absolute, out var uri) && uri.Host.Length > 0)
        {
            var partes = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (partes.Length >= 2) normal = $"https://{uri.Host}/{partes[^2]}/{partes[^1]}";
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normal)));
    }

    public static string Conferir(ReciboDaFinalizacaoV1? recibo, string sessao, EnvioDeProjetoV1? projeto)
    {
        if (recibo is null || projeto is null) return "Sessão sem recibo da finalização confirmado pelo host.";
        var formato = ValidarFormato(recibo, sessao);
        if (formato.Length > 0) return formato;
        var politica = projeto.PoliticaDeFinalizacao;
        if (politica is not { Politica: "finalizacao-unica" } || politica.Slug != recibo.Projeto
            || projeto.Slug != recibo.Projeto || projeto.Branch != recibo.Branch || projeto.DestinoDaPr != recibo.Destino
            || politica.Revisao != recibo.RevisaoDaPolitica || politica.IdentidadeDoFluxo != recibo.Fluxo
            || politica.RevisaoDoFluxo != recibo.RevisaoDoFluxo
            || politica.ModeloDaFinalizacao?.Agente != recibo.Agente || politica.ModeloDaFinalizacao?.Modelo != recibo.Modelo)
            return "Recibo da finalização obsoleto ou divergente do projeto/política.";
        static string Nome(string r)
        {
            var nome = r.TrimEnd('/', '\\').Split('/', '\\', ':').Last();
            return nome.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? nome[..^4] : nome;
        }
        var esperados = projeto.Repositorios.Where(r => projeto.Itens.Any(i => i.Repositorios.Count == 0
            || i.Repositorios.Any(a => Nome(a).Equals(Nome(r), StringComparison.OrdinalIgnoreCase))))
            .Select(IdDoRepositorio).ToHashSet(StringComparer.Ordinal);
        if (esperados.Count == 0 || !esperados.SetEquals(recibo.Shas.Keys))
            return "Recibo da finalização não corresponde aos repositórios da composição.";
        return string.Empty;
    }
}
