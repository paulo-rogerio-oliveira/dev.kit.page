namespace DevKitPage.Contracts.V1;

/// <summary>
/// Os catálogos NOMEADOS (US #461): o site passa a ser a fonte de publicação — o básico (que vale para todas as empresas)
/// e os catálogos que o admin cria e relaciona a empresas. O catálogo da empresa da US #425 (publicado pelos devs) é
/// outro nível e não muda.
/// </summary>
public static class Catalogos
{
    /// <summary>O slug do catálogo básico: o registro semeado, que não se vincula nem se exclui.</summary>
    public const string Basico = "basico";

    /// <summary>Os slugs que colidem com as rotas de <c>/api/catalogos</c> (o <c>itens</c> desde a US #687: a lista de todos).</summary>
    public static IReadOnlyList<string> Reservados { get; } = [Basico, "meus", "base", "empresa", "itens"];

    /// <summary>A tag da release <paramref name="versao"/>: <c>catalogo-vN</c> no básico (continua a numeração do GitHub), <c>{slug}-vN</c> nos demais.</summary>
    public static string Tag(string slug, int versao)
        => slug == Basico ? $"catalogo-v{versao}" : $"{slug}-v{versao}";
}

/// <summary>Uma empresa relacionada a um catálogo: os usuários dela recebem o catálogo no dev.kit.</summary>
public sealed record EmpresaVinculadaV1(int Id, string Nome, DateTimeOffset RelacionadaEm);

/// <summary>Um catálogo na gestão do admin (<c>GET /api/catalogos</c>).</summary>
/// <param name="Versao">A versão publicada mais nova (0 = nunca publicado).</param>
/// <param name="Itens">Os itens ativos (não retirados), publicados ou não.</param>
/// <param name="Pendentes">Os itens com rascunho que ainda não chegou às máquinas (novo, alterado ou retirado).</param>
public sealed record CatalogoV1(
    int Id, string Slug, string Nome, string Descricao, bool Basico, int Versao, DateTimeOffset? PublicadaEm, int Itens, int Pendentes,
    IReadOnlyList<EmpresaVinculadaV1> Empresas, DateTimeOffset CriadoEm);

/// <summary>Um catálogo novo: o slug (a identidade, imutável) e o nome que o dev.kit mostra na coluna Origem.</summary>
public sealed record CatalogoNovoV1(string Slug, string Nome, string? Descricao);

/// <summary>A edição do catálogo: o nome e a descrição (o slug não muda: é o que as máquinas guardam).</summary>
public sealed record CatalogoEditadoV1(string Nome, string? Descricao);

/// <summary>Relacionar uma empresa ao catálogo.</summary>
public sealed record VinculoV1(int EmpresaId);

/// <summary>As situações de um item editável em relação à última release do catálogo.</summary>
public static class SituacoesDoItem
{
    /// <summary>Nunca publicado: entra na próxima release.</summary>
    public const string Novo = "novo";

    /// <summary>Publicado e editado depois: a próxima release leva a versão nova.</summary>
    public const string Alterado = "alterado";

    /// <summary>Igual ao da última release.</summary>
    public const string Publicado = "publicado";

    /// <summary>Retirado no site, ainda presente na última release: a próxima o tira das máquinas.</summary>
    public const string RetiradaPendente = "retirada-pendente";

    /// <summary>Retirado e já fora da última release.</summary>
    public const string Retirado = "retirado";
}

/// <summary>
/// Um item editável de um catálogo (skill, Tech Plan ou fluxo). O <see cref="Conteudo"/> é o MESMO JSON que o dev.kit grava
/// (o <c>SkillJson</c>, o <c>TechPlanJson</c>, o envelope <c>devkit-fluxo</c>): na lista ele vem nulo, no detalhe vem inteiro.
/// </summary>
/// <param name="Nome">A identidade na máquina: o nome da skill, a chave fixa do plan, o slug do fluxo.</param>
/// <param name="Categoria">O grupo da vitrine (a tecnologia): o informado, ou o derivado do nome como no git.kit.</param>
/// <param name="VersaoPublicada">A versão do item na última release (0 = nunca publicado).</param>
/// <param name="Situacao">Um de <see cref="SituacoesDoItem"/>.</param>
/// <param name="Dependencias">O que o item usa (<c>tipo:nome</c>) — no fluxo, as skills das etapas e os plans embutidos.</param>
/// <param name="Catalogo">O slug do catálogo do item (US #687: a lista de todos os catálogos); nulo numa API anterior.</param>
/// <param name="Escopo">Para onde o item vai (<see cref="EscoposDoItem"/>, a classificação base/empresa/projeto do git.kit).</param>
/// <param name="Stack">A stack da Rule (<c>vb6</c>, <c>dotnet</c>…); vazia em qualquer stack e nos outros tipos.</param>
/// <param name="RuleDeImpacto">A Rule é de IMPACTO (o tipo <c>impacto</c>): nunca vai para o básico.</param>
public sealed record ItemEditavelV1(
    long Id, string Tipo, string Nome, string Descricao, string Categoria, string Hash, int VersaoPublicada, string Situacao,
    DateTimeOffset AlteradoEm, string AlteradoPor, IReadOnlyList<string> Dependencias, string? Conteudo,
    string? Catalogo = null, string? Escopo = null, string? Stack = null, bool RuleDeImpacto = false);

/// <summary>
/// Para onde vai um item (US #687) — a classificação do git.kit (<c>ClassificacaoDoCatalogo</c>) vista do site: o item do
/// catálogo básico é BASE (toda máquina); o de um catálogo relacionado a empresas é EMPRESA; o preso a um repositório (a
/// Rule com repositório, a skill com repositórios) é PROJETO. A Rule de impacto nunca é base.
/// </summary>
public static class EscoposDoItem
{
    public const string Base = "base";
    public const string Empresa = "empresa";
    public const string Projeto = "projeto";

