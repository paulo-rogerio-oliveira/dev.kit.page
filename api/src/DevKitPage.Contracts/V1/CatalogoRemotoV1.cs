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
/// <param name="Agentes">
/// (campo novo, correções do board remoto) Os agentes que a máquina roda, <c>cli</c> ou <c>cli:modelo</c> — o chip de MODELO do
/// fluxo no chat. Até <see cref="RemotoV1.MaximoNoCatalogo"/>. O dev.kit antigo não o manda.
/// </param>
/// <param name="Membros">
/// (campo novo) Os membros do time do board, pelo nome de exibição — o seletor do parâmetro de usuário do fluxo. Até
/// <see cref="RemotoV1.MaximoNoCatalogo"/>.
/// </param>
public sealed record CatalogoRemotoV1(
    int Contrato, string MaquinaId, DateTimeOffset GeradoEm, string? ComandoId,
    IReadOnlyList<WorkItemNoCatalogoV1>? WorkItems, IReadOnlyList<RepositorioNoCatalogoV1>? Repositorios,
    IReadOnlyList<FluxoNoCatalogoV1>? Fluxos, IReadOnlyList<ProjetoNoCatalogoV1>? Projetos, IReadOnlyList<DesignNoCatalogoV1>? Designs,
    IReadOnlyList<string>? Agentes = null, IReadOnlyList<string>? Membros = null);

/// <summary>Um work item em andamento do dev: o número que o atalho <c>#</c> leva, o título, o tipo e o estado.</summary>
public sealed record WorkItemNoCatalogoV1(int Id, string Titulo, string? Tipo = null, string? Estado = null);

/// <summary>Um repositório cadastrado no dev.kit: o nome que o atalho <c>@</c> leva e o branch padrão.</summary>
public sealed record RepositorioNoCatalogoV1(string Nome, string? BranchPadrao = null);

/// <summary>Um fluxo do catálogo (salvo ou básico): o nome que o atalho <c>/</c> leva.</summary>
/// <param name="Id">(campo novo) O id do fluxo salvo — o <c>fluxo</c> da mensagem; ausente no dev.kit antigo.</param>
/// <param name="Parametros">(campo novo) Os parâmetros que o fluxo pede, cada um com o tipo, as opções e o padrão.</param>
/// <param name="AgentePadrao">(campo novo) O agente padrão da task do fluxo, <c>cli</c> ou <c>cli:modelo</c>.</param>
public sealed record FluxoNoCatalogoV1(
    string Nome, string? Descricao = null, long? Id = null, IReadOnlyList<ParametroDoFluxoV1>? Parametros = null, string? AgentePadrao = null);

/// <summary>
/// Um parâmetro do fluxo como o chat do site o pede: o nome (o <c>{NOME}</c>), o rótulo, o tipo (um de
/// <see cref="RemotoV1.TiposDeParametro"/>), as opções da lista (ou as sugestões do texto), a marca de usuário do board, se é
/// obrigatório e o padrão.
/// </summary>
public sealed record ParametroDoFluxoV1(
    string Nome, string? Rotulo = null, string Tipo = RemotoV1.ParametroTexto, IReadOnlyList<string>? Opcoes = null, bool Usuario = false,
    bool Obrigatorio = true, string? Padrao = null);

/// <summary>Um projeto do Orquestrador: o slug, o nome e a situação (<see cref="ProjetosV1.Situacoes"/>).</summary>
public sealed record ProjetoNoCatalogoV1(string Slug, string Nome, string? Situacao = null);

/// <summary>Um projeto de design (a aba Design do dev.kit): o nome e quantas telas tem.</summary>
/// <param name="Repositorio">
/// (campo novo, item 4) O repositório do projeto, pelo mesmo nome de <see cref="RepositorioNoCatalogoV1.Nome"/>: o site abre por
/// ele o projeto de design compartilhado (<see cref="ProjetoDeDesignV1.Chave"/>). O dev.kit antigo não o manda.
/// </param>
/// <param name="Miniatura">
/// (campo novo, correções do board remoto) O link da API de design da última versão ENVIADA do projeto
/// (<c>/api/design/{projeto}/{tela}/v{n}</c>): a miniatura do card do painel. Ausente sem versão no site.
/// </param>
public sealed record DesignNoCatalogoV1(string Nome, int Telas = 0, string? Repositorio = null, string? Miniatura = null);

/// <summary>A resposta do envio do catálogo. <see cref="Aplicado"/> falso é o catálogo mais antigo que o gravado: aceito, mas descartado.</summary>
public sealed record CatalogoRecebidoV1(string MaquinaId, DateTimeOffset RecebidoEm, bool Aplicado);

/// <summary>O último catálogo de uma máquina do usuário, como o site o lê: quando a API o recebeu e o envio do dev.kit.</summary>
public sealed record CatalogoDaMaquinaV1(string MaquinaId, DateTimeOffset RecebidoEm, CatalogoRemotoV1 Catalogo);
