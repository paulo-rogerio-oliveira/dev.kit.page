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
public sealed record TelemetryEventV1(
    string EventId, string Tipo, string SessaoId, int Quantidade, long? Valor, string Detalhe, DateTimeOffset Em);

/// <summary>Um lote de eventos de uma máquina.</summary>
public sealed record TelemetryBatchV1(string Versao, string MaquinaId, string VersaoDevKit, IReadOnlyList<TelemetryEventV1> Eventos);

/// <summary>O que a ingestão fez com o lote.</summary>
/// <param name="Recebidos">Eventos no lote.</param>
/// <param name="Novos">Gravados agora.</param>
/// <param name="Duplicados">Já recebidos antes (o reenvio) — não contam de novo.</param>
/// <param name="Ignorados">De tipo que esta versão da API não conhece.</param>
public sealed record BatchResultV1(int Recebidos, int Novos, int Duplicados, int Ignorados);

/// <summary>O pedido de registro de uma máquina: o id anônimo, sem hostname nem usuário.</summary>
public sealed record MachineRegistrationV1(string MaquinaId, string VersaoDevKit, string CodigoRegistro);

/// <summary>A chave da máquina — devolvida UMA vez; a API guarda só o hash.</summary>
public sealed record MachineRegistrationResponseV1(string Chave);

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

    /// <summary>Os tipos que esta versão da API grava.</summary>
    public static IReadOnlySet<string> Conhecidos { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ArquivoAlterado, FluxoExecutado, FerramentaAcionada, ComandoDelegado, TurnoExecutado,
        TurnoFalhou, ObjetivoAvaliado, ObjetivoCumprido, ObjetivoRecusado, SessaoIniciada,
    };
}
