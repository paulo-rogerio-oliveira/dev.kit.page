using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>O projeto gravado: <see cref="Novo"/> é a criação (201); falso, a substituição da versão lida (200).</summary>
public sealed record ProjetoGravado(ProjetoV1 Projeto, bool Novo);

/// <summary>
/// Os PROJETOS compartilhados (US #693): o projeto do dev.kit (repositórios, branch, definição, autonomia, paralelismo,
/// destino da PR) e o plano dele (itens e ondas), por escopo. A visibilidade e quem apaga são as do design
/// (<see cref="RegrasDoDesign.Visivel"/>, <see cref="RegrasDoDesign.PodeApagar"/>): o nível empresa para todos da
/// empresa, o time para quem o declara, o usuário só para o dono — e quem não tem empresa guarda no nível usuário.
/// </summary>
public interface IProjetosDaEmpresa
{
    /// <summary>Os projetos visíveis; com <paramref name="time"/>, o nível time só desse time; com <paramref name="desde"/>, só os alterados depois.</summary>
    Task<IReadOnlyList<ProjetoV1>> ListarAsync(EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct);

    /// <summary>Um projeto visível; nulo quando ele não existe ou não é visível (404, sem dizer qual dos dois).</summary>
    Task<ProjetoV1?> ObterAsync(EscopoDoDesign escopo, long id, CancellationToken ct);

    /// <summary>
    /// Cria pelo (escopo, slug), ou substitui o projeto quando a versão enviada é a gravada — com o plano inteiro. O envio
    /// chega JÁ validado (<see cref="RegrasDosProjetos.Validar"/>): aqui ficam o time sem empresa, o conflito e a versão.
    /// </summary>
    Task<ResultadoDaGestao<ProjetoGravado>> GravarAsync(EscopoDoDesign escopo, EnvioDeProjetoV1 envio, CancellationToken ct);

    /// <summary>Apaga o projeto e o plano: só o autor ou o gestor da empresa.</summary>
    Task<ResultadoDaGestao<ProjetoV1>> ApagarAsync(EscopoDoDesign escopo, long id, CancellationToken ct);
}

