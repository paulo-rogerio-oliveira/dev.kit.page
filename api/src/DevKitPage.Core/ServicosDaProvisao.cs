using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// A provisão vista pela API pública (projeto controle-remoto, item 4): grava o pedido e lê o estado, sempre do usuário do
/// TOKEN. Ela não fala com o Azure — quem executa é o <see cref="IMotorDeProvisao"/>, num processo separado, com identidade
/// própria. O recurso de outro usuário é 404, sem revelar que existe.
/// </summary>
public interface IProvisao
{
    /// <summary>Se o usuário pode pedir, onde, e os recursos dele.</summary>
    Task<SituacaoDaProvisaoV1> SituacaoAsync(int usuario, CancellationToken ct);

    /// <summary>
    /// Pede a fila do usuário. Sem a liberação, 403. Idempotente: com uma fila ativa (ou a mesma chave do cliente), devolve
    /// ela com <see cref="PedidoAceito.Novo"/> falso; a anterior ainda sendo removida é 409.
    /// </summary>
    Task<ResultadoDaGestao<PedidoAceito>> PedirFilaAsync(int usuario, PedidoDeFilaV1 pedido, CancellationToken ct);

    /// <summary>O recurso com o histórico; nulo quando não é dele ou não existe.</summary>
    Task<RecursoNaNuvemV1?> RecursoAsync(int usuario, long id, CancellationToken ct);

    /// <summary>Repete a operação que falhou, com as tentativas zeradas. Repetir a criação exige a liberação; a remoção, não.</summary>
    Task<ResultadoDaGestao<RecursoNaNuvemV1>> RepetirAsync(int usuario, long id, CancellationToken ct);

    /// <summary>
    /// Pede a remoção do recurso pronto ou que falhou (a criação pela metade também se limpa). Não exige a liberação: remover
    /// só reduz o custo. Pedir de novo enquanto remove, ou já removido, devolve o mesmo recurso.
    /// </summary>
    Task<ResultadoDaGestao<RecursoNaNuvemV1>> RemoverAsync(int usuario, long id, CancellationToken ct);
}

/// <summary>O pedido aceito e se ele criou um recurso agora (202) ou devolveu o que já existia (200).</summary>
public sealed record PedidoAceito(RecursoNaNuvemV1 Recurso, bool Novo);

/// <summary>A liberação da provisão usuário a usuário, só do admin.</summary>
public interface ILiberacoesDaProvisao
{
    /// <summary>Todas as liberações gravadas, inclusive as revogadas (com quem revogou).</summary>
    Task<IReadOnlyList<LiberacaoDaProvisaoV1>> ListarAsync(CancellationToken ct);

    /// <summary>Libera ou revoga; o usuário inexistente é 404. Revogar não mexe nos recursos que ele já tem.</summary>
    Task<ResultadoDaGestao<LiberacaoDaProvisaoV1>> DefinirAsync(int usuario, bool liberada, string autor, CancellationToken ct);
}

/// <summary>
/// O motor da provisão, que roda no trabalhador: pega UM pedido vencido (com a concessão no SQL, para dois trabalhadores não
/// pegarem o mesmo), executa no <see cref="IProvedorDeNuvem"/> e grava o desfecho e a auditoria.
/// </summary>
public interface IMotorDeProvisao
{
    /// <summary>Processa o próximo pedido; falso quando não havia nenhum.</summary>
    Task<bool> ProcessarProximoAsync(string trabalhador, CancellationToken ct);
}

/// <summary>
/// O Azure como o motor o vê: garantir e remover a fila de um usuário. As duas operações são IDEMPOTENTES — o mesmo alvo pode
/// chegar de novo depois de uma queda no meio, e o resultado é o mesmo recurso. A implementação de verdade fica no projeto do
/// trabalhador, com a identidade gerenciada dele; a API não a referencia.
/// </summary>
public interface IProvedorDeNuvem
{
    Task<ResultadoNaNuvem> GarantirFilaAsync(AlvoNaNuvem alvo, CancellationToken ct);

    Task<ResultadoNaNuvem> RemoverFilaAsync(AlvoNaNuvem alvo, CancellationToken ct);
}

/// <summary>O que criar ou remover: os nomes já calculados pela API e as etiquetas de custo.</summary>
public sealed record AlvoNaNuvem(string GrupoDeRecursos, string Conta, string Fila, string Regiao, IReadOnlyDictionary<string, string> Etiquetas);

