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

    /// <summary>Os slugs que colidem com as rotas de <c>/api/catalogos</c>.</summary>
    public static IReadOnlyList<string> Reservados { get; } = [Basico, "meus", "base", "empresa"];

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
public sealed record ItemEditavelV1(
    long Id, string Tipo, string Nome, string Descricao, string Categoria, string Hash, int VersaoPublicada, string Situacao,
    DateTimeOffset AlteradoEm, string AlteradoPor, IReadOnlyList<string> Dependencias, string? Conteudo);

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
