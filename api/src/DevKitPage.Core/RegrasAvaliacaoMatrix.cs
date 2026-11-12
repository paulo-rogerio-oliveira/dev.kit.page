using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevKit.Matrix;

namespace DevKitPage.Core;

/// <summary>Mesmas regras no host e API. O recibo é emitido antes da leitura; resultados nunca o substituem.</summary>
public static class RegrasAvaliacaoMatrix
{
    public static string IdDoEvento(string evento, string matriz, string sessaoAvaliada)
        => "e-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessaoAvaliada + "\n" + evento + "\n" + matriz)))[..48];

    public static void ValidarEvento(DocumentoMatrix d, EventoMatrix e)
    {
        RegrasMatrix.Exigir(d.Id == IdDoEvento(e.Evento, e.Matriz, e.SessaoAvaliada) && RegrasMatrix.Identificador(e.Matriz)
            && e.Evento is { Length: > 8 } && e.Evento.StartsWith("gatilho:", StringComparison.Ordinal) && long.TryParse(e.Evento[8..], out var execucao) && execucao > 0,
            "Recibo exige identidade persistida do evento.");
        RegrasMatrix.Exigir(e.Revisao > 0 && e.RevisaoEfetiva > 0 && e.RevisaoEfetiva <= e.Revisao
            && e.RevisaoDoContexto >= e.RevisaoEfetiva && e.RevisaoDoContexto <= e.Revisao
            && e.Avaliador > 0 && RegrasMatrix.Identificador(e.SessaoAvaliada)
            && !string.IsNullOrWhiteSpace(e.Maquina), "Recibo exige host, avaliador, sessão e revisões do recorte.");
        RegrasMatrix.Exigir(e.Responsaveis is { Length: > 0 } && e.Responsaveis.All(RegrasMatrix.Identificador)
            && e.Celulas is not null && e.Celulas.All(RegrasMatrix.Identificador)
            && e.Dependencias is not null && e.Dependencias.All(RegrasMatrix.Identificador), "Recorte inválido.");
        RegrasMatrix.Exigir(e.Shas is { Count: > 0 } && e.Shas.All(s => !string.IsNullOrWhiteSpace(s.Key)
            && s.Value is { Length: 40 or 64 } && s.Value.All(char.IsAsciiHexDigit)), "Recibo exige SHAs completos.");
        RegrasMatrix.Exigir(EntregaConfere(e.RevisaoDaEntrega, e.Shas), "Revisão imutável da entrega não confere com os SHAs do evento.");
    }

