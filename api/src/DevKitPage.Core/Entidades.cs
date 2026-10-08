namespace DevKitPage.Core;

/// <summary>Um usuário do dashboard. A senha só existe como hash (PasswordHasher do Identity).</summary>
public sealed class Usuario
{
    public int Id { get; set; }
    public string Login { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public bool EhAdmin { get; set; }

    /// <summary>
    /// Um de <c>Papeis</c> (US #381): <c>admin</c> (o semeado; <see cref="EhAdmin"/> ligado) ou
    /// <c>gestor</c> (de UMA empresa, <see cref="EmpresaId"/>). É a claim <c>role</c> do token.
    /// </summary>
    public string Papel { get; set; } = "admin";

    /// <summary>A empresa do gestor; nula para o admin.</summary>
    public int? EmpresaId { get; set; }

    public Empresa? Empresa { get; set; }

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

    /// <summary>
    /// A empresa a que o colaborador aderiu NO DEV.KIT (US #381), com o código de adesão e o aceite do
    /// aviso de coleta. Nula: a máquina é anônima e não aparece para gestor nenhum.
    /// </summary>
    public int? EmpresaId { get; set; }

    public Empresa? Empresa { get; set; }

    /// <summary>O nome que o próprio colaborador informou no dev.kit (o que o gestor vê).</summary>
    public string Colaborador { get; set; } = string.Empty;

    /// <summary>
    /// Quando o colaborador consentiu. Sem ele, NADA da máquina aparece para o gestor — nem nos totais:
    /// o consentimento é a condição de todo dado que a empresa vê (US #381).
    /// </summary>
    public DateTime? ConsentiuEmUtc { get; set; }

    /// <summary>
    /// O primeiro dia cujos totais o gestor vê (o dia do consentimento): o uso ANTERIOR à adesão é da
    /// pessoa, e não da empresa. O log bruto e as ocorrências de erro usam o instante exato
    /// (<see cref="ConsentiuEmUtc"/>); os totais, que são por dia, partem daqui. Nulo: nada visível.
    /// </summary>
    public DateOnly? DadosDesde { get; set; }
}

/// <summary>
/// Uma empresa do plano empresarial (US #381): o gestor dela vê e exporta o uso dos colaboradores
/// que aderiram com o <see cref="CodigoDeAdesao"/> e consentiram.
/// </summary>
public sealed class Empresa
{
    public int Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Plano { get; set; } = string.Empty;

    /// <summary>Quantas máquinas podem aderir; a adesão acima disto é recusada (a máquina fica anônima).</summary>
    public int Assentos { get; set; }

    /// <summary>Único: o que o colaborador informa no dev.kit para aderir.</summary>
    public string CodigoDeAdesao { get; set; } = string.Empty;

    public DateTime CriadaEmUtc { get; set; }
}

/// <summary>
/// A trilha de auditoria do acesso aos dados dos colaboradores (US #381): quem exportou o quê, e
/// quando. Expurgada com <see cref="OpcoesDeTelemetria.RetencaoDias"/>, como os eventos brutos.
/// </summary>
public sealed class AcessoAosDados
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public string Login { get; set; } = string.Empty;

    /// <summary>A empresa do escopo de quem acessou; nula é o admin (todas).</summary>
    public int? EmpresaId { get; set; }

    /// <summary>O que foi acessado: a rota, o formato, o período e o filtro.</summary>
    public string OQue { get; set; } = string.Empty;

    /// <summary>Quantas linhas saíram.</summary>
    public int Linhas { get; set; }

    public DateTime EmUtc { get; set; }
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
/// A FOTO do ROI de um work item numa máquina (US #387): uma linha por (<see cref="MaquinaId"/>,
/// <see cref="WorkItemId"/>), substituída na ingestão só por um <c>RoiCalculado</c> MAIS NOVO
/// (<see cref="EmUtc"/>) — o reenvio ou um evento antigo que chegue atrasado não a volta para trás.
/// Não é expurgada com os brutos: é o estado atual do item, e não um histórico.
/// </summary>
public sealed class RoiDeWorkItem
{
    public long Id { get; set; }
    public int MaquinaId { get; set; }
    public Maquina? Maquina { get; set; }
    public int WorkItemId { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public DateOnly De { get; set; }
    public DateOnly Ate { get; set; }
    public int TurnosDoAgente { get; set; }
    public int Sessoes { get; set; }
    public decimal Horas { get; set; }
    public decimal HorasNoBoard { get; set; }
    public decimal? HorasNoTimesheet { get; set; }
    public double? LeadTimeDias { get; set; }
    public bool Aberto { get; set; }
    public int PullRequests { get; set; }
    public int PullRequestsMergeadas { get; set; }

    /// <summary>O <c>em</c> do evento que trouxe a foto: é por ele que "o mais recente vence".</summary>
    public DateTime EmUtc { get; set; }

    /// <summary>O evento que trouxe a foto (para conferir de onde ela veio).</summary>
    public string EventId { get; set; } = string.Empty;
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
