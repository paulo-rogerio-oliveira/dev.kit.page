using System.Text.Json;
using DevKit.Matrix;

namespace DevKitPage.Core;

/// <summary>Validação compartilhada pelos leitores/editores locais. Nenhuma edição concede portões.</summary>
public static class RegrasMatrix
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static void Exigir([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condicao, string mensagem)
    {
        if (!condicao) throw new ArgumentException(mensagem);
    }

    public static bool Identificador(string? id) => id is { Length: > 0 and <= 100 }
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

    public static void Validar(DocumentoMatrix d)
    {
        ArgumentNullException.ThrowIfNull(d);
        Exigir(d.Contrato == 1, "Contrato matrix desconhecido; atualize o cliente sem sobrescrever a definição.");
        Exigir(Identificador(d.Aplicacao) && Identificador(d.Id), "Aplicação e id devem ser estáveis (até 100 caracteres).");
        Exigir(d.Projeto is not null && (d.Projeto.Length == 0 || Identificador(d.Projeto)), "Projeto inválido.");
        Exigir(new object?[] { d.Mapa, d.Matriz, d.Registro, d.Vinculo, d.Evento }.Count(x => x is not null) == 1,
            "Informe exatamente um documento do tipo solicitado.");
        switch (d.Tipo)
        {
            case "mapa" when d.Mapa is { } m:
                Exigir(d.Id == d.Aplicacao, "O id do mapa é o da aplicação.");
                Texto(m.Nome, "nome");
                Exigir(m.Repositorios is { Length: > 0 } && m.Repositorios.All(r => !string.IsNullOrWhiteSpace(r))
                    && m.Repositorios.Distinct(StringComparer.OrdinalIgnoreCase).Count() == m.Repositorios.Length, "Informe repositórios distintos.");
                Eixos(m.Componentes); Eixos(m.Layers);
                Exigir(m.Relacoes is not null && m.Relacoes.All(r => r is not null), "Informe relações válidas (lista vazia quando não houver).");
                Unicos(m.Relacoes!.Select(r => r.Id));
                foreach (var r in m.Relacoes)
                {
                    Exigir(m.Componentes.Any(c => c.Id == r.Origem) && m.Componentes.Any(c => c.Id == r.Destino), "Relação aponta componente inexistente.");
                    Exigir(r.Layers is { Length: > 0 } && r.Layers.All(l => m.Layers.Any(e => e.Id == l)), "Relação aponta layer inexistente.");
                    Texto(r.Gatilho, "gatilho"); Texto(r.Motivo, "motivo"); Texto(r.Propagacao, "propagação"); Texto(r.Verificacao, "verificação");
                }
                break;
            case "matriz" when d.Matriz is { } m:
                Exigir(m.RevisaoDoMapa > 0, "Fixe uma revisão existente do mapa.");
                Eixos(m.Componentes); Eixos(m.Layers);
                Exigir(m.Celulas is not null && m.Celulas.All(c => c is not null), "Informe células válidas.");
                Unicos(m.Celulas!.Select(c => c.Id));
                Exigir(m.Celulas.Select(c => (c.Componente, c.Layer)).Distinct().Count() == m.Celulas.Length, "Componente × layer duplicado.");
                foreach (var c in m.Celulas)
                {
                    Exigir(m.Componentes.Any(e => e.Id == c.Componente) && m.Layers.Any(e => e.Id == c.Layer), "Célula aponta eixo inexistente.");
                    Exigir(c.Obrigacao is "alterar" or "preservar" or "verificar", "Obrigação: alterar, preservar ou verificar.");
                    Texto(c.Justificativa, "justificativa"); Texto(c.Origem, "origem");
                    Texto(c.Criterio, "critério"); Texto(c.EvidenciaEsperada, "evidência esperada");
                    Exigir(c.Responsaveis is not null && c.Responsaveis.All(r => r is not null) && (!c.Aplicavel || c.Responsaveis.Length > 0), "Célula aplicável precisa de responsável válido.");
                    Unicos(c.Responsaveis!.Select(r => r.Id));
                    Exigir(c.Responsaveis.All(r => r.Item is null or > 0), "Item deve ser positivo.");
                }
                Exigir(m.Coberturas is null || m.Coberturas.All(c => c is not null && Identificador(c.Relacao)
                    && m.Celulas.Any(x => x.Id == c.Celula && x.Responsaveis.Length > 0)), "Relação exige obrigação e dono explícitos.");
                break;
            case "registro" when d.Registro is { } r:
                Exigir(r.Evento is not null && r.SessaoAvaliada is not null && r.RevisaoDaEntrega is not null, "Metadados do resultado inválidos.");
                Exigir(Identificador(r.Celula) && Identificador(r.Responsavel) && r.RevisaoDaDefinicao > 0, "Identifique célula, responsável e revisão.");
                Exigir(r.Tipo is "implementacao" or "avaliacao", "Registro: implementação ou avaliação.");
                Exigir(r.Resultado is "atendido" or "parcial" or "nao-atendido" or "nao-aplicavel", "Resultado inválido.");
                Texto(r.Justificativa, "justificativa");
                Exigir(r.Evidencias is not null && r.Evidencias.All(e => !string.IsNullOrWhiteSpace(e)), "Evidências inválidas.");
                Exigir(r.Shas is not null && r.Shas.All(s => !string.IsNullOrWhiteSpace(s.Key) && s.Value is { Length: 40 or 64 } && s.Value.All(char.IsAsciiHexDigit)), "SHAs completos por repositório.");
                Exigir(r.Resultado == "nao-aplicavel" || r.Resultado == "nao-atendido" || r.Evidencias!.Length > 0, "Resultado precisa de evidência.");
                Exigir(r.Tipo != "avaliacao" || r.Shas!.Count > 0, "Avaliação precisa dos SHAs avaliados.");
                break;
            case "evento" when d.Evento is { } evento:
                RegrasAvaliacaoMatrix.ValidarEvento(d, evento);
                break;
            case "vinculo" when d.Vinculo is { } v:
                Exigir(Identificador(v.Responsavel) && Identificador(v.Sessao) && v.Item is null or > 0, "Vínculo inválido.");
                break;
            default: throw new ArgumentException("Tipo e conteúdo do documento incompatíveis.");
        }
        Exigir(JsonSerializer.SerializeToUtf8Bytes(d, Json).Length <= 512 * 1024, "Documento passa de 512 KB.");
    }

    public static MudancaMatrix Comparar(DocumentoMatrix? antes, DocumentoMatrix depois)
    {
        var a = antes?.Matriz?.Celulas ?? [];
        var b = depois.Matriz?.Celulas ?? [];
        var componentes = EixosDiferentes(antes?.Matriz?.Componentes ?? [], depois.Matriz?.Componentes ?? []);
        var layers = EixosDiferentes(antes?.Matriz?.Layers ?? [], depois.Matriz?.Layers ?? []);
        var relacoes = (antes?.Matriz?.Coberturas ?? []).Except(depois.Matriz?.Coberturas ?? [])
            .Concat((depois.Matriz?.Coberturas ?? []).Except(antes?.Matriz?.Coberturas ?? [])).Select(c => c.Celula).ToHashSet();
        var ids = a.Select(c => c.Id).Union(b.Select(c => c.Id)).Where(id => relacoes.Contains(id) ||
            JsonSerializer.Serialize(a.FirstOrDefault(c => c.Id == id), Json) != JsonSerializer.Serialize(b.FirstOrDefault(c => c.Id == id), Json)
            || a.Concat(b).Any(c => c.Id == id && (componentes.Contains(c.Componente) || layers.Contains(c.Layer)))).ToArray();
        // A revisão do mapa identifica a origem; as obrigações efetivas são os snapshots de células/eixos.
        // Um eixo sem células ou uma reordenação não muda o recorte de nenhuma task.
        return new(ids, a.Concat(b).Where(c => ids.Contains(c.Id)).SelectMany(c => c.Responsaveis).Select(r => r.Id).Distinct().ToArray());
    }

    private static HashSet<string> EixosDiferentes(EixoMatrix[] antes, EixoMatrix[] depois)
        => antes.Except(depois).Concat(depois.Except(antes)).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

    public static void ValidarReferencia(DocumentoMatrix d, Func<string, string, int, RevisaoMatrix?> ler)
    {
        if (d.Matriz is { } m)
        {
            var mapa = ler("mapa", d.Aplicacao, m.RevisaoDoMapa)?.Documento.Mapa;
            Exigir(mapa is not null, "Revisão do mapa não encontrada nesta aplicação.");
            Exigir((m.Coberturas ?? []).All(c => mapa!.Relacoes.Any(r => r.Id == c.Relacao)), "Cobertura aponta relação inexistente no mapa.");
        }
        if (d.Registro is { } r)
        {
            var celula = ler("matriz", d.Id, r.RevisaoDaDefinicao)?.Documento.Matriz?.Celulas.FirstOrDefault(c => c.Id == r.Celula);
            Exigir(celula is not null && celula.Responsaveis.Any(a => a.Id == r.Responsavel), "Célula/responsável não pertence à revisão informada.");
        }
        if (d.Vinculo is { } v)
        {
            var matriz = ler("matriz", d.Id, 0)?.Documento.Matriz;
            Exigir(matriz is not null && matriz.Celulas.Any(c => c.Responsaveis.Any(r => r.Id == v.Responsavel && r.Item == v.Item)), "Responsável/item não pertence à matriz.");
        }
        if (d.Evento is { } evento)
        {
            var matriz = ler("matriz", evento.Matriz, evento.Revisao)?.Documento;
            Exigir(matriz?.Matriz is not null && matriz.Projeto == d.Projeto, "Evento exige a matriz/revisão do mesmo escopo.");
            Exigir(evento.Celulas.Order().SequenceEqual(matriz!.Matriz!.Celulas
                .Where(c => c.Responsaveis.Any(r => evento.Responsaveis.Contains(r.Id))).Select(c => c.Id).Order()),
                "Recibo difere do recorte atribuído na revisão.");
        }
        if (d.Registro is { Evento.Length: > 0 } registro)
        {
            var recibo = ler("evento", RegrasAvaliacaoMatrix.IdDoEvento(registro.Evento, d.Id, registro.SessaoAvaliada), 0)?.Documento.Evento;
            RegrasAvaliacaoMatrix.ConferirVinculo(registro, recibo);
        }
    }

    public static bool RegistroVigente(RegistroMatrix registro, RevisaoMatrix origem, RevisaoMatrix atual)
        => registro.RevisaoDaDefinicao == origem.Versao && origem.Documento.Aplicacao == atual.Documento.Aplicacao
            && origem.Documento.Id == atual.Documento.Id && !Comparar(origem.Documento, atual.Documento).Celulas.Contains(registro.Celula);

    public static void ValidarEstabilidade(IEnumerable<RevisaoMatrix> historico, DocumentoMatrix d)
    {
        if (d.Matriz is not { } matriz) return;
        foreach (var antiga in historico.SelectMany(r => r.Documento.Matriz?.Celulas ?? []))
            foreach (var nova in matriz.Celulas)
            {
                Exigir(antiga.Id != nova.Id || (antiga.Componente == nova.Componente && antiga.Layer == nova.Layer), "ID estável não pode mudar de componente/layer.");
                Exigir(antiga.Componente != nova.Componente || antiga.Layer != nova.Layer || antiga.Id == nova.Id, "Preserve o ID da célula ao editar/reintroduzir o mesmo componente/layer.");
            }
    }

    private static void Texto(string? t, string campo) => Exigir(!string.IsNullOrWhiteSpace(t) && t.Length <= 16000, $"Informe {campo} (até 16000 caracteres).");
    private static void Eixos(EixoMatrix[]? e)
    {
        Exigir(e is { Length: > 0 } && e.All(x => x is not null), "Informe componentes e layers configuráveis válidos.");
        Unicos(e!.Select(x => x.Id));
        foreach (var x in e) { Texto(x.Nome, "nome do eixo"); Exigir(x.Orientacao is not null, "Orientação inválida."); }
    }
    private static void Unicos(IEnumerable<string> valores)
    {
        var ids = valores.ToArray();
        Exigir(ids.All(Identificador) && ids.Distinct().Count() == ids.Length, "IDs inválidos ou duplicados.");
    }
}
