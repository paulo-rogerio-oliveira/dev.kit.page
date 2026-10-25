namespace DevKitPage.Contracts.V1;

/// <summary>
/// Um PROJETO do dev.kit (US #693): o trabalho que o Orquestrador quebra em itens e ondas, num BRANCH próprio (ou na
/// semana convertida), compartilhado por escopo — <c>empresa</c>, <c>time</c> ou <c>usuario</c>, a mesma regra do contexto e
/// do design. Único por (escopo, <see cref="Slug"/>).
/// </summary>
/// <param name="Id">O número do projeto na API.</param>
/// <param name="Slug">O identificador do projeto no dev.kit (o <c>{SLUG}</c> do <c>branch.projeto</c>).</param>
/// <param name="Nivel">Um de <see cref="NiveisDeConvencao"/>.</param>
/// <param name="Time">O time do nível <c>time</c>; vazio nos outros.</param>
/// <param name="Repositorios">As URLs dos repositórios do projeto.</param>
/// <param name="Branch">O branch do projeto (o próprio, ou o da semana convertida).</param>
/// <param name="SemanaConvertida">O branch é o da semana, convertido em projeto (não foi criado).</param>
/// <param name="Definicao">O que o dev quer, em texto livre.</param>
/// <param name="Anexos">Os anexos e as telas do Design citadas na definição (caminho ou <c>tela vNNN</c>).</param>
/// <param name="Autonomia">O Orquestrador dispara as ondas sozinho depois do plano aprovado.</param>
/// <param name="Paralelismo">Quantas execuções ao mesmo tempo, no máximo.</param>
/// <param name="DestinoDaPr">O branch para onde vai a PR do projeto.</param>
/// <param name="Situacao">Um de <see cref="ProjetosV1.Situacoes"/>.</param>
/// <param name="Versao">A versão lida: o envio com outra é conflito (409).</param>
/// <param name="Autor">Quem criou o projeto (o nome, ou o login): é quem o apaga, além do gestor.</param>
/// <param name="PodeApagar">Quem consulta pode apagar o projeto (o autor ou o gestor).</param>
public sealed record ProjetoV1(
    long Id, string Slug, string Nome, string Nivel, string Time, IReadOnlyList<string> Repositorios, string Branch,
    bool SemanaConvertida, string Definicao, IReadOnlyList<string> Anexos, bool Autonomia, int Paralelismo, string DestinoDaPr,
    string Situacao, int Versao, string Autor, DateTimeOffset AtualizadoEm, IReadOnlyList<ItemDoProjetoV1> Itens, bool PodeApagar);

/// <summary>
/// Um ITEM do plano do projeto: o trabalho com o fluxo certo, os repositórios e os componentes que toca, as dependências
/// (pelo <see cref="Numero"/> de outros itens) e a onda em que roda.
/// </summary>
/// <param name="Numero">O número do item no projeto (1, 2, 3…).</param>
/// <param name="Fluxo">O fluxo da execução (o nome do salvo, ou as marcas da <c>montar-fluxo</c>).</param>
/// <param name="Dependencias">Os números dos itens que precisam estar integrados antes deste.</param>
/// <param name="Onda">A onda do item (1, 2, 3…); 0 antes do plano.</param>
/// <param name="WorkItemId">O work item da execução; 0 sem item (tarefa avulsa).</param>
/// <param name="Situacao">Um de <see cref="ProjetosV1.SituacoesDoItem"/>.</param>
public sealed record ItemDoProjetoV1(
    int Numero, string Titulo, string Descricao, string Fluxo, IReadOnlyList<string> Repositorios, IReadOnlyList<string> Componentes,
    IReadOnlyList<int> Dependencias, int Onda, int WorkItemId, string Situacao);

/// <summary>
/// O envio de um projeto (o <c>PUT /api/projetos</c>): cria pelo (escopo, slug), ou substitui o que existe quando a
/// <see cref="Versao"/> é a lida. Os itens vêm inteiros — o plano é uma coisa só.
/// </summary>
/// <param name="Versao">A versão lida; nula para criar (com o slug já existente é conflito).</param>
public sealed record EnvioDeProjetoV1(
    string? Slug, string? Nome, string? Nivel, string? Time, IReadOnlyList<string>? Repositorios, string? Branch,
    bool SemanaConvertida, string? Definicao, IReadOnlyList<string>? Anexos, bool Autonomia, int Paralelismo, string? DestinoDaPr,
    string? Situacao, int? Versao, IReadOnlyList<ItemDoProjetoV1>? Itens);

/// <summary>Os nomes e os tetos do contrato v1 dos projetos (o dev.kit os confere antes de enviar).</summary>
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
