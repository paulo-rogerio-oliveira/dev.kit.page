using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// A conversa e os comandos remotos (fase 2 do controle remoto). O SITE manda o comando a uma máquina — gravado no SQL e na
/// fila do usuário, com id, destino e TTL — e lê o histórico e os eventos; o DEV.KIT pede a SAS curta da fila, confirma o
/// recebimento e manda as respostas por HTTPS. O isolamento é o <c>usuarioId</c> do token em TODA operação: o comando, a
/// máquina e a fila de outro usuário são inexistentes (404). Os envios chegam JÁ validados (<see cref="RegrasDaConversa"/>).
/// </summary>
public interface IConversaRemota
{
    /// <summary>
    /// Grava o comando e o põe na fila. IDEMPOTENTE pelo id: o mesmo id devolve o comando que já existe (o que falhou na fila
    /// tenta de novo). Recusa a máquina que não é do usuário, o usuário sem fila pronta, o reinício sem confirmação com o turno
    /// em andamento e o envio acima de <see cref="OpcoesDoRemoto.ComandosPorHora"/>.
    /// </summary>
    Task<ResultadoDaConversa<ComandoAceito>> EnviarAsync(int usuarioId, ComandoNovoV1 comando, CancellationToken ct);

    /// <summary>O comando do usuário; nulo quando não é dele.</summary>
    Task<ComandoRemotoV1?> ComandoAsync(int usuarioId, string comandoId, CancellationToken ct);

    /// <summary>O histórico com a máquina e o último evento; nulo quando a máquina não é do usuário.</summary>
    Task<ConversaDaMaquinaV1?> ConversaAsync(int usuarioId, string maquinaId, CancellationToken ct);

    /// <summary>Os eventos do usuário depois de <paramref name="depoisDe"/> (de uma máquina, ou de todas), os mais antigos primeiro.</summary>
    Task<IReadOnlyList<EventoDaConversaV1>> EventosAsync(int usuarioId, string? maquinaId, long depoisDe, int maximo, CancellationToken ct);

    /// <summary>A SAS curta da fila do usuário, só de consumo, para o serviço de uma máquina DELE.</summary>
    Task<ResultadoDaConversa<SasDaFilaV1>> SasAsync(int usuarioId, string maquinaId, CancellationToken ct);

    /// <summary>
    /// O recebimento do comando pela máquina de destino: o primeiro dentro do TTL leva a <c>entregue</c> e manda executar; o
    /// repetido e o vencido não. A máquina que não é a do comando recebe <see cref="FalhaNaConversa.OutraMaquina"/>.
    /// </summary>
    Task<ResultadoDaConversa<RecebimentoAceitoV1>> ReceberAsync(int usuarioId, string comandoId, string maquinaId, CancellationToken ct);

    /// <summary>Grava a resposta do dev.kit (idempotente pelo id dela) e leva o comando respondido ao estado da resposta.</summary>
    Task<ResultadoDaConversa<RespostaRecebidaV1>> ResponderAsync(int usuarioId, RespostaDoDevKitV1 resposta, CancellationToken ct);

    /// <summary>
    /// Grava a fala do chat do dev.kit com o autor e a origem. IDEMPOTENTE pelo id dela: o mesmo conteúdo devolve a gravada
    /// (<see cref="FalaRecebidaV1.Repetida"/>), outro é <see cref="FalhaNaConversa.IdEmUso"/>. Não muda o estado de comando nenhum.
    /// </summary>
    Task<ResultadoDaConversa<FalaRecebidaV1>> FalarAsync(int usuarioId, FalaDoDevKitV1 fala, CancellationToken ct);

    /// <summary>Marca <c>expirado</c> o comando do usuário (ou de todos, sem usuário) cujo TTL venceu antes da entrega.</summary>
    Task<int> ExpirarVencidosAsync(int? usuarioId, CancellationToken ct);
}

