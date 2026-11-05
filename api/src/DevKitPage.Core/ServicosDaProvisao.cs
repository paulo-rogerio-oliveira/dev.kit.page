using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// A provisão vista pela API PÚBLICA: o usuário pede, acompanha, repete e remove — e a API só GRAVA o pedido. Ela não tem
/// permissão nenhuma no Azure: quem executa é o worker (<see cref="IMotorDeProvisao"/>), noutro processo. O isolamento é o
/// <c>usuarioId</c> do token em toda operação: o recurso de outro usuário é inexistente (404).
/// </summary>
public interface IProvisao
{
    /// <summary>Se o usuário pode provisionar, e de onde vem a liberação (a permissão do admin ou o plano).</summary>
    Task<PermissaoDeProvisaoV1> PermissaoAsync(int usuarioId, CancellationToken ct);

    /// <summary>Os recursos do usuário, os mais novos primeiro (os removidos também: é o histórico do que custou).</summary>
    Task<IReadOnlyList<RecursoProvisionadoV1>> RecursosAsync(int usuarioId, CancellationToken ct);

    /// <summary>O recurso com a auditoria; nulo quando não é do usuário.</summary>
    Task<DetalheDoRecursoV1?> DetalheAsync(int usuarioId, long id, CancellationToken ct);

    /// <summary>
    /// Pede a fila na região (validada antes). IDEMPOTENTE: com a cota cheia, devolve a fila ativa que já existe, sem criar
    /// outra (o <c>Novo</c> falso) — o duplo clique e o reenvio não duplicam recurso pago.
    /// </summary>
    Task<ResultadoDaGestao<PedidoAceito>> PedirFilaAsync(int usuarioId, string autor, string regiao, CancellationToken ct);

    /// <summary>Devolve ao worker a operação que FALHOU, do começo das tentativas. Só no estado <c>falhou</c> (409 nos outros).</summary>
    Task<ResultadoDaGestao<PedidoAceito>> RepetirAsync(int usuarioId, string autor, long id, CancellationToken ct);

    /// <summary>
    /// Pede a remoção do recurso pronto (ou que falhou). Idempotente: a remoção já pedida, ou feita, volta como está. Com o
    /// recurso ainda em criação, 409 — remove-se depois que ele terminar.
    /// </summary>
    Task<ResultadoDaGestao<PedidoAceito>> RemoverAsync(int usuarioId, string autor, long id, CancellationToken ct);
}

/// <summary>O pedido gravado: o recurso e se ele nasceu agora (202) ou já existia (200).</summary>
public sealed record PedidoAceito(RecursoProvisionadoV1 Recurso, bool Novo);

/// <summary>
/// O MOTOR da provisão, o lado do worker: reserva o próximo pedido, e conclui ou falha a execução. A reserva usa o token de
/// concorrência (<see cref="RecursoProvisionado.Versao"/>), então dois workers nunca executam o mesmo pedido; e vence
/// (<see cref="OpcoesDaProvisao.ReservaMinutos"/>), então o worker que caiu no meio não o prende.
/// </summary>
public interface IMotorDeProvisao
{
    /// <summary>O próximo pedido vencido, já reservado a <paramref name="trabalhador"/> (com a tentativa contada); nulo sem nenhum.</summary>
    Task<RecursoProvisionado?> ReservarAsync(string trabalhador, CancellationToken ct);

    /// <summary>A operação deu certo: a criação fica <c>pronto</c>, a remoção <c>removido</c>. Falso quando a reserva não é mais dele.</summary>
    Task<bool> ConcluirAsync(long id, string trabalhador, RecursoNaNuvem? recurso, CancellationToken ct);

    /// <summary>
    /// A operação falhou: a TRANSITÓRIA volta a <c>pedido</c> com a espera (<see cref="RegrasDaProvisao.Espera"/>) até
    /// <see cref="OpcoesDaProvisao.MaximoDeTentativas"/>; a definitiva, ou a que esgotou as tentativas, fica <c>falhou</c>.
    /// </summary>
    Task<bool> FalharAsync(long id, string trabalhador, string mensagem, bool transitoria, CancellationToken ct);
}

/// <summary>
/// A nuvem que o worker comanda — o Azure Resource Manager em produção (<c>DevKitPage.Provisao.ProvedorDoAzure</c>), um falso
/// nos testes. As operações são IDEMPOTENTES (o PUT do ARM cria ou confirma; remover o que não existe é sucesso): o pedido
/// repetido depois de uma queda no meio chega ao mesmo resultado.
/// </summary>
public interface IProvedorDeNuvem
{
    /// <summary>Cria (ou confirma) o grupo do usuário, a conta de armazenamento e a fila.</summary>
    Task<RecursoNaNuvem> CriarFilaAsync(FilaNaNuvem fila, CancellationToken ct);