/// <summary>Como o Azure respondeu.</summary>
public enum SituacaoNaNuvem
{
    /// <summary>Feito: o recurso existe (ou não existe mais, na remoção).</summary>
    Pronto,

    /// <summary>O Azure aceitou e ainda está fazendo: o motor volta depois, sem contar falha.</summary>
    EmAndamento,

    /// <summary>Rede, limite (429) ou erro do Azure (5xx): conta uma falha e tenta de novo com espera.</summary>
    FalhaTransitoria,

    /// <summary>Recusa que repetir não muda (permissão, nome tomado, região inválida): falhou na hora.</summary>
    FalhaDefinitiva,
}

/// <summary>A resposta do provedor, com o motivo para a auditoria (passa pela <see cref="RegrasDaProvisao.MensagemSegura"/>).</summary>
public sealed record ResultadoNaNuvem(SituacaoNaNuvem Situacao, string Mensagem = "");

/// <summary>
/// As regras puras da provisão, sem banco nem Azure: os nomes, a liberação, o pedido, a espera entre tentativas e o que pode
/// acontecer em cada estado.
/// </summary>
public static partial class RegrasDaProvisao
{
    /// <summary>O prefixo do grupo quando o configurado não serve.</summary>
    public const string PrefixoPadrao = "rg-devkit";

    /// <summary>O grupo de recursos do usuário: um por usuário, para o papel, a cota e o custo ficarem nele.</summary>
    public static string GrupoDeRecursos(string? prefixo, int usuario)
    {
        var limpo = (prefixo ?? string.Empty).Trim().ToLowerInvariant();
        return $"{(PrefixoValido().IsMatch(limpo) ? limpo : PrefixoPadrao)}-u{usuario}";
    }

    /// <summary>
    /// O nome da conta de armazenamento, único no Azure inteiro: <c>dk</c> e 16 caracteres do SHA-256 de (ambiente, usuário,
    /// geração) em base32 minúsculo. Determinístico de propósito: repetir a criação acerta a MESMA conta, e o id do usuário não
    /// aparece no nome público.
    /// </summary>
    public static string NomeDaConta(string? ambiente, int usuario, int geracao)
    {
        var semente = $"devkit-provisao:{(ambiente ?? string.Empty).Trim().ToLowerInvariant()}:{usuario}:{geracao}";
        return "dk" + Base32(SHA256.HashData(Encoding.UTF8.GetBytes(semente)))[..16];
    }

    /// <summary>A regra do Azure para a conta de armazenamento: 3 a 24 letras minúsculas e dígitos.</summary>
    public static bool NomeDaContaValido(string? nome) => nome is not null && ContaRegex().IsMatch(nome);

    /// <summary>A regra do Azure para a fila: 3 a 63 minúsculas, dígitos e hífen, sem hífen nas pontas nem dois seguidos.</summary>
    public static bool NomeDaFilaValido(string? nome) => nome is not null && FilaRegex().IsMatch(nome) && !nome.Contains("--", StringComparison.Ordinal);

    /// <summary>
    /// De onde vem a liberação: o admin (<see cref="ProvisaoV1.OrigemUsuario"/>) ou o plano da empresa
    /// (<see cref="ProvisaoV1.OrigemPlano"/>, o tier de assinatura); nula sem nenhuma das duas.
    /// </summary>
    public static string? Origem(bool liberadaPeloAdmin, string? plano, IEnumerable<string> planosQueLiberam)
    {
        if (liberadaPeloAdmin)
            return ProvisaoV1.OrigemUsuario;
        var meu = (plano ?? string.Empty).Trim();
        return meu.Length > 0 && planosQueLiberam.Any(p => string.Equals((p ?? string.Empty).Trim(), meu, StringComparison.OrdinalIgnoreCase))
            ? ProvisaoV1.OrigemPlano
            : null;
    }