    public static IReadOnlyList<string> Todos { get; } = [Base, Empresa, Projeto];
}

/// <summary>Uma linha da comparação do rascunho com a versão publicada.</summary>
/// <param name="Tipo">Um de <see cref="TiposDeLinha"/>.</param>
/// <param name="Numero">O número da linha no rascunho (nulo na removida).</param>
public sealed record LinhaDaDiferencaV1(string Tipo, string Texto, int? Numero);

/// <summary>Os tipos de linha da comparação.</summary>
public static class TiposDeLinha
{
    public const string Igual = "igual";
    public const string Adicionada = "adicionada";
    public const string Removida = "removida";
}

/// <summary>
/// As diferenças do rascunho de um item com a versão publicada (<c>GET .../itens/{id}/diferencas</c>), no TEXTO que o
/// editor mostra: o SKILL.md da skill (o frontmatter e o conteúdo), o conteúdo da Rule, o JSON do fluxo.
/// </summary>
/// <param name="VersaoPublicada">A versão comparada (0 = nunca publicado: tudo é linha nova).</param>
/// <param name="Publicado">O texto da versão publicada (vazio quando não há).</param>
/// <param name="Rascunho">O texto de agora.</param>
public sealed record DiferencasDoItemV1(int VersaoPublicada, string Publicado, string Rascunho, IReadOnlyList<LinhaDaDiferencaV1> Linhas, int Adicionadas, int Removidas);

/// <summary>Uma linha do histórico do item: o rascunho salvo, descartado ou retirado, ou a versão que uma release publicou.</summary>
/// <param name="Acao">Uma de <see cref="AcoesNoItem"/>.</param>
/// <param name="Versao">A versão do item publicada (na publicação) ou a de partida; 0 quando não se aplica.</param>
/// <param name="Release">A tag da release (na publicação); vazia nas outras ações.</param>
public sealed record HistoricoDoItemV1(DateTimeOffset Em, string Acao, int Versao, string Release, string Autor);

/// <summary>As ações do histórico de um item do catálogo.</summary>
public static class AcoesNoItem
{
    public const string Rascunho = "rascunho";
    public const string Descartado = "descartado";
    public const string Retirado = "retirado";
    public const string Publicado = "publicado";
}

/// <summary>Um arquivo anexo a um item (material de apoio de quem edita: fica no site, a máquina não o recebe).</summary>
public sealed record AnexoDoItemV1(long Id, string Nome, string TipoDeConteudo, long TamanhoBytes, string Autor, DateTimeOffset Em);

/// <summary>Os limites dos anexos de um item.</summary>
public static class AnexosDoItem
{
    /// <summary>O tamanho máximo de um anexo: 1 MB.</summary>
    public const int TamanhoMaximo = 1024 * 1024;

    /// <summary>Quantos anexos um item tem no máximo.</summary>
    public const int MaximoPorItem = 10;
}

/// <summary>
/// Criar ou editar um item: o <paramref name="Conteudo"/> é o JSON no formato do dev.kit (a skill do <c>SkillJson</c>, o plan
/// do <c>TechPlanJson</c>, o envelope <c>devkit-fluxo</c>). O nome e a descrição saem dele; a <paramref name="Categoria"/>
/// vazia é a derivada do nome.
/// </summary>
public sealed record ItemEditadoV1(string Tipo, string Conteudo, string? Categoria = null);

/// <summary>
/// A conferência de uma publicação (<c>GET /api/catalogos/{slug}/publicacao</c>) — o que o Publicar mostra antes de
/// confirmar, e o corpo do 409 quando a publicação é recusada.
/// </summary>
/// <param name="Mudancas">As linhas do relatório, no formato do gerador do git.kit: <c>+</c> novo, <c>~</c> versão nova, <c>-</c> retirado, <c>·</c> dados da vitrine.</param>
/// <param name="Problemas">O que impede a publicação: dependência sem item, colisão com o básico, conteúdo que não abre.</param>
/// <param name="MinDevKit">O dev.kit mínimo que os itens novos e alterados vão exigir (o do manifesto anterior, sem outro informado).</param>
public sealed record RelatorioDePublicacaoV1(
    string Slug, int VersaoAtual, int Proxima, bool PodePublicar, int MinDevKit, IReadOnlyList<string> Mudancas, IReadOnlyList<string> Problemas);

/// <summary>O pedido de publicação: o dev.kit mínimo dos itens que mudaram (nulo = o do manifesto anterior).</summary>
public sealed record PublicarCatalogoV1(int? MinDevKit = null);

/// <summary>O que a publicação gravou: a release nova e o relatório.</summary>
public sealed record PublicacaoDoCatalogoV1(ReleaseDoCatalogoV1 Release, RelatorioDePublicacaoV1 Relatorio);

/// <summary>
/// Um catálogo que o dev.kit de quem consulta sincroniza (<c>GET /api/catalogos/meus</c>): o básico, para todos, e os
/// relacionados à empresa do usuário. O slug e o nome são o que a máquina grava na procedência de cada item.
/// </summary>
/// <param name="Versao">A última versão publicada (0 = ainda sem release: nada a baixar).</param>
public sealed record CatalogoDisponivelV1(string Slug, string Nome, bool Basico, int Versao, DateTimeOffset? PublicadaEm);

/// <summary>O resultado da importação da release <c>catalogo-vN</c> do GitHub para o básico do site.</summary>
public sealed record ImportacaoDoBasicoV1(int Versao, int Itens, int Retirados, bool Importada, string Mensagem);