    /// <summary>Remove a conta de armazenamento (e a fila com ela). O grupo do usuário fica: vazio, não custa.</summary>
    Task RemoverFilaAsync(FilaNaNuvem fila, CancellationToken ct);
}

/// <summary>O que o worker pede à nuvem: os nomes JÁ decididos no pedido (o repetido usa os mesmos).</summary>
public sealed record FilaNaNuvem(long RecursoId, int UsuarioId, string GrupoDeRecursos, string Conta, string Fila, string Regiao);

/// <summary>O recurso criado: o id no Azure e o endereço da fila.</summary>
public sealed record RecursoNaNuvem(string IdNoAzure, string Endpoint);

/// <summary>
/// A falha da nuvem já CLASSIFICADA: <see cref="Transitoria"/> é a que vale tentar de novo (429, 5xx, rede); a outra é
/// definitiva (sem permissão, nome tomado, pedido inválido). A mensagem vai para o usuário: nunca leva token nem chave.
/// </summary>
public sealed class FalhaNaNuvem(string mensagem, bool transitoria, Exception? causa = null) : Exception(mensagem, causa)
{
    public bool Transitoria { get; } = transitoria;
}

/// <summary>As regras puras da provisão: a liberação, os nomes, a região, a espera entre as tentativas e as transições. Sem banco.</summary>
public static partial class RegrasDaProvisao
{
    /// <summary>As regiões quando a configuração não diz nenhuma.</summary>
    public static IReadOnlyList<string> RegioesPadrao { get; } = ["brazilsouth", "eastus2"];

    /// <summary>O teto do nome da conta de armazenamento no Azure (3 a 24, minúsculas e dígitos).</summary>
    public const int TamanhoMaximoDaConta = 24;

    /// <summary>O teto da mensagem de erro guardada.</summary>
    public const int TamanhoMaximoDoErro = 500;

    [GeneratedRegex("^[a-z0-9]{3,24}$", RegexOptions.CultureInvariant)]
    private static partial Regex Conta();

    /// <summary>
    /// A liberação: a permissão explícita do admin vence; sem ela, o plano da empresa do usuário quando está em
    /// <see cref="OpcoesDaProvisao.PlanosQueLiberam"/> (o tier de assinatura); senão, nada.
    /// </summary>
    public static string Origem(bool permissao, string? plano, IEnumerable<string> planosQueLiberam)
    {
        if (permissao)
            return ProvisaoV1.OrigemPermissao;
        return !string.IsNullOrWhiteSpace(plano) && planosQueLiberam.Any(p => string.Equals(p?.Trim(), plano.Trim(), StringComparison.OrdinalIgnoreCase))
            ? ProvisaoV1.OrigemPlano
            : ProvisaoV1.OrigemNenhuma;
    }