/// <summary>A retenção da conversa: o comando, a resposta e o evento mais velhos que <see cref="OpcoesDoRemoto.RetencaoDaConversaDias"/> saem.</summary>
public interface IExpurgoDaConversa
{
    Task<int> ExpurgarAsync(CancellationToken ct);
}

/// <summary>O comando gravado e se ele nasceu agora (202) ou já existia (200).</summary>
public sealed record ComandoAceito(ComandoRemotoV1 Comando, bool Novo);

/// <summary>Por que a conversa recusou: cada um vira um status (e um <c>codigo</c>) na API.</summary>
public enum FalhaNaConversa
{
    Nenhuma,

    /// <summary>A máquina, o comando ou a fila não é do usuário (ou não existe): 404.</summary>
    NaoEncontrado,

    /// <summary>O usuário ainda não tem a fila pronta: 409 <c>sem-fila</c>.</summary>
    SemFila,

    /// <summary>Reiniciar com o turno em andamento, sem a confirmação: 409 <c>turno-em-andamento</c>.</summary>
    TurnoEmAndamento,

    /// <summary>O reenvio com o mesmo id e outro conteúdo: 409 <c>id-em-uso</c>.</summary>
    IdEmUso,

    /// <summary>O recebimento ou a resposta de uma máquina que não é a do comando: 409 <c>outra-maquina</c>.</summary>
    OutraMaquina,

    /// <summary>Acima do limite de envios por usuário: 429 <c>limite-de-envio</c>, com o <c>Retry-After</c>.</summary>
    Limite,

    /// <summary>A fila não aceitou (ou a SAS não pôde ser assinada): 503 <c>fila-indisponivel</c>.</summary>
    FilaIndisponivel,
}

/// <summary>
/// O resultado da conversa. O <see cref="Valor"/> pode vir COM a falha: o comando que a fila não aceitou volta gravado
/// (<c>falhou</c>), para o site mostrar o que houve.
/// </summary>
public sealed record ResultadoDaConversa<T>(T? Valor, FalhaNaConversa Falha = FalhaNaConversa.Nenhuma, string Mensagem = "", TimeSpan? TenteEm = null)
    where T : class
{
    public static ResultadoDaConversa<T> Falhou(FalhaNaConversa falha, string mensagem, TimeSpan? tenteEm = null) => new(null, falha, mensagem, tenteEm);
}

/// <summary>
/// A fila do usuário no Azure, vista pela API: pôr a mensagem (com o TTL) e emitir a SAS curta de consumo. A implementação do
/// Azure (<c>DevKitPage.Infrastructure.FilaDoAzure</c>) assina com a chave da conta, lida pela identidade gerenciada da API;
/// a simulada é de desenvolvimento. Falha da fila sai como <see cref="FalhaNaFila"/>, com a mensagem sem segredo.
/// </summary>
public interface IFilaDeComandos
{
    /// <summary>Põe a mensagem na fila; ela some da fila depois de <paramref name="ttl"/>, lida ou não.</summary>
    Task EnviarAsync(FilaDoUsuario fila, string mensagem, TimeSpan ttl, CancellationToken ct);

    /// <summary>A SAS de serviço SÓ desta fila e SÓ de consumo (<see cref="ConversaV1.PermissaoDaSas"/>), válida até <paramref name="ate"/>.</summary>
    Task<AcessoAFila> EmitirSasAsync(FilaDoUsuario fila, DateTimeOffset ate, CancellationToken ct);
}

/// <summary>A fila pronta do usuário (o recurso da provisão): a conta, a fila e o id dela no Azure.</summary>
public sealed record FilaDoUsuario(long RecursoId, int UsuarioId, string Conta, string Fila, string IdNoAzure, string Endpoint);

/// <summary>O acesso à fila: o endereço e a query da SAS.</summary>
public sealed record AcessoAFila(string Endpoint, string Sas, DateTimeOffset ExpiraEm);

/// <summary>A fila não aceitou ou não respondeu. A mensagem vai para o usuário: nunca leva a chave nem a SAS.</summary>
public sealed class FalhaNaFila(string mensagem, Exception? causa = null) : Exception(mensagem, causa);