/// <summary>As regras puras dos projetos (US #693): o slug, a validação do envio e as dependências sem ciclo — sem banco.</summary>
public static partial class RegrasDosProjetos
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,59}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugRegex();

    /// <summary>O slug do projeto: minúsculas, dígitos e hífen, começando por letra ou dígito (até 60).</summary>
    public static bool SlugValido(string? slug) => slug is not null && SlugRegex().IsMatch(slug);

    /// <summary>A lista gravada (uma por linha) como lista, sem vazios.</summary>
    public static IReadOnlyList<string> Lista(string? gravada)
        => (gravada ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A lista como é gravada: uma por linha, sem vazios nem repetidos.</summary>
    public static string Linhas(IEnumerable<string>? lista)
        => string.Join('\n', (lista ?? []).Select(l => (l ?? string.Empty).Trim()).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal));

    /// <summary>As dependências gravadas (por vírgula) como números.</summary>
    public static IReadOnlyList<int> Dependencias(string? gravadas)
        => (gravadas ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => int.TryParse(d, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0)
            .Where(n => n > 0).ToArray();

    /// <summary>
    /// O item que fecha um ciclo de dependências, ou nulo quando o plano é um grafo sem ciclo — o Orquestrador não teria
    /// onda para o item que depende de si mesmo, por mais voltas que dê.
    /// </summary>
    public static int? ItemEmCiclo(IReadOnlyList<ItemDoProjetoV1> itens)
    {
        var dependencias = itens.ToDictionary(i => i.Numero, i => i.Dependencias);
        var estado = new Dictionary<int, int>();
        int? Visitar(int numero)
        {
            if (estado.GetValueOrDefault(numero) == 2)
                return null;
            if (estado.GetValueOrDefault(numero) == 1)
                return numero;
            estado[numero] = 1;
            foreach (var dependencia in dependencias.GetValueOrDefault(numero) ?? [])
            {
                if (Visitar(dependencia) is { } ciclo)
                    return ciclo;
            }
            estado[numero] = 2;
            return null;
        }

        foreach (var item in itens)
        {
            if (Visitar(item.Numero) is { } ciclo)
                return ciclo;
        }
        return null;
    }

    /// <summary>O erro do envio, ou vazio quando ele vale (o time e o conflito dependem do banco).</summary>
    public static string Validar(EnvioDeProjetoV1? envio)
    {
        if (envio is null)
            return "Envie o projeto.";
        if (!SlugValido(envio.Slug))
            return "O slug do projeto usa minúsculas, dígitos e hífen (até 60), começando por letra ou dígito.";
        var nome = envio.Nome?.Trim() ?? string.Empty;
        if (nome.Length == 0 || nome.Length > ProjetosV1.TamanhoMaximoDoNome)
            return $"Informe o nome do projeto (até {ProjetosV1.TamanhoMaximoDoNome} caracteres).";
        var nivel = envio.Nivel?.Trim().ToLowerInvariant() ?? string.Empty;
        if (nivel.Length > 0 && nivel is not (NiveisDeConvencao.Empresa or NiveisDeConvencao.Time or NiveisDeConvencao.Usuario))
            return $"O nível é '{NiveisDeConvencao.Empresa}', '{NiveisDeConvencao.Time}' ou '{NiveisDeConvencao.Usuario}'.";
        if (RegrasDoConhecimentoDaEmpresa.NormalizarTime(envio.Time).Length > RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoTime)
            return $"O nome do time passa de {RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoTime} caracteres.";
        var repositorios = envio.Repositorios?.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray() ?? [];
        if (repositorios.Length == 0)
            return "Informe ao menos um repositório do projeto.";
        if (repositorios.Length > ProjetosV1.MaximoDeRepositorios || repositorios.Any(r => r.Trim().Length > DesignV1.TamanhoMaximoDoRepositorio))
            return $"O projeto tem até {ProjetosV1.MaximoDeRepositorios} repositórios, de até {DesignV1.TamanhoMaximoDoRepositorio} caracteres.";
        var branch = envio.Branch?.Trim() ?? string.Empty;
        if (branch.Length == 0 || branch.Length > ProjetosV1.TamanhoMaximoDoBranch || branch.Contains(' ', StringComparison.Ordinal) || branch.Contains("..", StringComparison.Ordinal))
            return "Informe o branch do projeto (um nome de branch válido).";
        if ((envio.DestinoDaPr?.Trim().Length ?? 0) > ProjetosV1.TamanhoMaximoDoBranch)
            return "O destino da PR passa do tamanho de um branch.";
        if (envio.Paralelismo < 1 || envio.Paralelismo > ProjetosV1.ParalelismoMaximo)
            return $"O paralelismo vai de 1 a {ProjetosV1.ParalelismoMaximo}.";
        if (!ProjetosV1.Situacoes.Contains(envio.Situacao ?? ProjetosV1.Rascunho))
            return $"A situação do projeto é uma de: {string.Join(", ", ProjetosV1.Situacoes)}.";
        if ((envio.Definicao?.Length ?? 0) > ProjetosV1.TamanhoMaximoDaDefinicao)
            return $"A definição passa de {ProjetosV1.TamanhoMaximoDaDefinicao} caracteres.";
        var anexos = envio.Anexos ?? [];
        if (anexos.Count > ProjetosV1.MaximoDeAnexos || anexos.Any(a => (a?.Length ?? 0) > ProjetosV1.TamanhoMaximoDoTexto))
            return $"O projeto tem até {ProjetosV1.MaximoDeAnexos} anexos.";
        return ValidarItens(envio.Itens ?? []);
    }

    /// <summary>O erro do plano, ou vazio: números únicos, dependências que existem, sem ciclo, situações conhecidas.</summary>
    public static string ValidarItens(IReadOnlyList<ItemDoProjetoV1> itens)
    {
        if (itens.Count > ProjetosV1.MaximoDeItens)
            return $"O plano tem até {ProjetosV1.MaximoDeItens} itens.";
        var numeros = new HashSet<int>();
        foreach (var item in itens)
        {
            if (item.Numero <= 0 || !numeros.Add(item.Numero))
                return $"O número do item é positivo e único no plano (o {item.Numero} se repete ou não vale).";
            var titulo = item.Titulo?.Trim() ?? string.Empty;
            if (titulo.Length == 0 || titulo.Length > ProjetosV1.TamanhoMaximoDoNome)
                return $"O item {item.Numero} precisa de título (até {ProjetosV1.TamanhoMaximoDoNome} caracteres).";
            if ((item.Descricao?.Length ?? 0) > ProjetosV1.TamanhoMaximoDoTexto || (item.Fluxo?.Length ?? 0) > ProjetosV1.TamanhoMaximoDoNome)
                return $"Um dos textos do item {item.Numero} passa do tamanho do contrato.";
            if (item.Prioridade is < 0 or > 4 || !double.IsFinite(item.Horas) || item.Horas < 0
                || item.WorkItemId < 0 || item.Branch is null || item.Branch.Length > ProjetosV1.TamanhoMaximoDoBranch)
                return $"Metadados inválidos no item {item.Numero}.";
            if (item.Onda < 0)
                return $"A onda do item {item.Numero} não é negativa.";
            if (!ProjetosV1.SituacoesDoItem.Contains(item.Situacao ?? ProjetosV1.ItemPlanejado))
                return $"A situação do item {item.Numero} é uma de: {string.Join(", ", ProjetosV1.SituacoesDoItem)}.";
        }

        foreach (var item in itens)
        {
            foreach (var dependencia in item.Dependencias ?? [])
            {
                if (dependencia == item.Numero)
                    return $"O item {item.Numero} depende de si mesmo.";
                if (!numeros.Contains(dependencia))
                    return $"O item {item.Numero} depende do item {dependencia}, que não está no plano.";
            }
        }

        return ItemEmCiclo(itens) is { } ciclo ? $"As dependências fecham um ciclo no item {ciclo}: o plano não teria onda para ele." : string.Empty;
    }
}
