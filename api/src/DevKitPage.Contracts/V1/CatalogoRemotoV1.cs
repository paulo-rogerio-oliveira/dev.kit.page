namespace DevKitPage.Contracts.V1;

/// <summary>
/// O CATÁLOGO de uma máquina (o handshake do chat remoto, projeto remote-control-v3): o que o dev.kit tem para o dev escolher
/// nos atalhos do chat do Board — os work items em andamento (<c>#</c>), os repositórios cadastrados (<c>@</c>), os fluxos
/// do catálogo, os projetos e os projetos de design (<c>/</c>). O site o pede com o comando
/// <see cref="ConversaV1.ComandoCatalogo"/> (ao conectar e no "Atualizar"), e o dev.kit responde com este envio, já
/// sanitizado. É uma FOTO inteira, como o retrato: o mais antigo que chegar depois (pelo <see cref="GeradoEm"/>) não volta o
/// catálogo para trás. A API guarda o último por máquina, com a data, e o site o mostra também com o dev.kit fechado.
/// </summary>
/// <param name="ComandoId">O comando de handshake que este envio responde (vazio no envio espontâneo).</param>
public sealed record CatalogoRemotoV1(
    int Contrato, string MaquinaId, DateTimeOffset GeradoEm, string? ComandoId,
    IReadOnlyList<WorkItemNoCatalogoV1>? WorkItems, IReadOnlyList<RepositorioNoCatalogoV1>? Repositorios,
    IReadOnlyList<FluxoNoCatalogoV1>? Fluxos, IReadOnlyList<ProjetoNoCatalogoV1>? Projetos, IReadOnlyList<DesignNoCatalogoV1>? Designs);

/// <summary>Um work item em andamento do dev: o número que o atalho <c>#</c> leva, o título, o tipo e o estado.</summary>
public sealed record WorkItemNoCatalogoV1(int Id, string Titulo, string? Tipo = null, string? Estado = null);

/// <summary>Um repositório cadastrado no dev.kit: o nome que o atalho <c>@</c> leva e o branch padrão.</summary>
public sealed record RepositorioNoCatalogoV1(string Nome, string? BranchPadrao = null);

/// <summary>Um fluxo do catálogo (salvo ou básico): o nome que o atalho <c>/</c> leva.</summary>
public sealed record FluxoNoCatalogoV1(string Nome, string? Descricao = null);

/// <summary>Um projeto do Orquestrador: o slug, o nome e a situação (<see cref="ProjetosV1.Situacoes"/>).</summary>
public sealed record ProjetoNoCatalogoV1(string Slug, string Nome, string? Situacao = null);

/// <summary>Um projeto de design (a aba Design do dev.kit): o nome e quantas telas tem.</summary>
public sealed record DesignNoCatalogoV1(string Nome, int Telas = 0);

/// <summary>A resposta do envio do catálogo. <see cref="Aplicado"/> falso é o catálogo mais antigo que o gravado: aceito, mas descartado.</summary>
public sealed record CatalogoRecebidoV1(string MaquinaId, DateTimeOffset RecebidoEm, bool Aplicado);

/// <summary>O último catálogo de uma máquina do usuário, como o site o lê: quando a API o recebeu e o envio do dev.kit.</summary>
public sealed record CatalogoDaMaquinaV1(string MaquinaId, DateTimeOffset RecebidoEm, CatalogoRemotoV1 Catalogo);
