namespace DevKitPage.Core;

/// <summary>
/// Um catálogo NOMEADO (US #461): o básico (o registro semeado com <see cref="Basico"/>, que vale para todas as empresas e
/// não se vincula nem se exclui) ou um catálogo que o admin cria e relaciona a empresas. O <see cref="Slug"/> é a
/// identidade que as máquinas guardam na procedência de cada item: não muda depois de criado.
/// </summary>
public sealed class Catalogo
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public bool Basico { get; set; }
    public DateTime CriadoEmUtc { get; set; }
}

/// <summary>
/// Uma empresa relacionada a um catálogo (N:N): os usuários dela recebem o catálogo no dev.kit. O nome não é
/// <c>CatalogoDaEmpresa</c> (o do planejamento) porque esse já é o serviço do catálogo da empresa da US #425.
/// </summary>
public sealed class VinculoDeCatalogo
{
    public long Id { get; set; }
    public int CatalogoId { get; set; }
    public Catalogo? Catalogo { get; set; }
    public int EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
    public DateTime RelacionadoEmUtc { get; set; }
}

/// <summary>
/// Um item EDITÁVEL de um catálogo nomeado (US #461) — nome distinto do <see cref="ItemDeCatalogo"/> da US #425 (o que
/// os devs publicam) para não confundir os dois níveis. Editar grava o RASCUNHO: nada chega às máquinas antes de
/// publicar, e a situação (novo, alterado, publicado, retirado) é a comparação com o manifesto da última release.
/// Único por (<see cref="CatalogoId"/>, <see cref="Tipo"/>, <see cref="Nome"/>).
/// </summary>
public sealed class ItemDoCatalogoEditavel
{
    public long Id { get; set; }
    public int CatalogoId { get; set; }
    public Catalogo? Catalogo { get; set; }

    /// <summary>skill, plan ou fluxo.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>A identidade na máquina: o nome da skill, a chave fixa do plan, o slug do fluxo.</summary>
    public string Nome { get; set; } = string.Empty;

    public string Descricao { get; set; } = string.Empty;

    /// <summary>O grupo da vitrine informado pelo admin; vazio é o derivado do nome (a regra do gerador).</summary>
    public string Categoria { get; set; } = string.Empty;

    /// <summary>O caminho no zip (<c>skills/x.json</c>): o do manifesto importado, ou o da pasta do tipo com o nome.</summary>
    public string Arquivo { get; set; } = string.Empty;

    /// <summary>O JSON do item no formato do dev.kit (até 256 KB).</summary>
    public string Conteudo { get; set; } = string.Empty;

    /// <summary>O hash do conteúdo pela regra do manifesto — é o que decide se a próxima release leva versão nova.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>As dependências (<c>tipo:nome</c>), uma por linha — as do fluxo.</summary>
    public string Dependencias { get; set; } = string.Empty;

    public DateTime AlteradoEmUtc { get; set; }
    public string AlteradoPor { get; set; } = string.Empty;
    public DateTime? RetiradoEmUtc { get; set; }
}

/// <summary>
/// Uma release de um catálogo publicada PELO SITE (US #461): a tag (<c>catalogo-vN</c> no básico, <c>{slug}-vN</c> nos
/// demais), o manifesto e o hash do zip. O zip fica à parte (<see cref="PacoteDoCatalogo"/>, pelo
/// <see cref="IArmazemDePacotes"/>), para a lista das releases não carregar os bytes.
/// </summary>
public sealed class VersaoDoCatalogo
{
    public long Id { get; set; }
    public int CatalogoId { get; set; }
    public Catalogo? Catalogo { get; set; }
    public int Versao { get; set; }
    public string Tag { get; set; } = string.Empty;

    /// <summary>O <c>manifesto.json</c> da release, como foi para o zip.</summary>
    public string Manifesto { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
    public long TamanhoBytes { get; set; }
    public int Itens { get; set; }
    public int Retirados { get; set; }
    public DateTime PublicadaEmUtc { get; set; }
    public string PublicadaPor { get; set; } = string.Empty;
}

/// <summary>O zip de uma <see cref="VersaoDoCatalogo"/> no banco (o armazém de hoje: o pacote é pequeno, 0,1 MB).</summary>
public sealed class PacoteDoCatalogo
{
    public long VersaoId { get; set; }
    public byte[] Zip { get; set; } = [];
}
