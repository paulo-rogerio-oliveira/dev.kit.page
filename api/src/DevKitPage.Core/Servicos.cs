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

/// <summary>O que o registro devolve: a chave nova e o desfecho da adesão à empresa (US #381).</summary>
/// <param name="Empresa">A empresa a que a máquina ficou vinculada, ou nulo.</param>
/// <param name="Adesao">O desfecho do código de empresa, para o colaborador ler no dev.kit; nulo quando não veio código.</param>
public sealed record RegistroDaMaquina(string Chave, string? Empresa, string? Adesao);

/// <summary>O registro e a autenticação das MÁQUINAS — separados do login de usuário.</summary>
public interface IMaquinas
{
    /// <summary>
    /// Registra a máquina (ou gira a chave de uma já registrada) e devolve a chave nova. O
    /// <paramref name="codigoEmpresa"/> (US #381) vincula a máquina à empresa — nulo não mexe no
    /// vínculo (o dev.kit antigo), vazio o desfaz, e o código desconhecido ou sem assento livre deixa a
    /// máquina anônima.
    /// <para>
    /// A máquina que JÁ existe só é registrada de novo por quem prova ser ela — a
    /// <paramref name="chaveAtual"/> — ou pelo admin: o código de registro é público (vai no instalador),
    /// e sem isto qualquer um giraria a chave de outra máquina e mexeria na adesão dela. Recusado, nulo.
    /// </para>
    /// </summary>
    Task<RegistroDaMaquina?> RegistrarAsync(
        string maquinaId, string versaoDevKit, string? codigoEmpresa, string? colaborador, string? chaveAtual, bool peloAdmin, CancellationToken ct);

    /// <summary>A máquina dona da chave, ou nula.</summary>
    Task<Maquina?> AutenticarAsync(string chave, CancellationToken ct);
}

/// <summary>A ingestão dos lotes: idempotente por eventId, consolidando o total diário junto.</summary>
public interface IIngestaoDeTelemetria
{
    Task<BatchResultV1> RegistrarAsync(int maquinaId, TelemetryBatchV1 lote, CancellationToken ct);
}

/// <summary>
/// As consultas do dashboard, agregadas no banco. TODA consulta recebe o <see cref="EscopoDoPainel"/>
/// (US #381) e o aplica aqui, num ponto só: o gestor nunca lê o que é de outra empresa, e o dado
/// individual só das máquinas que consentiram.
/// </summary>
public interface IConsultasDoPainel
{
    /// <summary>As máquinas do escopo: o admin vê todas; o gestor, só as que consentiram.</summary>
    Task<IReadOnlyList<MaquinaResumo>> MaquinasAsync(EscopoDoPainel escopo, CancellationToken ct);

    /// <summary>
    /// A máquina pode ser filtrada por quem consulta: existe e, para o gestor, é da empresa dele e
    /// consentiu. Falso é 403 — o filtro não serve para sondar outra empresa.
    /// </summary>
    Task<bool> MaquinaVisivelAsync(EscopoDoPainel escopo, int maquina, CancellationToken ct);

    Task<QuantidadeResposta> QuantidadeAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct);

    Task<QualidadeResposta> QualidadeAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct);

    Task<Pagina<EventoDoLog>> EventosAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, int pagina, int tamanho, CancellationToken ct);

    /// <summary>
    /// Os grupos de exceção não classificada com ocorrência no período (US #381), do visto por último
    /// para o mais antigo. As contagens são do período, do escopo (e da máquina, quando filtrada).
    /// </summary>
    Task<Pagina<GrupoDeErroResumo>> ErrosAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, int pagina, int tamanho, CancellationToken ct);

    /// <summary>O grupo existe e tem ocorrência em alguma máquina do escopo. Falso é 403 para o gestor.</summary>
    Task<bool> ErroVisivelAsync(EscopoDoPainel escopo, long id, CancellationToken ct);

    /// <summary>O detalhe de um grupo no período e no escopo — também o corpo da exportação. Nulo quando ele não existe.</summary>
    Task<GrupoDeErroDetalhe?> ErroAsync(EscopoDoPainel escopo, long id, Periodo periodo, CancellationToken ct);

    /// <summary>Os colaboradores que consentiram (máquinas vinculadas a empresa), no escopo.</summary>
    Task<IReadOnlyList<ColaboradorResumo>> ColaboradoresAsync(EscopoDoPainel escopo, CancellationToken ct);

    /// <summary>O uso por colaborador (máquina que consentiu) e dia — o corpo da exportação em CSV ou JSON.</summary>
    Task<IReadOnlyList<LinhaExportada>> ExportarAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct);

    /// <summary>
    /// O ROI por work item (US #387): a foto mais recente de cada (máquina, item) calculada no período, no
    /// escopo (e da máquina, quando filtrada), das mais recentes para as mais antigas, com os totais.
    /// </summary>
    Task<RoiResposta> RoiAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct);
}

/// <summary>As empresas do plano empresarial e os gestores delas (US #381) — só o admin.</summary>
public interface IEmpresas
{
    Task<EmpresaResumo> CriarAsync(EmpresaNova empresa, CancellationToken ct);

    Task<IReadOnlyList<EmpresaResumo>> ListarAsync(CancellationToken ct);

    /// <summary>
    /// Cria o gestor da empresa com uma senha inicial (devolvida UMA vez) e a troca obrigatória no
    /// primeiro acesso. Nulo quando a empresa não existe; o erro, quando o login já existe.
    /// </summary>
    Task<(GestorCriado? Gestor, string Erro)> CriarGestorAsync(int empresaId, string login, CancellationToken ct);
}

/// <summary>A trilha de auditoria do acesso aos dados dos colaboradores (US #381).</summary>
public interface IAuditoriaDeAcesso
{
    Task RegistrarAsync(int usuarioId, string login, EscopoDoPainel escopo, string oQue, int linhas, CancellationToken ct);
}

/// <summary>A reação a um grupo de exceção (US #381): marcar visto, resolver na versão, ignorar, reabrir.</summary>
public interface IReacaoAErros
{
    /// <summary>Grava o estado JÁ validado (<see cref="RegrasDeErro.Validar"/>). Falso quando o grupo não existe.</summary>
    Task<bool> AlterarEstadoAsync(long id, AlterarEstadoDoGrupo pedido, CancellationToken ct);
}

/// <summary>O expurgo das ocorrências de erro além da retenção (o grupo, com o estado, fica).</summary>
public interface IExpurgoDeOcorrencias
{
    /// <summary>Apaga as ocorrências anteriores ao corte e devolve quantas.</summary>
    Task<int> ExpurgarAsync(CancellationToken ct);
}

/// <summary>O expurgo da trilha de auditoria além da retenção (US #381).</summary>
public interface IExpurgoDeAcessos
{
    /// <summary>Apaga os registros anteriores ao corte e devolve quantos.</summary>
    Task<int> ExpurgarAsync(CancellationToken ct);
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
