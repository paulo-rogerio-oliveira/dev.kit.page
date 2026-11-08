namespace DevKitPage.Contracts.V1;

/// <summary>
/// O formulário do NOVO PROJETO pelo site (remote-control-v3, item 4): o JSON que vai no <c>texto</c> do comando
/// <see cref="ConversaV1.ComandoProjeto"/>. O dev.kit o executa pelo mesmo caminho do <c>devcli projeto --criar</c> e responde
/// <c>concluido</c> (com o slug) ou <c>recusado</c> (com o motivo). O projeto nasce em rascunho, com a autonomia e o paralelismo
/// padrão: o plano, a aprovação e o disparo continuam no dev.kit — o site não aprova nem dispara.
/// </summary>
/// <param name="Nome">O nome do projeto; o slug sai dele, como no <c>devcli</c>.</param>
/// <param name="Repositorios">
/// Os repositórios, pelo NOME do catálogo da máquina (<see cref="RepositorioNoCatalogoV1.Nome"/>, o host e o caminho): o dev.kit
/// só aceita os que conhece e recusa o resto.
/// </param>
/// <param name="Definicao">O que o dev quer (texto livre, opcional).</param>
/// <param name="Nivel">O compartilhamento no dev.kit.page: um de <see cref="CriacaoRemotaV1.Niveis"/>; vazio é o padrão do <c>devcli</c>.</param>
public sealed record NovoProjetoRemotoV1(string Nome, IReadOnlyList<string> Repositorios, string? Definicao = null, string? Nivel = null);

/// <summary>
/// O formulário do NOVO PROJETO DE DESIGN pelo site: o JSON do <c>texto</c> do comando <see cref="ConversaV1.ComandoDesign"/>. O
/// dev.kit abre (ou cria) o projeto do repositório pelo mesmo caminho do <c>devcli design --repo</c>; nenhuma versão é gravada
/// nem enviada, então o teto de versões do plano (<see cref="UsoDoDesignV1"/>) não muda com ele.
/// </summary>
/// <param name="Repositorio">O repositório, pelo nome do catálogo da máquina; o dev.kit recusa o que não conhece.</param>
public sealed record NovoDesignRemotoV1(string Repositorio);

/// <summary>Os tetos dos formulários de criação pelo site.</summary>
public static class CriacaoRemotaV1
{
    /// <summary>Quantos repositórios um projeto criado pelo site leva.</summary>
    public const int MaximoDeRepositorios = 20;

    /// <summary>O maior texto da definição (o JSON inteiro cabe no texto do comando, <see cref="ConversaV1.TamanhoMaximoDoTexto"/>).</summary>
    public const int TamanhoMaximoDaDefinicao = 3000;

    public const string NivelUsuario = "usuario";
    public const string NivelTime = "time";
    public const string NivelEmpresa = "empresa";

    /// <summary>Os níveis de compartilhamento do projeto, os mesmos do <c>devcli projeto --nivel</c>.</summary>
    public static IReadOnlyList<string> Niveis { get; } = [NivelUsuario, NivelTime, NivelEmpresa];
}
