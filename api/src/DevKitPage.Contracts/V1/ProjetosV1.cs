namespace DevKitPage.Contracts.V1;

/// <summary>Estado portátil de um item. Não contém caminhos nem ids de sessões da máquina.</summary>
public sealed record ItemDoProjetoV1(
    int Numero, string Titulo, string Descricao, string Fluxo, IReadOnlyList<string> Repositorios,
    IReadOnlyList<string> Componentes, IReadOnlyList<int> Dependencias, int Onda, int WorkItemId, string Situacao,
    string Tipo = "", int Prioridade = 0, double Horas = 0, string Branch = "",
    DateTimeOffset? AtualizadoEm = null);

/// <summary>Snapshot completo, versionado pelo servidor. Ausência de itens não significa exclusão.</summary>
public sealed record EnvioDeProjetoV1(
    string Slug, string Nome, string? Nivel, string? Time, IReadOnlyList<string> Repositorios, string Branch,
    bool SemanaConvertida, string Definicao, IReadOnlyList<string> Anexos, bool Autonomia, int Paralelismo,
    string DestinoDaPr, string? Situacao, int? Versao, IReadOnlyList<ItemDoProjetoV1> Itens,
    int FeatureId = 0, int EpicId = 0, string Iteracao = "", string Board = "",
    IReadOnlyList<string>? BranchesDasTasks = null, DateTimeOffset? AtualizadoEm = null);

public sealed record ProjetoV1(
    long Id, string Slug, string Nome, string Nivel, string Time, IReadOnlyList<string> Repositorios, string Branch,
    bool SemanaConvertida, string Definicao, IReadOnlyList<string> Anexos, bool Autonomia, int Paralelismo,
    string DestinoDaPr, string Situacao, int Versao, string Autor, DateTimeOffset AtualizadoEm,
    IReadOnlyList<ItemDoProjetoV1> Itens, bool PodeApagar,
    int FeatureId = 0, int EpicId = 0, string Iteracao = "", string Board = "",
    IReadOnlyList<string>? BranchesDasTasks = null, DateTimeOffset? AlteradoEm = null);

public static class ProjetosV1
{
    public const int TamanhoMaximoDoSlug = 60;
    public const int TamanhoMaximoDoNome = 200;
    public const int TamanhoMaximoDoBranch = 250;
    public const int TamanhoMaximoDaDefinicao = 20000;
    public const int TamanhoMaximoDoTexto = 2000;
    public const int MaximoDeRepositorios = 20;
    public const int MaximoDeItens = 100;
    public const int MaximoDeAnexos = 50;
    public const int ParalelismoMaximo = 8;

    public const string Rascunho = "rascunho";
    public const string AguardandoAprovacao = "aguardando";
    public const string Aprovado = "aprovado";
    public const string Pausado = "pausado";
    public const string Fechando = "fechando";
    public const string Concluido = "concluido";

    public const string ItemPlanejado = "planejado";
    public const string ItemRodando = "rodando";
    public const string ItemPergunta = "pergunta";
    public const string ItemIntegrado = "integrado";
    public const string ItemFalhou = "falhou";

    /// <summary>As situações do projeto, na ordem da vida dele.</summary>
    public static IReadOnlyList<string> Situacoes { get; } = [Rascunho, AguardandoAprovacao, Aprovado, Pausado, Fechando, Concluido];

    /// <summary>As situações de um item.</summary>
    public static IReadOnlyList<string> SituacoesDoItem { get; } = [ItemPlanejado, ItemRodando, ItemPergunta, ItemIntegrado, ItemFalhou];
}
