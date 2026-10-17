using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O que a EMPRESA sabe e compartilha com o dev.kit (US #491), além do catálogo de skills: o catálogo de repositórios e
/// as convenções da empresa e dos times. Toda operação recebe o <see cref="EscopoDaEmpresa"/> e o aplica aqui, num
/// ponto só — o que é de outra empresa não aparece, nem se grava por cima.
/// </summary>
public interface IConhecimentoDaEmpresa
{
    /// <summary>Os repositórios da empresa cujo caminho ou descrição contém o <paramref name="filtro"/> (todos, sem ele).</summary>
    Task<IReadOnlyList<RepositorioDaEmpresaV1>> RepositoriosAsync(EscopoDaEmpresa escopo, string? filtro, CancellationToken ct);

    /// <summary>
    /// Grava o repositório JÁ validado (<see cref="RegrasDoConhecimentoDaEmpresa.ValidarRepositorio"/>): o novo entra; o
    /// mesmo caminho (pela chave normalizada) troca a descrição. Qualquer um da empresa grava.
    /// </summary>
    Task<ResultadoDaGestao<RepositorioDaEmpresaV1>> GravarRepositorioAsync(EscopoDaEmpresa escopo, GravacaoDeRepositorioV1 gravacao, CancellationToken ct);

    /// <summary>
    /// Remove o repositório do catálogo: o gestor, ou quem gravou a descrição atual. Caminho que não está no catálogo da
    /// empresa é <see cref="FalhaNaGestao.NaoEncontrado"/>.
    /// </summary>
    Task<ResultadoDaGestao<RepositorioDaEmpresaV1>> RemoverRepositorioAsync(EscopoDaEmpresa escopo, string origem, CancellationToken ct);

    /// <summary>
    /// As convenções que valem para quem consulta — o que o dev.kit lê: as da empresa, as do <paramref name="time"/>
    /// declarado e as do próprio usuário.
    /// </summary>
    Task<IReadOnlyList<ConvencaoV1>> ConvencoesAsync(EscopoDaEmpresa escopo, string? time, CancellationToken ct);

    /// <summary>
    /// TODAS as convenções que quem consulta pode auditar no site, com o autor e a data: o gestor vê todos os níveis
    /// (inclusive as de cada usuário); o dev vê as da empresa, as de todos os times e as dele.
    /// </summary>
    Task<IReadOnlyList<ConvencaoV1>> AuditoriaDeConvencoesAsync(EscopoDaEmpresa escopo, CancellationToken ct);

    /// <summary>
    /// Grava a convenção JÁ validada (<see cref="RegrasDoConhecimentoDaEmpresa.ValidarConvencao"/>); valor vazio apaga.
    /// A da EMPRESA é do gestor; a de um TIME, de qualquer um da empresa; a do USUÁRIO, dele mesmo.
    /// </summary>
    Task<ResultadoDaGestao<ConvencaoV1>> GravarConvencaoAsync(EscopoDaEmpresa escopo, ConvencaoV1 convencao, CancellationToken ct);
}

/// <summary>As regras puras do catálogo de repositórios e das convenções da empresa (US #491): validação e normalização.</summary>
public static class RegrasDoConhecimentoDaEmpresa
{
    public const int TamanhoMaximoDaOrigem = 400;
    public const int TamanhoMaximoDaDescricao = 1000;
    public const int TamanhoMaximoDaChave = 60;
    public const int TamanhoMaximoDoValor = 200;
    public const int TamanhoMaximoDoTime = 80;

    /// <summary>Nível de convenção (o maior dos dois nomes).</summary>
    public const int TamanhoMaximoDoNivel = 10;

    private static readonly Regex ChaveValida = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// O caminho normalizado — sem espaço nas pontas, sem barra nem <c>.git</c> no fim, em minúsculas: a mesma origem
    /// escrita de dois jeitos é UM repositório no catálogo.
    /// </summary>
    public static string ChaveDaOrigem(string origem)
    {
        var chave = (origem ?? string.Empty).Trim().TrimEnd('/', '\\');
        if (chave.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            chave = chave[..^4];
        return chave.ToLowerInvariant();
    }

    /// <summary>O time como chave: sem espaço nas pontas e em minúsculas ("Vendas" e "vendas" são o mesmo time).</summary>
    public static string NormalizarTime(string? time) => (time ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>O erro da gravação de repositório, ou vazio quando ela vale.</summary>
    public static string ValidarRepositorio(GravacaoDeRepositorioV1? gravacao)
    {
        var origem = gravacao?.Origem?.Trim() ?? string.Empty;
        var descricao = gravacao?.Descricao?.Trim() ?? string.Empty;
        if (origem.Length == 0)
            return "Informe o caminho do repositório (a URL do GitHub ou a pasta).";
        if (origem.Length > TamanhoMaximoDaOrigem)
            return $"O caminho passa de {TamanhoMaximoDaOrigem} caracteres.";
        if (descricao.Length == 0)
            return "Informe a descrição do repositório: para que serve e o que tem.";
        return descricao.Length > TamanhoMaximoDaDescricao
            ? $"A descrição passa de {TamanhoMaximoDaDescricao} caracteres."
            : string.Empty;
    }

    /// <summary>O erro da gravação de convenção, ou vazio quando ela vale.</summary>
    public static string ValidarConvencao(ConvencaoV1? convencao)
    {
        var nivel = convencao?.Nivel?.Trim().ToLowerInvariant() ?? string.Empty;
        var chave = convencao?.Chave?.Trim().ToLowerInvariant() ?? string.Empty;
        var time = NormalizarTime(convencao?.Time);
        if (nivel is not (NiveisDeConvencao.Empresa or NiveisDeConvencao.Time or NiveisDeConvencao.Usuario))
            return $"O nível da convenção é '{NiveisDeConvencao.Empresa}', '{NiveisDeConvencao.Time}' ou '{NiveisDeConvencao.Usuario}'.";
        if (chave.Length == 0 || chave.Length > TamanhoMaximoDaChave || !ChaveValida.IsMatch(chave))
            return "A chave da convenção é um nome curto em minúsculas, sem acento, como 'branch.hotfix'.";
        if ((convencao?.Valor?.Trim().Length ?? 0) > TamanhoMaximoDoValor)
            return $"O valor da convenção passa de {TamanhoMaximoDoValor} caracteres.";
        if (nivel == NiveisDeConvencao.Time && time.Length == 0)
            return "Informe o time da convenção (a Equipe das Configurações do dev.kit).";
        return time.Length > TamanhoMaximoDoTime ? $"O nome do time passa de {TamanhoMaximoDoTime} caracteres." : string.Empty;
    }
}