/// <summary>
/// O aviso de que a conversa de um usuário mudou: acorda o SSE aberto NESTA instância da API na hora. Nas outras instâncias,
/// o SSE relê o SQL a cada <see cref="OpcoesDoRemoto.IntervaloDoSseSegundos"/> — o aviso só encurta a espera.
/// </summary>
public interface IAvisoDaConversa
{
    void Avisar(int usuarioId);

    /// <summary>
    /// O próximo aviso do usuário: completa no próximo <see cref="Avisar"/>. Pedido ANTES de ler o SQL, o aviso que chega
    /// entre a leitura e a espera não se perde.
    /// </summary>
    Task ProximoAviso(int usuarioId);
}

/// <summary>As regras puras da conversa: a forma dos envios, o TTL, as transições, o limite e a SAS da fila. Sem banco.</summary>
public static class RegrasDaConversa
{
    /// <summary>A versão da API do Storage na SAS e nas chamadas à fila.</summary>
    public const string VersaoDoStorage = "2021-12-02";

    /// <summary>O id do comando e da resposta: letras, dígitos e hífen, até 64 (o GUID do site ou do dev.kit) — a regra do id da máquina.</summary>
    public static bool IdValido(string? id) => RegrasDoRemoto.MaquinaIdValido(id);

    /// <summary>O problema do comando, ou vazio quando ele vale.</summary>
    public static string Validar(ComandoNovoV1? comando, OpcoesDoRemoto opcoes)
    {
        if (comando is null)
            return "Envie o comando.";
        if (!string.IsNullOrEmpty(comando.Id) && !IdValido(comando.Id))
            return $"O id usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if (!RegrasDoRemoto.MaquinaIdValido(comando.MaquinaId))
            return $"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        var tipo = RegrasDoRemoto.Normalizar(comando.Tipo);
        if (!ConversaV1.TiposDeComando.Contains(tipo))
            return $"O tipo é um de: {string.Join(", ", ConversaV1.TiposDeComando)}.";
        var texto = (comando.Texto ?? string.Empty).Trim();
        if (tipo == ConversaV1.ComandoMensagem && texto.Length == 0)
            return "Escreva a mensagem.";
        if (texto.Length > ConversaV1.TamanhoMaximoDoTexto)
            return $"A mensagem tem até {ConversaV1.TamanhoMaximoDoTexto} caracteres.";
        if (ConversaV1.TiposDeCriacao.Contains(tipo) && RegrasDaCriacaoRemota.Validar(tipo, texto) is { Length: > 0 } formulario)
            return formulario;
        var dispositivo = RegrasDoRemoto.Normalizar(comando.Dispositivo);
        if (dispositivo.Length > 0 && !ConversaV1.Dispositivos.Contains(dispositivo))
            return $"O dispositivo é um de: {string.Join(", ", ConversaV1.Dispositivos)}.";
        if (comando.TtlSegundos is { } ttl && (ttl < TtlMinimo(opcoes) || ttl > TtlMaximo(opcoes)))
            return $"O ttlSegundos vai de {TtlMinimo(opcoes)} a {TtlMaximo(opcoes)}.";
        return string.Empty;
    }

