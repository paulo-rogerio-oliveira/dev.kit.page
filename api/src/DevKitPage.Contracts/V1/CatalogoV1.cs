namespace DevKitPage.Contracts.V1;

/// <summary>
/// Os tipos de item de catálogo (US #425): a skill, o Tech Plan e o fluxo — os três seguem a mesma regra de
/// importação no dev.kit.
/// </summary>
public static class TiposDeItem
{
    public const string Skill = "skill";
    public const string Plan = "plan";
    public const string Fluxo = "fluxo";

    public static IReadOnlyList<string> Todos { get; } = [Skill, Plan, Fluxo];
}

/// <summary>
/// O manifesto do catálogo BÁSICO (US #425) — o <c>manifesto.json</c> que o <c>build/catalogo.ps1</c> do git.kit
/// gera, versiona no repositório e anexa à release <c>catalogo-vN</c>. É o MESMO JSON do contrato
/// <c>ManifestoDoCatalogo</c> do git.kit (camelCase): mudar um nome aqui quebra a sincronia das máquinas.
/// </summary>
/// <param name="Versao">A versão do catálogo — o N da tag <c>catalogo-vN</c>; sobe a cada item novo, alterado ou retirado.</param>
/// <param name="MinDevKit">A menor versão do dev.kit que lê este manifesto.</param>
/// <param name="Itens">Os itens publicados (na delta, só os que mudaram depois de <c>desde</c>).</param>
/// <param name="Retirados">Os itens que saíram do catálogo (na delta, só os retirados depois de <c>desde</c>).</param>
public sealed record ManifestoDoCatalogoV1(
    int Versao, int MinDevKit, IReadOnlyList<ItemDoCatalogoV1> Itens, IReadOnlyList<ItemRetiradoV1> Retirados);

/// <summary>Um item do catálogo básico no manifesto.</summary>
/// <param name="Tipo">Um de <see cref="TiposDeItem"/>.</param>
/// <param name="Nome">O nome (a identidade: a skill, a chave fixa do Tech Plan, o id do fluxo).</param>
/// <param name="Versao">A versão do ITEM: sobe quando o hash muda.</param>
/// <param name="Hash">O SHA-256 do JSON do item sem Id e com as quebras de linha normalizadas.</param>
/// <param name="Catalogo">A versão do catálogo em que o item mudou por último — é por ela que a delta filtra.</param>
/// <param name="Arquivo">O caminho do item dentro do zip do catálogo.</param>
/// <param name="Descricao">A descrição (o que a vitrine mostra).</param>
/// <param name="Tecnologia">O agrupamento da vitrine (Angular, .NET, Git, Azure DevOps, dev.kit…).</param>
/// <param name="MinDevKit">A menor versão do dev.kit que usa o item: a máquina mais velha fica na versão anterior.</param>
/// <param name="Requer">As ferramentas que a skill usa (derivadas dos tokens de ferramenta).</param>
/// <param name="Dependencias">Os itens que este usa (<c>tipo:nome</c>) — o fluxo traz as skills e os plans dele.</param>
/// <param name="Hashes">Os hashes de TODAS as versões publicadas: a máquina que já tem o item o reconhece como do básico.</param>
public sealed record ItemDoCatalogoV1(
    string Tipo, string Nome, int Versao, string Hash, int Catalogo, string Arquivo, string Descricao, string Tecnologia,
    int MinDevKit, IReadOnlyList<string> Requer, IReadOnlyList<string> Dependencias, IReadOnlyList<string> Hashes);

/// <summary>Um item que saiu do catálogo básico, e em que versão do catálogo.</summary>
public sealed record ItemRetiradoV1(string Tipo, string Nome, int Versao, int Catalogo);

/// <summary>
/// A resposta de <c>GET /api/catalogo/base?desde=N</c>: o que mudou depois da versão <paramref name="Desde"/> e o
/// pacote para baixar. Sem mudança, a rota responde 304 e nada disto.
/// </summary>
/// <param name="UrlDownload">A rota da API que entrega o zip do catálogo (anônima, em streaming).</param>
/// <param name="Sha256">O SHA-256 do zip (o <c>.sha256</c> publicado ao lado), quando há.</param>
public sealed record CatalogoBaseV1(int Desde, ManifestoDoCatalogoV1 Manifesto, string UrlDownload, string? Sha256);

/// <summary>Um item da vitrine pública (<c>GET /api/catalogo/base/vitrine</c>): o que o visitante lê, sem conteúdo.</summary>
public sealed record ItemDaVitrineV1(string Tipo, string Nome, string Descricao, string Tecnologia, int Versao);

/// <summary>A vitrine do catálogo básico: a versão do catálogo e os itens agrupados por tecnologia.</summary>
public sealed record VitrineV1(int Versao, DateTimeOffset PublicadaEm, IReadOnlyList<GrupoDaVitrineV1> Grupos);

/// <summary>Os itens de uma tecnologia, em ordem de nome.</summary>
public sealed record GrupoDaVitrineV1(string Tecnologia, IReadOnlyList<ItemDaVitrineV1> Itens);

/// <summary>Uma release do catálogo básico (<c>catalogo-vN</c>) — o que o admin acompanha em <c>/catalogo-base</c>.</summary>
public sealed record ReleaseDoCatalogoV1(int Versao, string Tag, string Nome, DateTimeOffset PublicadaEm, int Itens, int Retirados, long TamanhoBytes, string? Sha256);

// ----- Fase 2: o catálogo da EMPRESA -----

/// <summary>A publicação de um item no catálogo da empresa (o <c>devcli skills --publicar</c>).</summary>
/// <param name="Conteudo">O JSON do item (o mesmo que o dev.kit exporta), até 256 KB.</param>
/// <param name="Dependencias">Os itens que ele usa (<c>tipo:nome</c>) — assinar traz junto.</param>
public sealed record PublicacaoV1(string Tipo, string Nome, string Descricao, string Conteudo, IReadOnlyList<string>? Dependencias = null);

/// <summary>O desfecho da publicação: a versão gravada, ou a mesma quando o hash não mudou.</summary>
/// <param name="Nova">Falso quando o conteúdo é igual ao da versão atual (mesmo hash): nada foi gravado.</param>
public sealed record PublicacaoFeitaV1(long Id, string Tipo, string Nome, int Versao, string Hash, bool Nova);

/// <summary>Um item do catálogo da empresa, na lista de quem é dela.</summary>
/// <param name="Assinantes">Quantos usuários da empresa assinam o item.</param>
/// <param name="Assinado">Quem consulta assina o item.</param>
/// <param name="Retirado">Quando o item foi retirado (não aparece para assinar, e a sincronia o tira das máquinas).</param>
public sealed record ItemDaEmpresaV1(
    long Id, string Tipo, string Nome, string Descricao, int Versao, string Hash, string Autor, int Assinantes, bool Assinado,
    DateTimeOffset PublicadoEm, DateTimeOffset? Retirado, IReadOnlyList<string> Dependencias);

/// <summary>Um item assinado, com o conteúdo da versão atual — o que a sincronia do dev.kit grava na máquina.</summary>
public sealed record ItemAssinadoV1(long Id, string Tipo, string Nome, int Versao, string Hash, string Conteudo, bool Retirado);

/// <summary>O que assinar fez: o item e as dependências que vieram junto.</summary>
public sealed record AssinaturaFeitaV1(long Id, IReadOnlyList<string> Trouxe);