    /// <summary>A região como a base guarda: sem espaço nas pontas, em minúsculas.</summary>
    public static string NormalizarRegiao(string? regiao) => (regiao ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>O motivo da recusa do pedido, ou nulo quando ele vale.</summary>
    public static string? Validar(PedidoDeFilaV1? pedido, IReadOnlyList<string> regioes)
    {
        if (pedido is null)
            return "Envie o pedido com a região.";
        var regiao = NormalizarRegiao(pedido.Regiao);
        if (!regioes.Contains(regiao))
            return $"Região não aceita: escolha uma de {string.Join(", ", regioes)}.";
        var chave = pedido.ChaveDoCliente ?? string.Empty;
        if (chave.Length > ProvisaoV1.TamanhoMaximoDaChaveDoCliente || (chave.Length > 0 && !ChaveRegex().IsMatch(chave)))
            return $"A chave do cliente tem até {ProvisaoV1.TamanhoMaximoDaChaveDoCliente} letras, dígitos, hífen ou sublinhado.";
        return null;
    }

    /// <summary>A espera antes da tentativa seguinte: a inicial dobrando a cada falha, até a máxima.</summary>
    public static TimeSpan Espera(int falhas, int inicialSegundos, int maximaSegundos)
    {
        var inicial = Math.Max(1, inicialSegundos);
        var maxima = Math.Max(inicial, maximaSegundos);
        var expoente = Math.Clamp(falhas - 1, 0, 20);
        return TimeSpan.FromSeconds(Math.Min((double)inicial * (1L << expoente), maxima));
    }

    /// <summary>O recurso ainda conta para o usuário (só o removido sai da conta).</summary>
    public static bool Ativo(string estado) => !string.Equals(estado, ProvisaoV1.Removido, StringComparison.Ordinal);

    /// <summary>O trabalhador ainda vai mexer nele: pedido ou em provisão.</summary>
    public static bool EmAndamento(string estado) => estado is ProvisaoV1.Pedido or ProvisaoV1.EmProvisao;

    /// <summary>Só o que falhou se repete: o resto já está andando, pronto ou removido.</summary>
    public static bool PodeRepetir(string estado) => estado == ProvisaoV1.Falhou;

    /// <summary>
    /// Por que o recurso não pode ser removido agora, ou nulo quando pode. A criação em andamento termina primeiro: remover no
    /// meio dela deixaria o trabalhador criando o que acabou de apagar.
    /// </summary>
    public static string? PorQueNaoRemove(string operacao, string estado)
        => operacao == ProvisaoV1.OperacaoCriar && EmAndamento(estado)
            ? "A fila ainda está sendo criada: espere ela ficar pronta ou falhar para remover."
            : null;

    /// <summary>
    /// O texto que vai para a auditoria e para a tela: sem segredo (a máscara dos relatos), sem caminho, URL nem GUID (a máscara
    /// do trace, que tira o id da assinatura e os caminhos do ARM), numa linha só e no teto do contrato.
    /// </summary>
    public static string MensagemSegura(string? texto)
    {
        var linha = Espacos().Replace(texto ?? string.Empty, " ").Trim();
        return RegrasDosRelatos.Mascarar(linha, ProvisaoV1.TamanhoMaximoDaMensagem);
    }

    /// <summary>As etiquetas de todo recurso criado: é por elas que o custo de cada usuário sai na análise de custos do Azure.</summary>
    public static IReadOnlyDictionary<string, string> Etiquetas(string? ambiente, int usuario, long recurso) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["devkit-ambiente"] = (ambiente ?? string.Empty).Trim().ToLowerInvariant(),
        ["devkit-usuario"] = usuario.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["devkit-recurso"] = recurso.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>Base32 (RFC 4648) em minúsculas, sem preenchimento: só letras e os dígitos 2–7, que a conta aceita.</summary>
    private static string Base32(byte[] dados)
    {
        const string Alfabeto = "abcdefghijklmnopqrstuvwxyz234567";
        var texto = new StringBuilder((dados.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in dados)
        {
            buffer = ((buffer << 8) | b) & 0xFFFF; // só os bits ainda não escritos (no máximo 12) importam
            bits += 8;
            while (bits >= 5)
            {
                texto.Append(Alfabeto[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            texto.Append(Alfabeto[(buffer << (5 - bits)) & 31]);
        return texto.ToString();
    }

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9._-]{0,59}\z", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixoValido();

    [GeneratedRegex(@"\A[a-z0-9]{3,24}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ContaRegex();

    [GeneratedRegex(@"\A[a-z0-9][a-z0-9-]{1,61}[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex FilaRegex();

    [GeneratedRegex(@"\A[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex ChaveRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();
}
