namespace DevKitPage.Core;

/// <summary>
/// Um item do CONTEXTO de uma empresa (US #556) — a convenção da US #491 (<c>ConvencaoDaEmpresa</c>) migrada sem perda e
/// estendida com a ORIGEM e a VERSÃO. Único por (<see cref="EmpresaId"/>, <see cref="Nivel"/>, <see cref="Equipe"/>,
/// <see cref="UsuarioId"/>, <see cref="Chave"/>, <see cref="Origem"/>): a instrução do usuário e a sugestão do agente
/// sobre o mesmo tema coexistem. A equipe é um NOME — a Equipe das Configurações do dev.kit, a mesma do <c>{equipe}</c>
/// do branch —, e não um cadastro.
/// </summary>
public sealed class ItemDeContexto
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }

    /// <summary><c>usuario</c>, <c>equipe</c> ou <c>empresa</c> (<c>NiveisDeContexto</c>).</summary>
    public string Nivel { get; set; } = string.Empty;

    /// <summary>A equipe (normalizada) do item de nível equipe; vazio nos outros níveis.</summary>
    public string Equipe { get; set; } = string.Empty;

    /// <summary>O dono do item de nível usuário; 0 nos outros níveis.</summary>
    public int UsuarioId { get; set; }

    public string Chave { get; set; } = string.Empty;
    public string Valor { get; set; } = string.Empty;

    /// <summary><c>usuario</c> (instrução) ou <c>agente</c> (sugestão) — <c>OrigensDeContexto</c>.</summary>
    public string Origem { get; set; } = string.Empty;

    /// <summary>Soma 1 a cada gravação: a edição sobre uma versão velha é conflito.</summary>
    public int Versao { get; set; } = 1;

    public int AutorId { get; set; }
    public Usuario? Autor { get; set; }
    public DateTime AtualizadoEmUtc { get; set; }
}

/// <summary>
/// Quem participa de uma equipe numa empresa (US #556, A4) — o registro leve que diz "as suas equipes", sem cadastro de
/// times. O dev só PEDE (a Equipe das Configurações chega na sincronização e vira <c>pendente</c>); quem APROVA é o
/// gestor da empresa ou o admin do board. Só a participação aprovada dá acesso ao contexto da equipe.
/// </summary>
public sealed class ParticipanteDeEquipe
{
    public long Id { get; set; }
    public int EmpresaId { get; set; }
    public Empresa? Empresa { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }

    /// <summary>A equipe, normalizada (<c>RegrasDoContexto.NormalizarEquipe</c>).</summary>
    public string Equipe { get; set; } = string.Empty;

    /// <summary><c>pendente</c> ou <c>aprovado</c> (<c>SituacoesDeParticipacao</c>).</summary>
    public string Situacao { get; set; } = string.Empty;

    public DateTime AtualizadoEmUtc { get; set; }
}
