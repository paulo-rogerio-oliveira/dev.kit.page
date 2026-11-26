using ItemDoProjeto = DevKitPage.Contracts.V1.ItemDoProjetoV1;

namespace DevKitPage.Core;

/// <summary>O plano posto em ondas: os itens com a onda de cada um, quantas ondas e o porquê — ou o erro.</summary>
/// <param name="Itens">Os itens, pelo número, com a <see cref="ItemDoProjeto.Onda"/> calculada.</param>
/// <param name="Ondas">Quantas ondas o plano tem.</param>
/// <param name="Erro">O que impede o plano (o ciclo, a dependência de fora); nulo quando ele vale.</param>
/// <param name="Motivos">O porquê de cada item que não ficou na primeira onda — a faixa "Por que N ondas" da tela.</param>
public sealed record PlanoMontado(IReadOnlyList<ItemDoProjeto> Itens, int Ondas, string? Erro, IReadOnlyList<string> Motivos)
{
    /// <summary>O plano vale: dá para aprovar e disparar.</summary>
    public bool Ok => Erro is null;

    /// <summary>Os itens de uma onda, pelo número.</summary>
    public IReadOnlyList<ItemDoProjeto> DaOnda(int onda) => Itens.Where(i => i.Onda == onda).ToArray();
}

/// <summary>
/// (US #693 · task #696) A regra PURA que põe o plano do projeto em ONDAS: a ordenação topológica pelas dependências (o
/// item fica numa onda depois da de todas as dependências dele) e a regra do COMPONENTE — dois itens que tocam o mesmo
/// componente não ficam na mesma onda, porque rodariam juntos e conflitariam no mesmo código (o 4 espera o 3 no
/// Executando). Os independentes, sem componente em comum, ficam juntos na primeira onda que cabe: é o paralelismo.
/// <para>
/// Determinística: a mesma entrada dá as mesmas ondas, e a ordem de decisão é a topológica com o número como desempate.
/// O ciclo e a dependência de um item que não está no plano são recusados — não haveria onda para eles.
/// </para>
/// </summary>
public static partial class PlanoEmOndas
{
    /// <summary>Monta as ondas dos <paramref name="itens"/>.</summary>
    public static PlanoMontado Montar(IReadOnlyList<ItemDoProjeto> itens)
    {
        var porNumero = new Dictionary<int, ItemDoProjeto>();
        foreach (var item in itens)
        {
            if (item.Numero <= 0 || !porNumero.TryAdd(item.Numero, item))
                return Recusado(itens, $"O número {item.Numero} do item se repete ou não vale: cada item tem um número positivo e único.");
        }

        foreach (var item in itens)
        {
            if (item.Dependencias.Contains(item.Numero))
                return Recusado(itens, $"O item {item.Numero} depende de si mesmo.");
            var fora = item.Dependencias.FirstOrDefault(d => !porNumero.ContainsKey(d));
            if (fora != 0)
                return Recusado(itens, $"O item {item.Numero} depende do item {fora}, que não está no plano.");
        }

        if (Topologica(porNumero) is not { } ordem)
            return Recusado(itens, $"As dependências fecham um ciclo ({Ciclo(porNumero)}): o plano não teria onda para eles.");

        var onda = new Dictionary<int, int>();
        var componentesDaOnda = new Dictionary<int, HashSet<string>>();
        var motivos = new List<string>();
        foreach (var numero in ordem)
        {
            var item = porNumero[numero];
            var depois = item.Dependencias.Count == 0 ? 0 : item.Dependencias.Max(d => onda[d]);
            var componentes = item.Componentes.Select(Normalizar).Where(c => c.Length > 0).ToArray();
            var candidata = depois + 1;
            string? emComum = null;
            while (componentesDaOnda.TryGetValue(candidata, out var ocupados) && componentes.FirstOrDefault(ocupados.Contains) is { } c)
            {
                emComum ??= c;
                candidata++;
            }

            onda[numero] = candidata;
            if (!componentesDaOnda.TryGetValue(candidata, out var daOnda))
                componentesDaOnda[candidata] = daOnda = new HashSet<string>(StringComparer.Ordinal);
            daOnda.UnionWith(componentes);

            if (candidata > 1)
                motivos.Add(Motivo(item, depois, emComum, porNumero, onda, candidata));
        }

        var comOnda = itens.OrderBy(i => i.Numero).Select(i => i with { Onda = onda[i.Numero] }).ToArray();
        return new PlanoMontado(comOnda, comOnda.Length == 0 ? 0 : comOnda.Max(i => i.Onda), null, motivos);
    }

