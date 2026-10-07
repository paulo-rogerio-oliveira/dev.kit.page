namespace DevKitPage.Core;

/// <summary>Um usuário do dashboard. A senha só existe como hash (PasswordHasher do Identity).</summary>
public sealed class Usuario
{
    public int Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public bool EhAdmin { get; set; }

    /// <summary>O admin semeado nasce com isto ligado: só a troca de senha é acessível até ele trocar.</summary>
    public bool DeveTrocarSenha { get; set; }

    /// <summary>Falhas de login seguidas — o bloqueio vem depois de <see cref="OpcoesDeAutenticacao.MaxFalhas"/>.</summary>
    public int FalhasSeguidas { get; set; }

    public DateTime? BloqueadoAteUtc { get; set; }
    public DateTime CriadoEmUtc { get; set; }
}

/// <summary>Uma máquina com o dev.kit que envia telemetria. A chave só existe como hash.</summary>
public sealed class Maquina
{
    public int Id { get; set; }

    /// <summary>O id ANÔNIMO que o dev.kit gerou (um GUID) — nunca o hostname.</summary>
    public string MaquinaId { get; set; } = string.Empty;

    public string Apelido { get; set; } = string.Empty;
    public string ChaveHash { get; set; } = string.Empty;
    public string VersaoDevKit { get; set; } = string.Empty;
    public DateTime RegistradaEmUtc { get; set; }
    public DateTime? UltimoEnvioEmUtc { get; set; }
}

/// <summary>
/// Um evento de uso BRUTO — a linha do log do dashboard. Guardado por
/// <see cref="OpcoesDeTelemetria.RetencaoDias"/> e depois expurgado; os números do dashboard não
/// dependem dele (vêm do <see cref="TotalDiario"/>).
/// </summary>
public sealed class EventoDeUso
{
    public long Id { get; set; }

    /// <summary>Único: é ele que torna o reenvio inofensivo.</summary>
    public string EventId { get; set; } = string.Empty;

    public int MaquinaId { get; set; }
    public Maquina? Maquina { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string SessaoId { get; set; } = string.Empty;
    public int Quantidade { get; set; }
    public long? Valor { get; set; }
    public string Detalhe { get; set; } = string.Empty;
    public DateTime EmUtc { get; set; }

    /// <summary>O dia (UTC) do evento — a chave da consolidação diária.</summary>
    public DateOnly Dia { get; set; }

    public DateTime RecebidoEmUtc { get; set; }
}

/// <summary>
/// Um pedido de demonstração vindo do formulário da landing. Guardado por
/// <see cref="OpcoesDeDemonstracao.RetencaoDias"/> e depois expurgado (LGPD); lido e excluído só
/// pelo usuário autenticado do dashboard.
/// </summary>
public sealed class PedidoDeDemonstracao
{
    public long Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Empresa { get; set; } = string.Empty;
    public string Mensagem { get; set; } = string.Empty;

    /// <summary>Quando o visitante deu o consentimento — o mesmo instante do envio.</summary>
    public DateTime ConsentimentoEmUtc { get; set; }

    public DateTime RecebidoEmUtc { get; set; }
}

/// <summary>
/// Um GRUPO de exceção não classificada (US #381): todas as ocorrências com a mesma assinatura.
/// Criado (ou atualizado) na ingestão, na MESMA transação do evento, e nunca expurgado — é ele que
/// guarda o estado da reação. As contagens por período e máquina não ficam aqui: saem dos
/// <see cref="TotalDiario"/> (tipo <c>ExcecaoNaoClassificada</c>, recorte = a assinatura).
/// </summary>
public sealed class GrupoDeErro
{
    public long Id { get; set; }

    /// <summary>Única: é a chave do agrupamento.</summary>
    public string Assinatura { get; set; } = string.Empty;

    /// <summary>O tipo da exceção (a primeira linha do trace).</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Um dos <c>EstadosDoGrupo</c>.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>A versão em que se espera que pare; a ocorrência numa versão igual ou maior é regressão.</summary>
    public string? ResolvidoNaVersao { get; set; }

    public string PrimeiraVersao { get; set; } = string.Empty;
    public string UltimaVersao { get; set; } = string.Empty;
    public DateTime PrimeiroVistoEmUtc { get; set; }
    public DateTime UltimoVistoEmUtc { get; set; }
}

/// <summary>
/// Uma ocorrência GUARDADA de um grupo, com o trace dela. Só as últimas
/// <see cref="RegrasDeErro.OcorrenciasGuardadasPorGrupo"/> de cada grupo ficam, e o expurgo diário
/// apaga as que passaram de <see cref="OpcoesDeTelemetria.RetencaoDias"/> — o grupo fica.
/// </summary>
public sealed class OcorrenciaDeErro
{
    public long Id { get; set; }
    public long GrupoId { get; set; }
    public GrupoDeErro? Grupo { get; set; }
    public int MaquinaId { get; set; }
    public Maquina? Maquina { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string VersaoDevKit { get; set; } = string.Empty;
    public string Trace { get; set; } = string.Empty;
    public DateTime EmUtc { get; set; }
}

/// <summary>
/// O total CONSOLIDADO de um tipo (e recorte) por máquina e dia. Atualizado na ingestão, junto com
/// o evento bruto, e nunca tocado pelo expurgo: é por isso que o histórico do dashboard sobrevive à
/// retenção dos eventos.
/// </summary>
public sealed class TotalDiario
{
    public long Id { get; set; }
    public int MaquinaId { get; set; }
    public DateOnly Dia { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Detalhe { get; set; } = string.Empty;

    /// <summary>A soma das quantidades.</summary>
    public long Quantidade { get; set; }

    /// <summary>Quantos eventos entraram na soma.</summary>
    public long Eventos { get; set; }

    /// <summary>A soma das medidas (duração, nota) — a média é ela sobre <see cref="Eventos"/>.</summary>
    public long Valor { get; set; }
}
