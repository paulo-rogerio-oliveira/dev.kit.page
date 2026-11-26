using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>A edição é calculada sobre a versão lida pelo servidor. Os demais campos permanecem no snapshot.</summary>
public static class EdicaoRemotaDoItem
{
    public static EnvioDeProjetoV1 Preparar(ProjetoV1 projeto, int numero, EdicaoRemotaDoItemV1 edicao, DateTimeOffset agora)
    {
        if (projeto.Situacao is ProjetosV1.Fechando or ProjetosV1.Concluido)
            throw new ArgumentException("Projeto encerrado ou em fechamento não aceita edição do plano.");
        var anterior = projeto.Itens.SingleOrDefault(i => i.Numero == numero);
        if (numero < 0 || numero > 0 && anterior is null)
            throw new ArgumentException("Item não pertence ao projeto.");
        if (anterior is not null && anterior.Situacao != ProjetosV1.ItemPlanejado)
            throw new ArgumentException("Item já iniciado conserva seu snapshot; somente item planejado pode ser editado.");
        if (edicao.Dependencias is null || edicao.Componentes is null || edicao.Titulo is null || edicao.Descricao is null
            || edicao.Fluxo is null || edicao.Agente is null || edicao.Modelo is null)
            throw new ArgumentException("Informe todos os campos da edição.");
        if (edicao.Componentes.Count > 100 || edicao.Componentes.Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 200))
            throw new ArgumentException("Componentes inválidos (até 100 nomes de 200 caracteres).");
        // O site escolhe somente referências e pares já presentes no cadastro sincronizado.
        var fluxos = projeto.Itens.Select(i => i.Fluxo).Append(projeto.FluxoPadrao ?? "").Append("");
        if (!fluxos.Contains(edicao.Fluxo, StringComparer.Ordinal))
            throw new ArgumentException("Fluxo fora do cadastro sincronizado. Sincronize as escolhas do host.");
        var modelos = projeto.Itens.Select(i => (i.Agente ?? "", i.Modelo ?? ""))
            .Concat((projeto.Modelos ?? []).Select(m => (m.Agente, m.Modelo)))
            .Append((projeto.Agente ?? "", projeto.Modelo ?? "")).Append(("", ""));
        if (!modelos.Contains((edicao.Agente, edicao.Modelo)))
            throw new ArgumentException("Agente/modelo fora do cadastro sincronizado.");
        var item = (anterior ?? new ItemDoProjetoV1(projeto.Itens.Select(i => i.Numero).DefaultIfEmpty().Max() + 1,
            "", "", "", [], [], [], 0, 0, ProjetosV1.ItemPlanejado)) with
        {
            Titulo = edicao.Titulo.Trim(), Descricao = edicao.Descricao.Trim(), Fluxo = edicao.Fluxo,
            Agente = edicao.Agente, Modelo = edicao.Modelo, Dependencias = edicao.Dependencias.Distinct().Order().ToArray(),
            Componentes = edicao.Componentes.Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Prioridade = edicao.Prioridade, AtualizadoEm = agora,
        };
        var itens = anterior is null ? projeto.Itens.Append(item).ToArray()
            : projeto.Itens.Select(i => i.Numero == numero ? item : i).ToArray();
        var erro = RegrasDosProjetos.ValidarItens(itens);
        if (erro.Length > 0) throw new ArgumentException(erro);
        if (anterior is null || !item.Dependencias.SequenceEqual(anterior.Dependencias) || !item.Componentes.SequenceEqual(anterior.Componentes))
        {
            // Mesma regra de ondas do desktop/devcli, espelhada e conferida por teste de contrato.
            var montado = PlanoEmOndas.Montar(itens);
            if (!montado.Ok) throw new ArgumentException(montado.Erro);
            if (projeto.Itens.Any(i => i.Situacao != ProjetosV1.ItemPlanejado
                && montado.Itens.Single(n => n.Numero == i.Numero).Onda != i.Onda))
                throw new ArgumentException("A edição moveria um item já iniciado; o snapshot foi preservado.");
            itens = montado.Itens.Select(i => projeto.Itens.FirstOrDefault(a => a.Numero == i.Numero) is { } a
                && a.Numero != item.Numero && a.Onda == i.Onda ? a : i with { AtualizadoEm = agora }).ToArray();
        }
        return new(projeto.Slug, projeto.Nome, projeto.Nivel, projeto.Time, projeto.Repositorios, projeto.Branch,
            projeto.SemanaConvertida, projeto.Definicao, projeto.Anexos, projeto.Autonomia, projeto.Paralelismo,
            projeto.DestinoDaPr, ProjetosV1.AguardandoAprovacao, edicao.VersaoEsperada, itens,
            projeto.FeatureId, projeto.EpicId, projeto.Iteracao, projeto.Board, projeto.BranchesDasTasks, agora,
            projeto.BaseDoBranch, projeto.FluxoPadrao, projeto.Agente, projeto.Modelo, projeto.Modelos, projeto.PoliticaDeFinalizacao,
            projeto.SkillDoPacote, projeto.SkillsDoPacote, projeto.ConfiguracaoDoPacote)
            { EdicaoPeloBoard = true };
    }
}