    /// <summary>
    /// Os números que dependem, direta ou indiretamente, de <paramref name="numero"/> — o que uma falha dele segura. O
    /// item em si não entra.
    /// </summary>
    public static IReadOnlySet<int> Dependentes(IReadOnlyList<ItemDoProjeto> itens, int numero)
    {
        var dependentes = new HashSet<int>();
        var fila = new Queue<int>(new[] { numero });
        while (fila.Count > 0)
        {
            var atual = fila.Dequeue();
            foreach (var item in itens.Where(i => i.Dependencias.Contains(atual) && dependentes.Add(i.Numero)))
                fila.Enqueue(item.Numero);
        }

        return dependentes;
    }

    private static string Motivo(ItemDoProjeto item, int depois, string? emComum, Dictionary<int, ItemDoProjeto> porNumero,
        Dictionary<int, int> onda, int candidata)
    {
        var partes = new List<string>();
        if (depois > 0)
            partes.Add($"depende de {Lista(item.Dependencias.Order())}");
        if (emComum is not null)
        {
            var dono = porNumero.Values.Where(o => o.Numero != item.Numero && onda.TryGetValue(o.Numero, out var w) && w < candidata && w > depois
                    && o.Componentes.Select(Normalizar).Contains(emComum))
                .Select(o => o.Numero).Order().FirstOrDefault();
            partes.Add(dono > 0 ? $"toca {emComum}, o mesmo componente do {dono}" : $"toca {emComum}");
        }

        return $"O item {item.Numero} vai para a onda {candidata}: {string.Join(" e ", partes)}.";
    }

    /// <summary>A ordem topológica (Kahn, menor número primeiro); nula com ciclo.</summary>
    private static IReadOnlyList<int>? Topologica(Dictionary<int, ItemDoProjeto> porNumero)
    {
        var faltam = porNumero.Values.ToDictionary(i => i.Numero, i => i.Dependencias.Distinct().Count());
        var prontos = new SortedSet<int>(faltam.Where(f => f.Value == 0).Select(f => f.Key));
        var ordem = new List<int>();
        while (prontos.Count > 0)
        {
            var proximo = prontos.Min;
            prontos.Remove(proximo);
            ordem.Add(proximo);
            foreach (var dependente in porNumero.Values.Where(i => i.Dependencias.Distinct().Contains(proximo)))
            {
                if (--faltam[dependente.Numero] == 0)
                    prontos.Add(dependente.Numero);
            }
        }

        return ordem.Count == porNumero.Count ? ordem : null;
    }

    /// <summary>Os itens presos no ciclo (os que a ordem topológica não alcançou), para a mensagem.</summary>
    private static string Ciclo(Dictionary<int, ItemDoProjeto> porNumero)
    {
        var alcancados = new HashSet<int>();
        bool progrediu;
        do
        {
            progrediu = false;
            foreach (var item in porNumero.Values.Where(i => !alcancados.Contains(i.Numero) && i.Dependencias.All(alcancados.Contains)))
                progrediu |= alcancados.Add(item.Numero);
        }
        while (progrediu);

        return "itens " + Lista(porNumero.Keys.Where(n => !alcancados.Contains(n)).Order());
    }

    private static PlanoMontado Recusado(IReadOnlyList<ItemDoProjeto> itens, string erro)
        => new(itens, 0, erro, Array.Empty<string>());

    private static string Normalizar(string componente) => (componente ?? string.Empty).Trim().ToLowerInvariant();

    private static string Lista(IEnumerable<int> numeros)
    {
        var lista = numeros.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        return lista.Length <= 1 ? string.Join(string.Empty, lista) : string.Join(", ", lista[..^1]) + " e " + lista[^1];
    }
}

