using DevKit.Matrix;

namespace DevKitPage.Core;

/// <summary>Somente definição: execução e avaliação são obrigações posteriores ao disparo.</summary>
public static class RegrasProntidaoMatrix
{
    public static void Validar(MatrizMatrix matriz, MapaMatrix mapa)
    {
        RegrasMatrix.Exigir(matriz.Celulas.Length > 0, "Matriz vazia.");
        RegrasMatrix.Exigir(matriz.Componentes.All(c => mapa.Componentes.Any(e => e.Id == c.Id))
            && matriz.Layers.All(l => mapa.Layers.Any(e => e.Id == l.Id)), "Eixo da matriz ausente na revisão do mapa.");
        foreach (var componente in matriz.Componentes)
            foreach (var layer in matriz.Layers)
                RegrasMatrix.Exigir(matriz.Celulas.Any(c => c.Componente == componente.Id && c.Layer == layer.Id),
                    $"Coordenada {componente.Id} × {layer.Id} sem decisão de aplicabilidade.");
        RegrasMatrix.Exigir(matriz.Celulas.All(c => c.Responsaveis.Length > 0), "Célula sem responsável, inclusive na não aplicação.");
        foreach (var relacao in mapa.Relacoes.Where(r => matriz.Celulas.Any(c => c.Aplicavel
            && (c.Componente == r.Origem || c.Componente == r.Destino) && r.Layers.Contains(c.Layer))))
            RegrasMatrix.Exigir((matriz.Coberturas ?? []).Any(c => c.Relacao == relacao.Id
                && matriz.Celulas.Any(x => x.Id == c.Celula && x.Responsaveis.Length > 0)),
                $"Relação {relacao.Id} sem obrigação e dono explícitos.");
    }
}
