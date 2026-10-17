using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O que a EMPRESA sabe e compartilha com o dev.kit (US #491), além do catálogo de skills: o catálogo de repositórios (as
/// convenções viraram o contexto, <see cref="IContextoDaEmpresa"/>, na US #556). Toda operação recebe o <see cref="EscopoDaEmpresa"/> e o aplica aqui, num
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
}

/// <summary>
/// As regras puras do catálogo de repositórios da empresa (US #491): validação e normalização. As das convenções viraram
/// as do contexto (<see cref="RegrasDoContexto"/>, US #556).
/// </summary>
public static class RegrasDoConhecimentoDaEmpresa
{
    public const int TamanhoMaximoDaOrigem = 400;
    public const int TamanhoMaximoDaDescricao = 1000;

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
}
