namespace DevKitPage.Core;

/// <summary>
/// Um PROJETO DE DESIGN (US #669): as telas de um REPOSITÓRIO (a <see cref="Chave"/> normalizada) num nível de
/// compartilhamento, no padrão da <see cref="ConvencaoDaEmpresa"/> — <c>empresa</c>, <c>time</c> ou <c>usuario</c>. Único
/// por (<see cref="Espaco"/>, <see cref="Nivel"/>, <see cref="Time"/>, <see cref="UsuarioId"/>, <see cref="Chave"/>). O
/// branch não separa projetos: é atributo de cada <see cref="VersaoDeDesign"/>.
/// </summary>
public sealed class ProjetoDeDesign
{
    public long Id { get; set; }

    /// <summary>A empresa do projeto; nula para quem não tem empresa (o projeto fica no nível usuário).</summary>
    public int? EmpresaId { get; set; }

    public Empresa? Empresa { get; set; }

    /// <summary>
    /// De quem é o TETO que o projeto consome (<see cref="RegrasDoDesign.EspacoDe"/>): <c>empresa:N</c>, ou
    /// <c>usuario:N</c> para quem não tem empresa. Também é a unicidade — o EmpresaId nulo não seria.
    /// </summary>
    public string Espaco { get; set; } = string.Empty;

    /// <summary><c>empresa</c>, <c>time</c> ou <c>usuario</c> (<c>NiveisDeConvencao</c>).</summary>
    public string Nivel { get; set; } = string.Empty;

    /// <summary>O time (normalizado) do nível time; vazio nos outros.</summary>
    public string Time { get; set; } = string.Empty;

    /// <summary>O dono do nível usuário; 0 nos outros.</summary>
    public int UsuarioId { get; set; }

    /// <summary>O caminho como veio na primeira versão.</summary>
    public string Repositorio { get; set; } = string.Empty;

    /// <summary>O caminho normalizado (<c>DesignV1.ChaveDoRepositorio</c>).</summary>
    public string Chave { get; set; } = string.Empty;

    public DateTime CriadoEmUtc { get; set; }

    /// <summary>A última versão enviada ou apagada: a sincronização incremental (<c>?desde=</c>) compara com ela.</summary>
    public DateTime AtualizadoEmUtc { get; set; }

    public List<TelaDeDesign> Telas { get; set; } = [];
}

/// <summary>Uma tela do projeto: o identificador do dev.kit (letras, números, - e _), o nome e o estado.</summary>
public sealed class TelaDeDesign
{
    public long Id { get; set; }
    public long ProjetoId { get; set; }
    public ProjetoDeDesign? Projeto { get; set; }
    public string Identificador { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public List<VersaoDeDesign> Versoes { get; set; } = [];
}

/// <summary>
/// Uma VERSÃO imutável de uma tela. A imagem NÃO fica no banco: está no armazém (<see cref="IArmazemDeImagens"/>, o
/// blob), na <see cref="ChaveDaImagem"/>. Única por (<see cref="TelaId"/>, <see cref="Numero"/>): o reenvio do mesmo PNG
/// (o mesmo <see cref="Sha256"/>) devolve esta versão, e um PNG diferente com o mesmo número é conflito.
/// </summary>
public sealed class VersaoDeDesign
{
    public long Id { get; set; }
    public long TelaId { get; set; }
    public TelaDeDesign? Tela { get; set; }
    public int Numero { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public int? Base { get; set; }
    public string Tema { get; set; } = string.Empty;
    public int Largura { get; set; }
    public int Altura { get; set; }
    public string Origem { get; set; } = string.Empty;
    public string Alvo { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Commit { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public int AutorId { get; set; }
    public Usuario? Autor { get; set; }

    /// <summary><c>usuario</c> ou <c>agente</c>.</summary>
    public string AutorTipo { get; set; } = string.Empty;

    public string Runner { get; set; } = string.Empty;

    /// <summary>Quando a versão foi gravada no dev.kit (a migração preserva a data original).</summary>
    public DateTime CriadaEmUtc { get; set; }

    /// <summary>Quando ela chegou aqui.</summary>
    public DateTime EnviadaEmUtc { get; set; }

    public string Sha256 { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string ChaveDaImagem { get; set; } = string.Empty;
}