    /// <summary>As regiões aceitas, em minúsculas e sem repetição.</summary>
    public static IReadOnlyList<string> Regioes(OpcoesDaProvisao opcoes)
    {
        var lidas = (opcoes.Regioes ?? []).Select(Normalizar).Where(r => r.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        return lidas.Length > 0 ? lidas : RegioesPadrao;
    }

    /// <summary>A região padrão, sempre uma das aceitas.</summary>
    public static string RegiaoPadrao(OpcoesDaProvisao opcoes)
    {
        var regioes = Regioes(opcoes);
        var padrao = Normalizar(opcoes.RegiaoPadrao);
        return regioes.Contains(padrao) ? padrao : regioes[0];
    }

    /// <summary>A região do pedido (vazia é a padrão), ou nula quando não é aceita.</summary>
    public static string? Regiao(string? pedida, OpcoesDaProvisao opcoes)
    {
        var regiao = Normalizar(pedida);
        if (regiao.Length == 0)
            return RegiaoPadrao(opcoes);
        return Regioes(opcoes).Contains(regiao) ? regiao : null;
    }

    /// <summary>O grupo de recursos do usuário: um só por usuário, com o papel do worker limitado a ele.</summary>
    public static string NomeDoGrupo(string prefixo, int usuarioId)
        => $"{(string.IsNullOrWhiteSpace(prefixo) ? "rg-devkit-u" : prefixo.Trim())}{usuarioId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// O nome da conta de armazenamento: <c>dkq</c>, o usuário em base 36 e um sufixo ALEATÓRIO até 24 caracteres. O nome é
    /// global no Azure: o sufixo de 8 caracteres (36^8) torna a colisão improvável, e o índice único da base a barra aqui.
    /// </summary>
    public static string NomeDaConta(int usuarioId, string? sufixo = null)
    {
        var usuario = Base36(usuarioId);
        var aleatorio = sufixo ?? Aleatorio(8);
        var nome = $"dkq{usuario}{aleatorio}";
        return nome.Length <= TamanhoMaximoDaConta ? nome : nome[..TamanhoMaximoDaConta];
    }

    /// <summary>O nome é um nome de conta de armazenamento válido.</summary>
    public static bool ContaValida(string? nome) => nome is not null && Conta().IsMatch(nome);

    /// <summary>
    /// A espera antes da tentativa seguinte à <paramref name="tentativa"/> (1, 2, ...): a inicial, dobrando, até a máxima — o
    /// recuo exponencial que não martela o Azure no 429.
    /// </summary>
    public static TimeSpan Espera(int tentativa, OpcoesDaProvisao opcoes)
    {
        var inicial = Math.Max(1, opcoes.EsperaInicialSegundos);
        var maxima = Math.Max(inicial, opcoes.EsperaMaximaSegundos);
        var expoente = Math.Clamp(tentativa - 1, 0, 20);
        return TimeSpan.FromSeconds(Math.Min(maxima, inicial * Math.Pow(2, expoente)));
    }

    /// <summary>A falha transitória ainda tem tentativa: volta a <c>pedido</c>; senão, <c>falhou</c>.</summary>
    public static bool TentaDeNovo(bool transitoria, int tentativas, OpcoesDaProvisao opcoes)
        => transitoria && tentativas < Math.Max(1, opcoes.MaximoDeTentativas);

    /// <summary>Só o que falhou se repete.</summary>
    public static bool PodeRepetir(string estado) => estado == ProvisaoV1.EstadoFalhou;

    /// <summary>
    /// O recurso ATIVO conta na cota: tudo que não foi removido. O que falhou na CRIAÇÃO conta também — os nomes estão
    /// reservados e algo pode ter ficado no Azure; ele se repete ou se remove.
    /// </summary>
    public static bool Ativo(string estado) => estado != ProvisaoV1.EstadoRemovido;

    /// <summary>O que acontece com o pedido de remoção, pelo estado e pela operação do recurso.</summary>
    public static Remocao DecidirRemocao(string estado, string operacao) => (estado, operacao) switch
    {
        (ProvisaoV1.EstadoRemovido, _) => Remocao.JaFeita,
        (ProvisaoV1.EstadoPedido or ProvisaoV1.EstadoEmProvisao, ProvisaoV1.OperacaoRemover) => Remocao.JaPedida,
        (ProvisaoV1.EstadoPedido or ProvisaoV1.EstadoEmProvisao, _) => Remocao.AguardeACriacao,
        _ => Remocao.Pedir,
    };

    /// <summary>
    /// A mensagem de erro (e o detalhe da auditoria) como fica guardada: uma linha, sem segredo, caminho, URL nem GUID (a
    /// máscara dos relatos, <see cref="RegrasDosRelatos.Mascarar(string?, int)"/> — a mensagem do Azure traz o id da
    /// assinatura) e cortada no teto.
    /// </summary>
    public static string Erro(string? mensagem)
        => RegrasDosRelatos.Mascarar((mensagem ?? string.Empty).ReplaceLineEndings(" ").Trim(), TamanhoMaximoDoErro);

    private static string Normalizar(string? valor) => (valor ?? string.Empty).Trim().ToLowerInvariant();

    private static string Base36(int numero)
    {
        const string digitos = "0123456789abcdefghijklmnopqrstuvwxyz";
        var n = (uint)Math.Max(0, numero);
        if (n == 0)
            return "0";
        var texto = string.Empty;
        while (n > 0)
        {
            texto = digitos[(int)(n % 36)] + texto;
            n /= 36;
        }
        return texto;
    }

    private static string Aleatorio(int tamanho)
    {
        const string alfabeto = "abcdefghijklmnopqrstuvwxyz0123456789";
        return string.Create(tamanho, alfabeto, (destino, letras) =>
        {
            for (var i = 0; i < destino.Length; i++)
                destino[i] = letras[RandomNumberGenerator.GetInt32(letras.Length)];
        });
    }
}

/// <summary>A decisão sobre o pedido de remoção (<see cref="RegrasDaProvisao.DecidirRemocao"/>).</summary>
public enum Remocao
{
    /// <summary>Pronto ou falhou: a remoção é pedida agora.</summary>
    Pedir,

    /// <summary>A remoção já está na fila ou em execução: nada muda.</summary>
    JaPedida,

    /// <summary>O recurso já foi removido: nada muda.</summary>
    JaFeita,

    /// <summary>A criação ainda não terminou: 409, remove-se depois.</summary>
    AguardeACriacao,
}