    /// <summary>O problema da resposta do dev.kit, ou vazio quando ela vale (o contrato é conferido à parte).</summary>
    public static string Validar(RespostaDoDevKitV1? resposta)
    {
        if (resposta is null)
            return "Envie a resposta.";
        if (!RegrasDoRemoto.MaquinaIdValido(resposta.MaquinaId))
            return $"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if (!IdValido(resposta.Id))
            return $"O id da resposta usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if (!string.IsNullOrEmpty(resposta.ComandoId) && !IdValido(resposta.ComandoId))
            return $"O comandoId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        var tipo = RegrasDoRemoto.Normalizar(resposta.Tipo);
        if (!ConversaV1.TiposDeResposta.Contains(tipo))
            return $"O tipo é um de: {string.Join(", ", ConversaV1.TiposDeResposta)}.";
        if (tipo != ConversaV1.RespostaMensagem && string.IsNullOrEmpty(resposta.ComandoId))
            return $"A resposta {tipo} diz de qual comando é (comandoId).";
        var texto = (resposta.Texto ?? string.Empty).Trim();
        if (tipo == ConversaV1.RespostaMensagem && texto.Length == 0)
            return "Escreva a mensagem.";
        if (texto.Length > ConversaV1.TamanhoMaximoDoTexto)
            return $"A mensagem tem até {ConversaV1.TamanhoMaximoDoTexto} caracteres.";
        if (resposta.GeradaEm == default)
            return "Informe quando a resposta foi gerada (geradaEm).";
        return string.Empty;
    }

    /// <summary>O problema da fala do chat do dev.kit, ou vazio quando ela vale (o contrato é conferido à parte).</summary>
    public static string Validar(FalaDoDevKitV1? fala)
    {
        if (fala is null)
            return "Envie a fala.";
        if (!RegrasDoRemoto.MaquinaIdValido(fala.MaquinaId))
            return $"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if (!IdValido(fala.Id))
            return $"O id da fala usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if (!ConversaV1.AutoresDaFala.Contains(RegrasDoRemoto.Normalizar(fala.Autor)))
            return $"O autor é um de: {string.Join(", ", ConversaV1.AutoresDaFala)}.";
        var origem = RegrasDoRemoto.Normalizar(fala.Origem);
        if (!ConversaV1.OrigensDaFala.Contains(origem))
            return $"A origem é uma de: {string.Join(", ", ConversaV1.OrigensDaFala)}.";
        if (!string.IsNullOrEmpty(fala.ComandoId) && !IdValido(fala.ComandoId))
            return $"O comandoId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if (origem == ConversaV1.OrigemRemoto && string.IsNullOrEmpty(fala.ComandoId))
            return "A fala de origem remoto diz de qual comando veio (comandoId).";
        var texto = (fala.Texto ?? string.Empty).Trim();
        if (texto.Length == 0)
            return "Escreva a fala.";
        if (texto.Length > ConversaV1.TamanhoMaximoDoTexto)
            return $"A fala tem até {ConversaV1.TamanhoMaximoDoTexto} caracteres.";
        if (fala.DitaEm == default)
            return "Informe quando a fala foi dita (ditaEm).";
        return string.Empty;
    }

    /// <summary>Os campos do comando com dado sensível (<see cref="DadosSensiveis"/>): o texto vai para o SQL e para a fila.</summary>
    public static IReadOnlyList<CampoSensivelV1> CamposSensiveis(ComandoNovoV1 comando)
        => RegrasDoRemoto.Sensiveis([("texto", comando.Texto)]);

    /// <summary>Os campos da resposta com dado sensível: a conversa sai da máquina do dev só sanitizada.</summary>
    public static IReadOnlyList<CampoSensivelV1> CamposSensiveis(RespostaDoDevKitV1 resposta)
        => RegrasDoRemoto.Sensiveis([("texto", resposta.Texto)]);

    /// <summary>Os campos da fala com dado sensível: a conversa do chat do dev.kit sai da máquina só sanitizada.</summary>
    public static IReadOnlyList<CampoSensivelV1> CamposSensiveis(FalaDoDevKitV1 fala)
        => RegrasDoRemoto.Sensiveis([("texto", fala.Texto)]);

    public static int TtlMinimo(OpcoesDoRemoto opcoes) => Math.Max(1, opcoes.TtlMinimoSegundos);

    public static int TtlMaximo(OpcoesDoRemoto opcoes) => Math.Max(TtlMinimo(opcoes), opcoes.TtlMaximoSegundos);

