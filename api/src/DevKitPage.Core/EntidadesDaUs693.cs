namespace DevKitPage.Core;

/// <summary>
/// Um PROJETO do dev.kit (US #693), compartilhado por escopo no padrão do <see cref="ProjetoDeDesign"/> — <c>empresa</c>,
/// <c>time</c> ou <c>usuario</c>. Único por (<see cref="Espaco"/>, <see cref="Nivel"/>, <see cref="Time"/>,
/// <see cref="UsuarioId"/>, <see cref="Slug"/>). As listas (repositórios, anexos) vão uma por linha: são lidas e gravadas
/// inteiras, e uma tabela para cada uma só multiplicaria o esquema.
/// </summary>
public sealed class ProjetoCompartilhado
{
    public long Id { get; set; }

    /// <summary>A empresa do projeto; nula para quem não tem empresa (o projeto fica no nível usuário).</summary>
    public int? EmpresaId { get; set; }

    public Empresa? Empresa { get; set; }

    /// <summary><c>empresa:N</c>, ou <c>usuario:N</c> sem empresa (<see cref="RegrasDoDesign.EspacoDe"/>): a unicidade.</summary>
    public string Espaco { get; set; } = string.Empty;

    public string Nivel { get; set; } = string.Empty;

    public string Time { get; set; } = string.Empty;

    /// <summary>O dono do nível usuário; 0 nos outros.</summary>
    public int UsuarioId { get; set; }

    public string Slug { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;
    public string Documento { get; set; } = string.Empty;

    /// <summary>As URLs dos repositórios, uma por linha.</summary>
    public string Repositorios { get; set; } = string.Empty;

    public string Branch { get; set; } = string.Empty;
    public bool SemanaConvertida { get; set; }
    public string Definicao { get; set; } = string.Empty;

    /// <summary>Os anexos e as telas do Design, um por linha.</summary>
    public string Anexos { get; set; } = string.Empty;

    public bool Autonomia { get; set; }
    public int Paralelismo { get; set; }
    public string DestinoDaPr { get; set; } = string.Empty;
    public string Situacao { get; set; } = string.Empty;

    /// <summary>A versão do projeto: cada gravação soma um, e o envio com a versão velha é conflito.</summary>
    public int Versao { get; set; }

    /// <summary>Quem criou o projeto: é quem o apaga, além do gestor.</summary>
    public int AutorId { get; set; }

    public Usuario? Autor { get; set; }

    /// <summary>Quem gravou por último.</summary>
    public int AtualizadoPorId { get; set; }

    public DateTime CriadoEmUtc { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }

    public List<ItemDeProjetoCompartilhado> Itens { get; set; } = [];
}

/// <summary>Um item do plano do projeto (US #693): as listas uma por linha, as dependências pelos números, por vírgula.</summary>
public sealed class ItemDeProjetoCompartilhado
{
    public long Id { get; set; }
    public long ProjetoId { get; set; }
    public ProjetoCompartilhado? Projeto { get; set; }
    public int Numero { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;
    public string Fluxo { get; set; } = string.Empty;
    public string Repositorios { get; set; } = string.Empty;
    public string Componentes { get; set; } = string.Empty;
    public string Dependencias { get; set; } = string.Empty;
    public int Onda { get; set; }
    public int WorkItemId { get; set; }
    public string Situacao { get; set; } = string.Empty;
}
