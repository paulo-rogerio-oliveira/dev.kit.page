namespace DevKitPage.Contracts.V1;

/// <summary>
/// O contrato v1 da ingestão — o MESMO que o dev.kit envia (git.kit,
/// <c>TelemetrySyncService</c>). Mudar um nome aqui quebra todas as máquinas já instaladas: um
/// campo novo é opcional, e uma mudança incompatível é um <c>v2</c>, com rota própria.
/// </summary>
public static class ContratoV1
{
    /// <summary>O valor de <see cref="TelemetryBatchV1.Versao"/> que esta rota aceita.</summary>
    public const string Versao = "v1";

    /// <summary>O cabeçalho com a chave da máquina — a ingestão só aceita isto, nunca o JWT de usuário.</summary>
    public const string CabecalhoDaChave = "X-Machine-Key";
}

/// <summary>Um evento de uso.</summary>
/// <param name="EventId">O id único, gerado no dev.kit: é por ele que o reenvio é descartado.</param>
/// <param name="Tipo">O tipo (<see cref="TiposDeEvento"/>); o desconhecido é ignorado, não recusado.</param>
/// <param name="SessaoId">A sessão (task) do dev.kit — um id interno, sem pasta nem nome.</param>
/// <param name="Quantidade">Quantos o evento soma.</param>
/// <param name="Valor">A medida (duração do turno em ms, nota do avaliador), quando há.</param>
/// <param name="Detalhe">O recorte (ferramenta, executável, causa da falha, CLI, versão).</param>
/// <param name="Em">Quando aconteceu (UTC).</param>
/// <param name="Trace">
/// Opcional (US #381): o trace JÁ SANITIZADO no dev.kit de uma <see cref="TiposDeEvento.ExcecaoNaoClassificada"/>
/// — o tipo e os quadros, sem caminhos, e-mails nem URLs. Até <c>8 KB</c>; o cliente antigo não manda.
/// </param>
/// <param name="Assinatura">Opcional (US #381): a impressão digital da exceção, que agrupa as ocorrências.</param>
public sealed record TelemetryEventV1(
    string EventId, string Tipo, string SessaoId, int Quantidade, long? Valor, string Detalhe, DateTimeOffset Em,
    string? Trace = null, string? Assinatura = null);

/// <summary>Um lote de eventos de uma máquina.</summary>
public sealed record TelemetryBatchV1(string Versao, string MaquinaId, string VersaoDevKit, IReadOnlyList<TelemetryEventV1> Eventos);

/// <summary>O que a ingestão fez com o lote.</summary>
/// <param name="Recebidos">Eventos no lote.</param>
/// <param name="Novos">Gravados agora.</param>
/// <param name="Duplicados">Já recebidos antes (o reenvio) — não contam de novo.</param>
/// <param name="Ignorados">De tipo que esta versão da API não conhece.</param>
public sealed record BatchResultV1(int Recebidos, int Novos, int Duplicados, int Ignorados);

/// <summary>O pedido de registro de uma máquina: o id anônimo, sem hostname nem usuário.</summary>
/// <param name="CodigoEmpresa">
/// Opcional (US #381): o código de adesão da empresa, que o dev.kit só manda depois de o colaborador
/// ACEITAR o aviso de coleta. Nulo (o cliente antigo) não mexe no vínculo; vazio desfaz.
/// </param>
/// <param name="Colaborador">Opcional (US #381): o nome que o próprio colaborador informou, compartilhado com o gestor.</param>
public sealed record MachineRegistrationV1(
    string MaquinaId, string VersaoDevKit, string CodigoRegistro, string? CodigoEmpresa = null, string? Colaborador = null);

/// <summary>A chave da máquina — devolvida UMA vez; a API guarda só o hash.</summary>
/// <param name="Empresa">A empresa a que a máquina ficou vinculada, ou nulo (US #381).</param>
/// <param name="Adesao">O que aconteceu com o código da empresa, para o dev.kit mostrar ao colaborador.</param>
public sealed record MachineRegistrationResponseV1(string Chave, string? Empresa = null, string? Adesao = null);

/// <summary>
/// Os tipos de evento que o dev.kit envia (o <c>TelemetryEventKind</c> do git.kit, pelo nome).
/// Quantidade de uso: os quatro da US #252 mais a sessão; qualidade: turnos, falhas, notas e objetivos.
/// </summary>
public static class TiposDeEvento
{
    public const string ArquivoAlterado = "ArquivoAlterado";
    public const string FluxoExecutado = "FluxoExecutado";
    public const string FerramentaAcionada = "FerramentaAcionada";
    public const string ComandoDelegado = "ComandoDelegado";
    public const string TurnoExecutado = "TurnoExecutado";
    public const string TurnoFalhou = "TurnoFalhou";
    public const string ObjetivoAvaliado = "ObjetivoAvaliado";
    public const string ObjetivoCumprido = "ObjetivoCumprido";
    public const string ObjetivoRecusado = "ObjetivoRecusado";
    public const string SessaoIniciada = "SessaoIniciada";

    /// <summary>
    /// Uma exceção que nenhum detector classificou (US #381): o turno que falhou sem causa em
    /// <c>TurnFailures.Padrao</c> ou a exceção não tratada do app, do serviço ou do devcli. Leva o
    /// <see cref="TelemetryEventV1.Trace"/> e a <see cref="TelemetryEventV1.Assinatura"/>.
    /// </summary>
    public const string ExcecaoNaoClassificada = "ExcecaoNaoClassificada";

    /// <summary>Os tipos que esta versão da API grava.</summary>
    public static IReadOnlySet<string> Conhecidos { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ArquivoAlterado, FluxoExecutado, FerramentaAcionada, ComandoDelegado, TurnoExecutado,
        TurnoFalhou, ObjetivoAvaliado, ObjetivoCumprido, ObjetivoRecusado, SessaoIniciada, ExcecaoNaoClassificada,
    };
}