    /// <summary>O TTL do comando: o pedido (já validado) ou o padrão, sempre dentro da faixa.</summary>
    public static TimeSpan Ttl(int? pedido, OpcoesDoRemoto opcoes)
        => TimeSpan.FromSeconds(Math.Clamp(pedido ?? opcoes.TtlPadraoSegundos, TtlMinimo(opcoes), TtlMaximo(opcoes)));

    /// <summary>A validade da SAS do serviço: <see cref="OpcoesDoRemoto.SasMinutos"/>, de 1 a 60 — curta de propósito.</summary>
    public static TimeSpan ValidadeDaSas(OpcoesDoRemoto opcoes) => TimeSpan.FromMinutes(Math.Clamp(opcoes.SasMinutos, 1, 60));

    /// <summary>O comando que ainda não chegou à máquina: é o que o TTL vence.</summary>
    public static bool AguardaEntrega(string estado)
        => estado is ConversaV1.EstadoPendente or ConversaV1.EstadoNaFila or ConversaV1.EstadoFalhou;

    /// <summary>O comando vencido: ainda não entregue e com o TTL passado.</summary>
    public static bool Vencido(string estado, DateTime expiraEmUtc, DateTime agoraUtc) => AguardaEntrega(estado) && agoraUtc >= expiraEmUtc;

    /// <summary>O estado do comando depois da resposta do dev.kit; o expirado e o que não foi entregue não mudam por ela.</summary>
    public static string EstadoDepoisDaResposta(string estado, string tipoDaResposta)
    {
        if (estado == ConversaV1.EstadoExpirado)
            return estado;
        return tipoDaResposta switch
        {
            ConversaV1.RespostaConcluido => ConversaV1.EstadoConcluido,
            ConversaV1.RespostaRecusado => ConversaV1.EstadoRecusado,
            _ => ConversaV1.EstadoRespondido,
        };
    }

    /// <summary>O limite por usuário está cheio? <paramref name="naJanela"/> é o que ele mandou na última hora.</summary>
    public static bool LimiteCheio(int naJanela, OpcoesDoRemoto opcoes) => naJanela >= Math.Max(1, opcoes.ComandosPorHora);

    /// <summary>Quando o envio volta a caber: o mais antigo da janela de uma hora sai dela.</summary>
    public static TimeSpan TenteEm(DateTime maisAntigoNaJanelaUtc, DateTime agoraUtc)
    {
        var espera = maisAntigoNaJanelaUtc.AddHours(1) - agoraUtc;
        return espera > TimeSpan.Zero ? espera : TimeSpan.FromSeconds(1);
    }

    /// <summary>
    /// O texto que a SAS de serviço da FILA assina (versão 2015-04-05 em diante): permissões, início, fim, o recurso
    /// canônico <c>/queue/{conta}/{fila}</c>, o identificador, o IP, o protocolo e a versão — uma linha cada.
    /// </summary>
    public static string TextoDaSas(string conta, string fila, string permissoes, DateTimeOffset inicio, DateTimeOffset fim)
        => string.Join('\n', permissoes, Data(inicio), Data(fim), $"/queue/{conta}/{fila}", string.Empty, string.Empty, "https", VersaoDoStorage);

    /// <summary>
    /// A query da SAS de serviço de UMA fila (sem o <c>?</c>): só HTTPS, assinada com HMAC-SHA256 da chave da conta. A chave
    /// não sai daqui — só a assinatura.
    /// </summary>
    public static string Sas(string conta, string fila, string chaveBase64, string permissoes, DateTimeOffset inicio, DateTimeOffset fim)
    {
        using var hmac = new HMACSHA256(Convert.FromBase64String(chaveBase64));
        var assinatura = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(TextoDaSas(conta, fila, permissoes, inicio, fim))));
        return string.Join('&',
            $"sv={VersaoDoStorage}",
            $"st={Uri.EscapeDataString(Data(inicio))}",
            $"se={Uri.EscapeDataString(Data(fim))}",
            $"sp={permissoes}",
            "spr=https",
            $"sig={Uri.EscapeDataString(assinatura)}");
    }

    private static string Data(DateTimeOffset data) => data.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
