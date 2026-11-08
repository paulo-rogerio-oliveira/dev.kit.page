using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// As regras dos comandos de CRIAÇÃO pelo site (remote-control-v3, item 4): o formulário que vai no <c>texto</c> de
/// <see cref="ConversaV1.ComandoProjeto"/> e <see cref="ConversaV1.ComandoDesign"/> é conferido ANTES da fila — o JSON, os campos
/// e os tetos —, para o dev.kit receber só o que pode executar. A decisão final é do dev.kit: ele confere os repositórios contra
/// os que conhece e aplica as regras do <c>devcli</c>, recusando com o motivo. Sem banco.
/// </summary>
public static class RegrasDaCriacaoRemota
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>O problema do formulário do comando de criação <paramref name="tipo"/>, ou vazio quando ele vale.</summary>
    public static string Validar(string tipo, string texto)
    {
        if (texto.Length == 0)
            return "Envie o formulário da criação no texto (JSON).";
        try
        {
            return tipo switch
            {
                ConversaV1.ComandoProjeto => Validar(JsonSerializer.Deserialize<NovoProjetoRemotoV1>(texto, Json)),
                ConversaV1.ComandoDesign => Validar(JsonSerializer.Deserialize<NovoDesignRemotoV1>(texto, Json)),
                _ => string.Empty,
            };
        }
        catch (JsonException)
        {
            return "O formulário da criação não é um JSON válido.";
        }
    }

    /// <summary>O problema do novo projeto, ou vazio quando ele vale.</summary>
    public static string Validar(NovoProjetoRemotoV1? projeto)
    {
        if (projeto is null)
            return "Envie o novo projeto.";
        if (Invalido(projeto.Nome))
            return $"Informe o nome do projeto (até {RemotoV1.TamanhoMaximoDoNome} caracteres).";
        var repositorios = projeto.Repositorios ?? [];
        if (repositorios.Count == 0)
            return "Escolha ao menos um repositório do catálogo da máquina.";
        if (repositorios.Count > CriacaoRemotaV1.MaximoDeRepositorios)
            return $"O projeto leva até {CriacaoRemotaV1.MaximoDeRepositorios} repositórios.";
        if (repositorios.Any(Invalido))
            return $"Cada repositório é um nome do catálogo (até {RemotoV1.TamanhoMaximoDoNome} caracteres).";
        if ((projeto.Definicao?.Length ?? 0) > CriacaoRemotaV1.TamanhoMaximoDaDefinicao)
            return $"A definição tem até {CriacaoRemotaV1.TamanhoMaximoDaDefinicao} caracteres.";
        var nivel = RegrasDoRemoto.Normalizar(projeto.Nivel);
        if (nivel.Length > 0 && !CriacaoRemotaV1.Niveis.Contains(nivel))
            return $"O nível é um de: {string.Join(", ", CriacaoRemotaV1.Niveis)}.";
        return string.Empty;
    }

    /// <summary>O problema do novo projeto de design, ou vazio quando ele vale.</summary>
    public static string Validar(NovoDesignRemotoV1? design)
    {
        if (design is null)
            return "Envie o novo projeto de design.";
        return Invalido(design.Repositorio)
            ? $"Escolha o repositório do catálogo da máquina (até {RemotoV1.TamanhoMaximoDoNome} caracteres)."
            : string.Empty;
    }

    private static bool Invalido(string? texto) => string.IsNullOrWhiteSpace(texto) || texto.Length > RemotoV1.TamanhoMaximoDoNome;
}
