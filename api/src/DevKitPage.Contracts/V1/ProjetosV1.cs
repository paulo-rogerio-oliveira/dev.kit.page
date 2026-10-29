namespace DevKitPage.Contracts.V1;

/// <summary>Estado portátil de um item. Não contém caminhos nem ids de sessões da máquina.</summary>
public sealed record ItemDoProjetoV1(
    int Numero, string Titulo, string Descricao, string Fluxo, IReadOnlyList<string> Repositorios,
    IReadOnlyList<string> Componentes, IReadOnlyList<int> Dependencias, int Onda, int WorkItemId, string Situacao,
    string Tipo = "", int Prioridade = 0, double Horas = 0, string Branch = "",
    DateTimeOffset? AtualizadoEm = null);

/// <summary>Snapshot completo, versionado pelo servidor. Ausência de itens não significa exclusão.</summary>
public sealed record EnvioDeProjetoV1(
    string Slug, string Nome, string Nivel, string Time, IReadOnlyList<string> Repositorios, string Branch,
    bool SemanaConvertida, string Definicao, IReadOnlyList<string> Anexos, bool Autonomia, int Paralelismo,
    string DestinoDaPr, string Situacao, int? Versao, IReadOnlyList<ItemDoProjetoV1> Itens,
    int FeatureId = 0, int EpicId = 0, string Iteracao = "", string Board = "",
    IReadOnlyList<string>? BranchesDasTasks = null, DateTimeOffset? AtualizadoEm = null);

public sealed record ProjetoV1(
    long Id, string Slug, string Nome, string Nivel, string Time, IReadOnlyList<string> Repositorios, string Branch,
    bool SemanaConvertida, string Definicao, IReadOnlyList<string> Anexos, bool Autonomia, int Paralelismo,
    string DestinoDaPr, string Situacao, int Versao, string Autor, DateTimeOffset AtualizadoEm,
    IReadOnlyList<ItemDoProjetoV1> Itens, bool PodeApagar,
    int FeatureId = 0, int EpicId = 0, string Iteracao = "", string Board = "",
    IReadOnlyList<string>? BranchesDasTasks = null, DateTimeOffset? AlteradoEm = null);
