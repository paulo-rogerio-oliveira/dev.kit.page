using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>O resultado do login.</summary>
public enum ResultadoDoLogin
{
    Ok,
    CredencialInvalida,

    /// <summary>O bloqueio TEMPORÁRIO por falhas seguidas (o usuário volta com o <see cref="Usuario.BloqueadoAteUtc"/>).</summary>
    Bloqueado,

    /// <summary>O admin bloqueou o usuário (US #405): só sai quando ele desbloqueia.</summary>
    BloqueadoPeloAdmin,
}

/// <summary>O login e a troca de senha dos usuários do dashboard.</summary>
public interface IUsuarios
{
    /// <summary>
    /// Confere a senha, conta as falhas e bloqueia depois de muitas seguidas. No
    /// <see cref="ResultadoDoLogin.Bloqueado"/> o usuário volta junto (a API devolve o
    /// <see cref="Usuario.BloqueadoAteUtc"/> ao app); o <see cref="ResultadoDoLogin.BloqueadoPeloAdmin"/> só sai
    /// com a senha CERTA — sem ela é credencial inválida, e quem não sabe a senha não descobre que o login existe.
    /// </summary>
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

    /// <summary>
    /// As avaliações de entrega (US #417) do período, no escopo (e da máquina, quando filtrada): a mais recente
    /// de cada (máquina, sessão, turno), das mais novas para as mais antigas, paginadas.
    /// </summary>
    Task<Pagina<FeedbackV1>> FeedbackAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, int pagina, int tamanho, CancellationToken ct);

    /// <summary>As métricas do feedback (US #417) no período e no escopo: total, positivos e negativos, por fluxo, por agente e por dia.</summary>
    Task<MetricasDeFeedbackV1> MetricasDeFeedbackAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct);
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

/// <summary>Por que uma operação da gestão de usuários não foi feita — a API traduz em status HTTP.</summary>
public enum FalhaNaGestao
{
    Nenhuma,

    /// <summary>O usuário (ou a empresa) não existe: 404.</summary>
    NaoEncontrado,

    /// <summary>O login já existe: 409.</summary>
    Conflito,

    /// <summary>A operação fere uma regra (empresa inexistente, o admin bloqueando a si mesmo): 400.</summary>
    Recusada,
}

/// <summary>O resultado de uma operação da gestão de usuários: o valor, ou a falha com a mensagem.</summary>
public sealed record ResultadoDaGestao<T>(T? Valor, FalhaNaGestao Falha = FalhaNaGestao.Nenhuma, string Mensagem = "")
    where T : class
{
    public static ResultadoDaGestao<T> Falhou(FalhaNaGestao falha, string mensagem) => new(null, falha, mensagem);
}

/// <summary>
/// A gestão de usuários pelo admin (US #405): criar com senha TEMPORÁRIA (devolvida uma vez), editar o
/// nome, o papel e a empresa, bloquear e desbloquear, e redefinir a senha. Os dados chegam JÁ validados
/// (<see cref="ValidadorDeUsuario"/>); aqui ficam as regras que dependem do banco.
/// </summary>
public interface IGestaoDeUsuarios
{
    Task<IReadOnlyList<UsuarioResumo>> ListarAsync(CancellationToken ct);

    /// <summary>Cria o usuário com a troca obrigatória no primeiro acesso. Conflito no login repetido; recusa com empresa inexistente.</summary>
    Task<ResultadoDaGestao<UsuarioComSenha>> CriarAsync(UsuarioNovo usuario, CancellationToken ct);

    /// <summary>Muda o nome, o papel e a empresa. Quem edita (<paramref name="quem"/>) não muda o próprio papel.</summary>
    Task<ResultadoDaGestao<UsuarioResumo>> EditarAsync(int id, UsuarioEditado edicao, int quem, CancellationToken ct);

    /// <summary>Bloqueia ou desbloqueia. Quem bloqueia não bloqueia a si mesmo.</summary>
    Task<ResultadoDaGestao<UsuarioResumo>> BloquearAsync(int id, bool bloqueado, int quem, CancellationToken ct);

    /// <summary>
    /// Gera uma senha temporária nova (devolvida uma vez), exige a troca no próximo acesso e zera o bloqueio
    /// TEMPORÁRIO por falhas — o bloqueio do admin fica como está.
    /// </summary>
    Task<ResultadoDaGestao<UsuarioComSenha>> RedefinirSenhaAsync(int id, CancellationToken ct);
}

/// <summary>
/// Uma release estável do dev.kit com o pacote (US #405), já lida do GitHub e com a versão do dev.kit
/// (<see cref="RegrasDeVersao"/>). O <see cref="AssetId"/> é o que o download pede ao GitHub.
/// </summary>
public sealed record ReleaseDoDevKit(
    int Versao, string Tag, string Nome, DateTimeOffset PublicadaEm, IReadOnlyList<string> Destaques,
    long AssetId, string NomeDoPacote, long TamanhoBytes, string? Sha256);

/// <summary>
/// O pacote aberto para o download: o conteúdo em STREAMING (nada é bufferizado na API) e o tamanho,
/// quando o GitHub o informa. Descartar fecha a resposta do GitHub.
/// </summary>
public sealed class PacoteAberto(Stream conteudo, long? tamanho, IDisposable dono) : IAsyncDisposable
{
    public Stream Conteudo { get; } = conteudo;

    public long? Tamanho { get; } = tamanho;

    public async ValueTask DisposeAsync()
    {
        await Conteudo.DisposeAsync();
        dono.Dispose();
    }
}

/// <summary>
/// As releases do dev.kit no GitHub (US #405). O token (<see cref="OpcoesDeAtualizacao.Token"/>) só existe
/// na API: o app e a landing pedem a versão e o pacote aqui. Nulo é "indisponível" (sem token, GitHub fora
/// do ar) — a API responde 503 sem repassar nada do GitHub.
/// </summary>
public interface IReleasesDoDevKit
{
    /// <summary>As releases estáveis com o zip, da versão mais nova para a mais velha (em cache por poucos minutos); nulo quando indisponível.</summary>
    Task<IReadOnlyList<ReleaseDoDevKit>?> EstaveisAsync(CancellationToken ct);

    /// <summary>Abre o zip da release em streaming; nulo quando o GitHub não o entrega.</summary>
    Task<PacoteAberto?> AbrirPacoteAsync(ReleaseDoDevKit release, CancellationToken ct);
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
