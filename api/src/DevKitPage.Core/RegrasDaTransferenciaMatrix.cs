using System.Text.Json;
using DevKit.Matrix;

namespace DevKitPage.Core;

/// <summary>Mesmo contrato nos produtores local/remoto; responsabilidade transferida não é evidência atendida.</summary>
public static class RegrasDaTransferenciaMatrix
{
    private static readonly JsonSerializerOptions JsonDoSnapshot = new(RegrasMatrix.Json) { NewLine = "\n" };
    public static string Snapshot(ItemDaTransferenciaMatrix item) => JsonSerializer.Serialize(item, JsonDoSnapshot);

    public static ItemDaTransferenciaMatrix Item(TransferenciaDeValidacaoMatrix transferencia)
        => JsonSerializer.Deserialize<ItemDaTransferenciaMatrix>(transferencia.SnapshotDoItem, RegrasMatrix.Json)
            ?? throw new ArgumentException("Retrato do item transferido ausente.");

    public static MatrizMatrix PreservarOmissoes(MatrizMatrix atual, MatrizMatrix? anterior)
        => atual with { Transferencias = atual.Transferencias ?? anterior?.Transferencias };

    public static void Validar(DocumentoMatrix documento)
    {
        if (documento.Matriz is not { Transferencias: { } transferencias } matriz) return;
        RegrasMatrix.Exigir(documento.Projeto.Length > 0 || transferencias.Length == 0, "Transferência exige matriz do projeto.");
        RegrasMatrix.Exigir(transferencias.All(t => t is not null) && transferencias.Select(t => t.Id).Distinct().Count() == transferencias.Length,
            "Transferências inválidas ou com ID duplicado.");
        var ativas = transferencias.Where(t => t.Revogacao is null).ToArray();
        RegrasMatrix.Exigir(ativas.Select(t => t.Item).Distinct().Count() == ativas.Length, "Mais de uma transferência ativa para o item nesta matriz.");
        foreach (var t in transferencias)
        {
            RegrasMatrix.Exigir(RegrasMatrix.Identificador(t.Id) && t.Item > 0 && t.RevisaoDeOrigem > 0
                && RegrasMatrix.Identificador(t.ResponsavelDaFinalizacao) && !string.IsNullOrWhiteSpace(t.Motivo)
                && !string.IsNullOrWhiteSpace(t.Origem) && t.Em != default && t.CelulasDeOrigem is { Length: > 0 },
                "Transferência exige origem, motivo, responsável, revisão e células de origem.");
            var item = Item(t);
            RegrasMatrix.Exigir(item.Numero == t.Item && item.Situacao is "planejado" or "falhou",
                "Só item planejado/falho pode transferir a validação; integrado ou em execução fica preservado.");
            RegrasMatrix.Exigir(t.CelulasDeOrigem.All(c => c.Responsaveis.Any(r => r.Item == t.Item)), "Célula de origem não pertencia ao item.");
            if (t.Revogacao is { } revogacao)
            {
                RegrasMatrix.Exigir(!string.IsNullOrWhiteSpace(revogacao.Motivo) && !string.IsNullOrWhiteSpace(revogacao.Origem)
                    && revogacao.Em != default, "Revogação exige motivo, origem e data; não apague a transferência.");
                continue;
            }
            RegrasMatrix.Exigir(t.CelulasDeOrigem.All(c => matriz.Celulas.Any(n => n.Id == c.Id
                && n.Responsaveis.Any(r => r.Id == t.ResponsavelDaFinalizacao && r.Item is null))), "Transferência ativa precisa das células e do dono formal da finalização.");
            RegrasMatrix.Exigir(!matriz.Celulas.Any(c => c.Responsaveis.Any(r => r.Item == t.Item)),
                "Transferência incompleta: ainda há obrigação atribuída ao item nesta matriz.");
        }
    }

