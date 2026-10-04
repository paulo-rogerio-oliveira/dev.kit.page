using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>O resultado do login.</summary>
public enum ResultadoDoLogin
{
    Ok,
    CredencialInvalida,
    Bloqueado,
}

/// <summary>O login e a troca de senha dos usuários do dashboard.</summary>
public interface IUsuarios
{
    /// <summary>Confere a senha, conta as falhas e bloqueia depois de muitas seguidas.</summary>
    Task<(ResultadoDoLogin Resultado, Usuario? Usuario)> AutenticarAsync(string login, string senha, CancellationToken ct);

    /// <summary>Troca a senha (confere a atual e a política). Devolve o erro, ou vazio.</summary>
    Task<string> TrocarSenhaAsync(int usuarioId, string senhaAtual, string novaSenha, CancellationToken ct);

    Task<Usuario?> ObterAsync(int usuarioId, CancellationToken ct);
}

/// <summary>O registro e a autenticação das MÁQUINAS — separados do login de usuário.</summary>
public interface IMaquinas
{
    /// <summary>Registra a máquina (ou gira a chave de uma já registrada) e devolve a chave nova.</summary>
    Task<string> RegistrarAsync(string maquinaId, string versaoDevKit, CancellationToken ct);

    /// <summary>A máquina dona da chave, ou nula.</summary>
    Task<Maquina?> AutenticarAsync(string chave, CancellationToken ct);
}

/// <summary>A ingestão dos lotes: idempotente por eventId, consolidando o total diário junto.</summary>
public interface IIngestaoDeTelemetria
{
    Task<BatchResultV1> RegistrarAsync(int maquinaId, TelemetryBatchV1 lote, CancellationToken ct);
}

/// <summary>As consultas do dashboard, agregadas no banco.</summary>
public interface IConsultasDoPainel
{
    Task<IReadOnlyList<MaquinaResumo>> MaquinasAsync(CancellationToken ct);

    Task<QuantidadeResposta> QuantidadeAsync(Periodo periodo, int? maquina, CancellationToken ct);

    Task<QualidadeResposta> QualidadeAsync(Periodo periodo, int? maquina, CancellationToken ct);

    Task<Pagina<EventoDoLog>> EventosAsync(Periodo periodo, int? maquina, int pagina, int tamanho, CancellationToken ct);
}

/// <summary>O expurgo dos eventos brutos além da retenção. Os totais diários ficam.</summary>
public interface IExpurgoDeEventos
{
    /// <summary>Apaga os eventos anteriores ao corte e devolve quantos.</summary>
    Task<int> ExpurgarAsync(CancellationToken ct);
}

/// <summary>Os pedidos de demonstração: a gravação pública e a leitura/exclusão do dashboard.</summary>
public interface IPedidosDeDemonstracao
{
    /// <summary>Grava o pedido JÁ validado (<see cref="ValidadorDeDemonstracao"/>).</summary>
    Task<PedidoDeDemonstracaoCriadoV1> RegistrarAsync(PedidoDeDemonstracaoV1 pedido, CancellationToken ct);

    /// <summary>Os pedidos do mais novo para o mais antigo.</summary>
    Task<Pagina<DemonstracaoResumo>> ListarAsync(int pagina, int tamanho, CancellationToken ct);

    /// <summary>Exclui o pedido (eliminação a pedido do titular). Falso quando ele não existe.</summary>
    Task<bool> ExcluirAsync(long id, CancellationToken ct);
}

/// <summary>O expurgo dos pedidos de demonstração além da retenção (LGPD).</summary>
public interface IExpurgoDePedidos
{
    /// <summary>Apaga os pedidos anteriores ao corte e devolve quantos.</summary>
    Task<int> ExpurgarAsync(CancellationToken ct);
}
