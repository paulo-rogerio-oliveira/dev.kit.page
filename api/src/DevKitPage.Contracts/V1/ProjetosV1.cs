namespace DevKitPage.Contracts.V1;

/// <summary>Estado portátil de um item. Não contém caminhos nem ids de sessões da máquina.</summary>
public sealed record ItemDoProjetoV1(
    int Numero, string Titulo, string Descricao, string Fluxo, IReadOnlyList<string> Repositorios,
    IReadOnlyList<string> Componentes, IReadOnlyList<int> Dependencias, int Onda, int WorkItemId, string Situacao,
    string Tipo = "", int Prioridade = 0, double Horas = 0, string Branch = "",
    DateTimeOffset? AtualizadoEm = null,
    DesignDoItemV1? Design = null, string? Agente = null, string? Modelo = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int VersaoDaEdicaoDoPlano { get; init; }
}

public sealed record ReferenciaDePropostaV1(string Projeto, string Tela, int Versao, string Hash);
public sealed record DesignDoItemV1(bool AlteraTela, int Revisao, IReadOnlyList<ReferenciaDePropostaV1> Propostas);
public sealed record ModeloDoProjetoV1(string Agente, string Modelo = "");

/// <summary>
/// Os três estados de um conjunto do espelho no Azure Boards (o work item pai ou os itens), nos eventos do projeto: a
/// criação na aprovação, o começo da execução e a finalização. Vazio (ou ausente) é o padrão pela categoria do tipo:
/// inicial do tipo, primeiro da categoria InProgress e, na finalização, Completed nos itens e Resolved no pai.
/// </summary>
public sealed record EstadosDoWorkItemV1(string? Inicial = null, string? Execucao = null, string? Finalizacao = null);

/// <summary>Os estados configurados do espelho (boards-estados): os do pai e os dos itens. Ausente é o padrão.</summary>
public sealed record EstadosNoBoardV1(EstadosDoWorkItemV1? Pai = null, EstadosDoWorkItemV1? Itens = null);

public sealed record PoliticaDoProjetoV1(string Slug, string Politica, int Revisao, string FluxoDaFinalizacao,
    DateTimeOffset AdotadaEm, string Motivo)
{
    public string IdentidadeDoFluxo { get; init; } = string.Empty;
    public string RevisaoDoFluxo { get; init; } = string.Empty;
    public ModeloDoProjetoV1? ModeloDaFinalizacao { get; init; }
}

/// <summary>
/// Snapshot completo, versionado pelo servidor. Ausência de itens não significa exclusão. Os campos do espelho
/// (<see cref="NivelNoBoard"/>, <see cref="EstadosNoBoard"/>) e a marca de disparo (<see cref="DisparadoEm"/>) são
/// opcionais: o cliente antigo que não os envia não apaga o valor gravado.
/// </summary>
public sealed record EnvioDeProjetoV1(
    string Slug, string Nome, string? Nivel, string? Time, IReadOnlyList<string> Repositorios, string Branch,
    bool SemanaConvertida, string Definicao, IReadOnlyList<string> Anexos, bool Autonomia, int Paralelismo,
    string DestinoDaPr, string? Situacao, int? Versao, IReadOnlyList<ItemDoProjetoV1> Itens,
    int FeatureId = 0, int EpicId = 0, string Iteracao = "", string Board = "",
    IReadOnlyList<string>? BranchesDasTasks = null, DateTimeOffset? AtualizadoEm = null,
    string? BaseDoBranch = null, string? FluxoPadrao = null, string? Agente = null, string? Modelo = null,
    IReadOnlyList<ModeloDoProjetoV1>? Modelos = null, PoliticaDoProjetoV1? PoliticaDeFinalizacao = null,
    string? SkillDoPacote = null, IReadOnlyList<OpcaoDeSkillDoPacoteV1>? SkillsDoPacote = null,
    string? NivelNoBoard = null, EstadosNoBoardV1? EstadosNoBoard = null, DateTimeOffset? DisparadoEm = null)
{
    /// <summary>Origem interna da rota de edição; não pode ser definida por um snapshot HTTP.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool EdicaoPeloBoard { get; init; }
}

public sealed record ProjetoV1(
    long Id, string Slug, string Nome, string Nivel, string Time, IReadOnlyList<string> Repositorios, string Branch,
    bool SemanaConvertida, string Definicao, IReadOnlyList<string> Anexos, bool Autonomia, int Paralelismo,
    string DestinoDaPr, string Situacao, int Versao, string Autor, DateTimeOffset AtualizadoEm,
    IReadOnlyList<ItemDoProjetoV1> Itens, bool PodeApagar,
    int FeatureId = 0, int EpicId = 0, string Iteracao = "", string Board = "",
    IReadOnlyList<string>? BranchesDasTasks = null, DateTimeOffset? AlteradoEm = null, string Papel = "",
    string? BaseDoBranch = null, string? FluxoPadrao = null, string? Agente = null, string? Modelo = null,
    IReadOnlyList<ModeloDoProjetoV1>? Modelos = null, PoliticaDoProjetoV1? PoliticaDeFinalizacao = null,
    string? SkillDoPacote = null, IReadOnlyList<OpcaoDeSkillDoPacoteV1>? SkillsDoPacote = null,
    string? NivelNoBoard = null, EstadosNoBoardV1? EstadosNoBoard = null, DateTimeOffset? DisparadoEm = null)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? ItensDoGrafo { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? PendenciaDoGrafo { get; init; }
}

public sealed record OpcaoDeSkillDoPacoteV1(string Nome, string Tipo);
public sealed record EdicaoDaSkillDoPacoteV1(int VersaoEsperada, string SkillDoPacote);

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

    /// <summary>O nome de um estado do espelho: o System.State do Azure Boards é um campo texto de até 255.</summary>
    public const int TamanhoMaximoDoEstado = 255;

    public const string NivelNoBoardEpic = "epic";
    public const string NivelNoBoardFeature = "feature";
    public const string NivelNoBoardRequisito = "requisito";

    /// <summary>O nível do work item pai no Azure Boards (boards-estados, D9); vazio ou ausente é a Feature.</summary>
    public static IReadOnlyList<string> NiveisNoBoard { get; } = [NivelNoBoardEpic, NivelNoBoardFeature, NivelNoBoardRequisito];

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