    public static void ConferirRevisao(RevisaoMatrix? anterior, DocumentoMatrix documento)
    {
        if (documento.Matriz is not { } matriz) return;
        var antigas = anterior?.Documento.Matriz?.Transferencias ?? [];
        var novas = matriz.Transferencias ?? [];
        foreach (var antiga in antigas)
        {
            var nova = novas.SingleOrDefault(t => t.Id == antiga.Id);
            RegrasMatrix.Exigir(nova is not null && JsonSerializer.Serialize(nova with { Revogacao = antiga.Revogacao }, RegrasMatrix.Json)
                == JsonSerializer.Serialize(antiga, RegrasMatrix.Json), "Transferência histórica não pode ser apagada ou reescrita.");
            RegrasMatrix.Exigir(antiga.Revogacao is null || nova.Revogacao == antiga.Revogacao, "Revogação histórica é imutável.");
            if (antiga.Revogacao is null && nova.Revogacao is not null)
                RegrasMatrix.Exigir(antiga.CelulasDeOrigem.All(c => matriz.Celulas.Any(n => n.Id == c.Id
                    && c.Responsaveis.Where(r => r.Item == antiga.Item).All(n.Responsaveis.Contains))),
                    "Revogação exige restaurar o vínculo formal com o item, preservando a revisão anterior.");
        }
        foreach (var nova in novas.Where(t => !antigas.Any(a => a.Id == t.Id)))
        {
            RegrasMatrix.Exigir(anterior is not null && nova.RevisaoDeOrigem == anterior.Versao && nova.Revogacao is null,
                "A transferência deve partir da revisão atual da matriz.");
            var origens = anterior.Documento.Matriz!.Celulas.Where(c => c.Responsaveis.Any(r => r.Item == nova.Item)).ToArray();
            RegrasMatrix.Exigir(origens.Length > 0 && JsonSerializer.Serialize(origens, RegrasMatrix.Json)
                == JsonSerializer.Serialize(nova.CelulasDeOrigem, RegrasMatrix.Json), "As células de origem não correspondem à revisão conferida.");
            foreach (var origem in origens)
            {
                var novaCelula = matriz.Celulas.SingleOrDefault(c => c.Id == origem.Id);
                RegrasMatrix.Exigir(novaCelula is not null && JsonSerializer.Serialize(novaCelula with { Responsaveis = origem.Responsaveis }, RegrasMatrix.Json)
                    == JsonSerializer.Serialize(origem, RegrasMatrix.Json), "A transferência preserva critérios, origem e evidência esperada da célula.");
                RegrasMatrix.Exigir(origem.Responsaveis.Where(r => r.Item != nova.Item).All(r => novaCelula.Responsaveis.Contains(r)),
                    "A transferência não pode retirar outros responsáveis.");
            }
        }
    }

    public static void ConferirFonte(TransferenciaDeValidacaoMatrix transferencia, IReadOnlyList<ItemDaTransferenciaMatrix> itens)
    {
        if (transferencia.Revogacao is not null) return;
        var item = itens.SingleOrDefault(i => i.Numero == transferencia.Item);
        // O JSON antigo pode ter CRLF de Windows; as quebras do texto do item estão escapadas e permanecem distintas.
        RegrasMatrix.Exigir(item is not null && Snapshot(item) == transferencia.SnapshotDoItem?.Replace("\r\n", "\n", StringComparison.Ordinal),
            "O item mudou desde a transferência: releia e revogue/replaneje explicitamente, sem reinterpretar a história.");
        RegrasMatrix.Exigir(!itens.Any(i => i.Numero != item.Numero && i.Dependencias.Contains(item.Numero)),
            "Outro item depende da validação final: replaneje essa dependência explicitamente antes de transferir.");
        if (!string.IsNullOrWhiteSpace(item.Design))
        {
            using var design = JsonDocument.Parse(item.Design);
            RegrasMatrix.Exigir(!design.RootElement.TryGetProperty("AlteraTela", out var altera) || altera.ValueKind != JsonValueKind.True,
                "Implementação de tela/proposta não é validação final transferível.");
        }
    }

    public static EdicaoMatrix Preparar(RevisaoMatrix revisao, ItemDaTransferenciaMatrix item, string responsavel, string motivo, string origem, DateTimeOffset em)
    {
        var matriz = revisao.Documento.Matriz ?? throw new ArgumentException("Selecione uma matriz existente.");
        RegrasMatrix.Exigir(!(matriz.Transferencias ?? []).Any(t => t.Item == item.Numero && t.Revogacao is null), "O item já tem transferência ativa; consulte o histórico.");
        var celulas = matriz.Celulas.Where(c => c.Responsaveis.Any(r => r.Item == item.Numero)).ToArray();
        var transferencia = new TransferenciaDeValidacaoMatrix($"item-{item.Numero}-finalizacao-r{revisao.Versao}", item.Numero, revisao.Versao,
            responsavel, motivo, origem, Snapshot(item), em, celulas);
        return new(revisao.Versao, revisao.Documento with { Matriz = matriz with
        {
            Transferencias = [.. matriz.Transferencias ?? [], transferencia],
            Celulas = matriz.Celulas.Select(c => celulas.Any(a => a.Id == c.Id) ? c with
                { Responsaveis = c.Responsaveis.Where(r => r.Item != item.Numero).Append(new ResponsavelMatrix(responsavel)).DistinctBy(r => r.Id).ToArray() } : c).ToArray()
        }});
    }
}