    public static bool EntregaConfere(string json, IReadOnlyDictionary<string, string> shas)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (!raiz.GetProperty("Conferencia").GetProperty("Ok").GetBoolean()) return false;
            var repos = raiz.GetProperty("Repositorios").EnumerateArray().ToArray();
            return shas.Count > 0 && repos.Length == shas.Count && shas.All(s =>
                repos.Count(r => r.GetProperty("Name").GetString() == s.Key
                    && r.TryGetProperty("Delivery", out var entrega) && entrega.ValueKind == JsonValueKind.Object
                    && string.Equals(entrega.GetProperty("Head").GetString(), s.Value, StringComparison.OrdinalIgnoreCase)) == 1);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return false; }
    }

    public static bool MesmosShas(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        => a.Count == b.Count && a.All(s => b.TryGetValue(s.Key, out var sha) && string.Equals(sha, s.Value, StringComparison.OrdinalIgnoreCase));

    public static void ConferirVinculo(RegistroMatrix r, EventoMatrix? e)
    {
        RegrasMatrix.Exigir(r.Tipo == "avaliacao" && e is not null && r.Evento == e.Evento
            && r.SessaoAvaliada == e.SessaoAvaliada && r.Avaliador == e.Avaliador
            && r.RevisaoDaDefinicao == e.Revisao && r.RevisaoEfetiva == e.RevisaoEfetiva
            && r.RevisaoDoContexto == e.RevisaoDoContexto && r.RevisaoDaEntrega == e.RevisaoDaEntrega
            && e.Responsaveis.Contains(r.Responsavel) && e.Celulas.Contains(r.Celula)
            && MesmosShas(r.Shas, e.Shas), "Resultado sem vínculo verificável ao evento/recorte/SHAs fornecidos pelo host.");
    }

    public static string MotivoDaObsolescencia(RegistroMatrix r, IReadOnlyList<RevisaoMatrix> historico)
    {
        if (r.Tipo == "avaliacao" && r.Evento.Length == 0) return "Avaliação anterior sem vínculo verificável ao evento do host.";
        var origem = historico.FirstOrDefault(h => h.Versao == r.RevisaoDaDefinicao);
        if (origem?.Documento.Matriz is null) return "Revisão da definição indisponível.";
        var antes = origem;
        foreach (var depois in historico.Where(h => h.Versao > origem.Versao).OrderBy(h => h.Versao))
        {
            if (RegrasMatrix.Comparar(antes.Documento, depois.Documento).Celulas.Contains(r.Celula))
                return $"Obrigação ou atribuição da célula alterada na revisão {depois.Versao}.";
            antes = depois;
        }
        return "";
    }

    public static void ConferirLeitura(EventoMatrix e, IReadOnlyList<RevisaoMatrix> historico)
    {
        var origem = historico.FirstOrDefault(h => h.Versao == e.Revisao)
            ?? throw new InvalidOperationException("Revisão do evento indisponível.");
        var antes = origem;
        foreach (var depois in historico.Where(h => h.Versao > origem.Versao).OrderBy(h => h.Versao))
        {
            var relevantes = antes.Documento.Matriz!.Celulas.Concat(depois.Documento.Matriz!.Celulas)
                .Where(c => e.Celulas.Contains(c.Id) || e.Dependencias.Contains(c.Id)
                    || c.Responsaveis.Any(r => e.Responsaveis.Contains(r.Id))).Select(c => c.Id);
            RegrasMatrix.Exigir(!RegrasMatrix.Comparar(antes.Documento, depois.Documento).Celulas.Intersect(relevantes).Any(),
                $"Recorte obsoleto: obrigação/atribuição mudou durante a leitura (r{depois.Versao}); solicite novo evento.");
            antes = depois;
        }
    }

    public static string EstadoDaCelula(CelulaMatrix celula, IReadOnlyList<RevisaoMatrix> registros, IReadOnlyList<RevisaoMatrix> historico)
    {
        var lista = registros.Where(x => x.Documento.Registro is { Tipo: "avaliacao" } r && r.Celula == celula.Id
                && celula.Responsaveis.Any(d => d.Id == r.Responsavel))
            .GroupBy(x => (x.Documento.Registro!.Responsavel, x.Documento.Registro.Avaliador))
            .Select(g => g.OrderBy(x => x.Versao).Last().Documento.Registro!).ToArray();
        lista = lista.Where(r => r.Evento.Length > 0 || !lista.Any(v => v.Responsavel == r.Responsavel && v.Evento.Length > 0)).ToArray();
        if (lista.Length == 0) return registros.Any(x => x.Documento.Registro?.Celula == celula.Id) ? "A avaliar" : "";
        if (lista.Any(r => MotivoDaObsolescencia(r, historico).Length > 0)) return "A avaliar · revisão alterada";
        if (celula.Responsaveis.Any(d => !lista.Any(r => r.Responsavel == d.Id))) return "A avaliar";
        if (lista.Any(r => r.Resultado == "nao-atendido")) return "Não atendido";
        if (lista.Any(r => r.Resultado == "parcial")) return "Parcial";
        return celula.Aplicavel && lista.All(r => r.Resultado == "atendido") ? "Comprovado" : "A avaliar";
    }
}
