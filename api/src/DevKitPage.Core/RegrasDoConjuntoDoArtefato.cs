using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

public static partial class RegrasDosArtefatosLocais
{
    private static string ValidarDetalhes(ArtefatoLocalNoRetratoV1 a)
    {
        if (a.Detalhes is not { } d) return "";
        if (a.Fontes.Count == 0 || d.PoliticaDePins is not ("conteudo-identico" or "declarada-por-consumidor" or "sha-do-projeto")
            || d.Pins is null || d.Exclusoes is null || d.Pins.Count > 100 || d.Exclusoes.Count > 100
            || a.Alvos.Any(t => t.Saida is null)
            || a.Alvos.Select(t => t.Saida).Distinct(StringComparer.OrdinalIgnoreCase).Count() != a.Alvos.Count)
            return "Artefato local: detalhes completos exigem fontes, política, listas limitadas e saídas distintas.";
        var fontes = a.Fontes.ToDictionary(f => f.Id);
        if (d.Pins.Any(p => p is null || !Hex(p.Consumidor, 64) || !Hex(p.Repositorio, 64)
            || !fontes.ContainsKey(p.Consumidor) || !fontes.ContainsKey(p.Repositorio)
            || NomeInvalido(p.Papel) || !Sha(p.Declarado) || !Sha(p.Capturado) || fontes[p.Repositorio].Sha != p.Capturado
            || !Sha(p.ArvoreDeclarada) || !Sha(p.ArvoreCapturada) || !Relativo(p.Raiz)
            || p.Politica is not ("conteudo-identico" or "sha-do-projeto")
            || (p.Politica == "conteudo-identico" && p.ArvoreDeclarada != p.ArvoreCapturada))
            || d.Pins.Select(p => (p.Consumidor, p.Papel)).Distinct().Count() != d.Pins.Count)
            return "Artefato local: pin exige fontes formais, SHA capturado, árvores, raiz e política coerentes.";
        if ((d.PoliticaDePins == "conteudo-identico" && d.Pins.Any(p => p.Politica != "conteudo-identico"))
            || (d.PoliticaDePins == "declarada-por-consumidor" && d.Pins.All(p => p.Politica == "conteudo-identico")))
            return "Artefato local: política do conjunto contradiz a política registrada por consumidor.";
        if (d.Exclusoes.Any(e => e is null || !Hex(e.Repositorio, 64) || !fontes.ContainsKey(e.Repositorio) || !Relativo(e.Caminho)
            || string.IsNullOrWhiteSpace(e.Motivo) || Longo(e.Motivo)
            || a.Alvos.Any(t => t.Repositorio == e.Repositorio && t.Nome == e.Caminho))
            || d.Exclusoes.Select(e => (e.Repositorio, e.Caminho)).Distinct().Count() != d.Exclusoes.Count)
            return "Artefato local: exclusão exige fonte/caminho/motivo e não pode contradizer alvos selecionados.";
        if (d.Opcoes is { } o && (!Hex(o.RepositorioPrincipal, 64) || !fontes.ContainsKey(o.RepositorioPrincipal)
            || o.FormatoSolicitado is not ("" or "pacote" or "preview") || o.PreviewRemoto is null
            || new[] { o.PreviewRemoto.App, o.PreviewRemoto.Grupo, o.PreviewRemoto.Assinatura, o.PreviewRemoto.Tenant }
                .Any(v => v is null || v.Length > 256 || v.Any(char.IsControl))))
            return "Artefato local: opções exigem referência/formato e identificadores do destino capturado, sem credenciais.";
        return "";
    }

    private static IEnumerable<(string Campo, string? Texto)> TextosDosDetalhes(DadosDoConjuntoNoRetratoV1 d, string campo)
    {
        foreach (var (p, i) in d.Pins.Select((p, i) => (p, i)))
        {
            yield return ($"{campo}.pins[{i}].papel", p.Papel);
            yield return ($"{campo}.pins[{i}].raiz", p.Raiz);
        }
        foreach (var (e, i) in d.Exclusoes.Select((e, i) => (e, i)))
        {
            yield return ($"{campo}.exclusoes[{i}].caminho", e.Caminho);
            yield return ($"{campo}.exclusoes[{i}].motivo", e.Motivo);
        }
        if (d.Opcoes?.PreviewRemoto is { } destino)
            foreach (var (nome, valor) in new[] { ("app", destino.App), ("grupo", destino.Grupo), ("assinatura", destino.Assinatura), ("tenant", destino.Tenant) })
                yield return ($"{campo}.opcoes.previewRemoto.{nome}", valor);
    }

    private static bool Relativo(string? valor) => !string.IsNullOrWhiteSpace(valor) && valor.Length <= 1024
        && !valor.Any(char.IsControl) && !valor.Contains(':') && !valor.Contains('\\')
        && !valor.Split('/').Any(p => p is "" or "." or "..");
}
