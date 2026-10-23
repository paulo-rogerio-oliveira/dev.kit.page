using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// Os relatos dos usuários do dev.kit (US #602). Quem relata é o usuário do token; quem ADMINISTRA recebe o
/// <see cref="EscopoDoPainel"/> — o mesmo filtro único do painel: o admin geral vê todos, o gestor só os da empresa
/// dele. Relato fora do escopo é tratado como inexistente (nulo → 404): o gestor não sonda outra empresa pelo id.
/// </summary>
public interface IRelatos
{
    /// <summary>
    /// Grava o relato JÁ validado (<see cref="RegrasDosRelatos.Validar"/>) como <c>novo</c>, com o texto e o contexto
    /// mascarados. A mesma <see cref="RelatoNovoV1.ChaveDoCliente"/> do mesmo usuário devolve o relato que já existe.
    /// </summary>
    Task<RelatoRecebidoV1> RegistrarAsync(int usuarioId, int? empresaId, RelatoNovoV1 relato, byte[]? captura, CancellationToken ct);

    /// <summary>Os relatos do escopo com o filtro, dos mais novos para os mais antigos, paginados.</summary>
    Task<Pagina<RelatoResumoV1>> ListarAsync(EscopoDoPainel escopo, FiltroDeRelatos filtro, CancellationToken ct);

    /// <summary>O relato inteiro; nulo quando não existe ou é de outra empresa.</summary>
    Task<RelatoDetalheV1?> DetalheAsync(EscopoDoPainel escopo, long id, CancellationToken ct);

    /// <summary>Troca o estado (já validado); nulo quando o relato não existe ou é de outra empresa.</summary>
    Task<RelatoDetalheV1?> AlterarEstadoAsync(EscopoDoPainel escopo, long id, string estado, CancellationToken ct);

    /// <summary>
    /// Responde (de novo, substitui) — o relato <c>novo</c> passa a <c>em-analise</c>: alguém já olhou. Nulo quando o
    /// relato não existe ou é de outra empresa.
    /// </summary>
    Task<RelatoDetalheV1?> ResponderAsync(EscopoDoPainel escopo, long id, int quemResponde, string texto, CancellationToken ct);

    /// <summary>Os relatos do PRÓPRIO usuário, com o estado e a resposta, dos mais novos para os mais antigos.</summary>
    Task<IReadOnlyList<MeuRelatoV1>> MeusAsync(int usuarioId, CancellationToken ct);
}

/// <summary>O filtro da lista da gestão. A empresa só vale para o admin (o gestor já está preso à dele).</summary>
public sealed record FiltroDeRelatos(
    string? Estado = null, string? Categoria = null, int? EmpresaId = null, DateOnly? De = null, DateOnly? Ate = null,
    int Pagina = 1, int Tamanho = 20);

/// <summary>
/// As regras puras dos relatos (US #602): validação do envio, da captura, do estado e da resposta, e a MÁSCARA. O
/// dev.kit já manda o texto e o contexto sanitizados (<c>TraceSanitizado</c>, no git.kit); aqui é a defesa em
/// profundidade — os segredos (token, senha, connection string) e, depois, a máscara do trace
/// (<see cref="RegrasDeErro.Mascarar"/>: caminhos, e-mails, URLs, GUIDs).
/// </summary>
public static partial class RegrasDosRelatos
{
    /// <summary>O tamanho do resumo da linha da lista.</summary>
    public const int TamanhoDoResumo = 140;

    /// <summary>O maior tamanho de página da lista.</summary>
    public const int TamanhoMaximoDaPagina = 100;

    /// <summary>O que entra no lugar de um segredo.</summary>
    public const string Segredo = "<segredo>";

    /// <summary>O que entra no lugar de uma connection string inteira.</summary>
    public const string ConnectionString = "<connection-string>";

    /// <summary>O problema do relato enviado, ou vazio quando ele vale.</summary>
    public static string Validar(RelatoNovoV1? relato)
    {
        if (relato is null)
            return "Envie o relato.";
        if (!CategoriasDeRelato.Todas.Contains(Normalizar(relato.Categoria)))
            return $"A categoria é {string.Join(", ", CategoriasDeRelato.Todas)}.";
        var texto = relato.Texto?.Trim() ?? string.Empty;
        if (texto.Length == 0)
            return "Escreva o relato: o que aconteceu, ou o que você sugere.";
        if (texto.Length > RelatosV1.TamanhoMaximoDoTexto)
            return $"O relato passa de {RelatosV1.TamanhoMaximoDoTexto} caracteres.";
        if ((relato.Contexto?.Length ?? 0) > RelatosV1.TamanhoMaximoDoContexto)
            return $"O contexto passa de {RelatosV1.TamanhoMaximoDoContexto} caracteres.";
        if ((relato.ChaveDoCliente?.Trim().Length ?? 0) > RelatosV1.TamanhoMaximoDaChave)
            return $"A chave do relato passa de {RelatosV1.TamanhoMaximoDaChave} caracteres.";
        if ((relato.VersaoDevKit?.Trim().Length ?? 0) > RelatosV1.TamanhoMaximoDaVersao)
            return $"A versão passa de {RelatosV1.TamanhoMaximoDaVersao} caracteres.";
        return LerCaptura(relato.Captura, out _);
    }

    /// <summary>
    /// A captura decodificada (nula sem captura) e o problema dela, ou vazio: base64 válido, até
    /// <see cref="RelatosV1.TamanhoMaximoDaCaptura"/> bytes e uma imagem PNG ou JPEG (pela assinatura do arquivo).
    /// </summary>
    public static string LerCaptura(string? base64, out byte[]? captura)
    {
        captura = null;
        var texto = base64?.Trim() ?? string.Empty;
        if (texto.Length == 0)
            return string.Empty;
        // O base64 tem 4 caracteres para cada 3 bytes: acima disto nem vale decodificar.
        if (texto.Length > (RelatosV1.TamanhoMaximoDaCaptura + 2) / 3 * 4)
            return $"A captura passa de {RelatosV1.TamanhoMaximoDaCaptura / (1024 * 1024)} MB.";

        var bytes = new byte[texto.Length * 3 / 4];
        if (!Convert.TryFromBase64String(texto, bytes, out var lidos))
            return "A captura não é um base64 válido.";
        if (lidos > RelatosV1.TamanhoMaximoDaCaptura)
            return $"A captura passa de {RelatosV1.TamanhoMaximoDaCaptura / (1024 * 1024)} MB.";
        var imagem = bytes.AsSpan(0, lidos);
        if (!EhPng(imagem) && !EhJpeg(imagem))
            return "A captura tem de ser uma imagem PNG ou JPEG.";
        captura = imagem.ToArray();
        return string.Empty;
    }

    /// <summary>O problema do estado pedido, ou vazio quando ele vale.</summary>
    public static string ValidarEstado(AlterarEstadoDoRelatoV1? pedido)
        => EstadosDeRelato.Todos.Contains(Normalizar(pedido?.Estado))
            ? string.Empty
            : $"O estado é {string.Join(", ", EstadosDeRelato.Todos)}.";

    /// <summary>O problema da resposta, ou vazio quando ela vale.</summary>
    public static string ValidarResposta(RespostaAoRelatoV1? resposta)
    {
        var texto = resposta?.Texto?.Trim() ?? string.Empty;
        if (texto.Length == 0)
            return "Escreva a resposta.";
        return texto.Length > RelatosV1.TamanhoMaximoDaResposta
            ? $"A resposta passa de {RelatosV1.TamanhoMaximoDaResposta} caracteres."
            : string.Empty;
    }

    /// <summary>O valor de categoria ou de estado como a base guarda: sem espaço nas pontas, em minúsculas.</summary>
    public static string Normalizar(string? valor) => (valor ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// O texto mascarado e cortado em <paramref name="teto"/>: primeiro os SEGREDOS (<see cref="MascararSegredos"/>),
    /// depois a máscara do trace (<see cref="RegrasDeErro.Mascarar"/>).
    /// </summary>
    public static string Mascarar(string? texto, int teto)
    {
        var mascarado = RegrasDeErro.Mascarar(MascararSegredos(texto));
        return mascarado.Length <= teto ? mascarado : mascarado[..teto];
    }

    /// <summary>
    /// Tira os segredos do texto: a connection string inteira (dois ou mais pares <c>chave=valor;</c> de banco ou de
    /// storage), o valor de <c>password=</c>/<c>senha:</c>/<c>token=</c>/<c>"apiKey": "..."</c>, o <c>Bearer</c>, o JWT
    /// e os tokens com formato conhecido (GitHub, Azure DevOps, AWS, Slack, chaves <c>sk-</c>).
    /// </summary>
    public static string MascararSegredos(string? texto)
    {
        var resultado = texto ?? string.Empty;
        resultado = ConexaoDeBanco().Replace(resultado, ConnectionString);
        resultado = ChaveValor().Replace(resultado, m =>
        {
            var valor = m.Groups["valor"].Value;
            var aspas = valor.Length > 0 && valor[0] is '"' or '\'' ? valor[0].ToString() : string.Empty;
            return m.Groups["chave"].Value + m.Groups["sep"].Value + aspas + Segredo + aspas;
        });
        resultado = Bearer().Replace(resultado, "Bearer " + Segredo);
        resultado = Jwt().Replace(resultado, Segredo);
        resultado = TokenConhecido().Replace(resultado, Segredo);
        return resultado;
    }

    /// <summary>O começo do texto para a linha da lista.</summary>
    public static string Resumo(string texto)
    {
        var linha = Espacos().Replace(texto, " ").Trim();
        return linha.Length <= TamanhoDoResumo ? linha : linha[..TamanhoDoResumo].TrimEnd() + "…";
    }

    private static bool EhPng(ReadOnlySpan<byte> b)
        => b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    private static bool EhJpeg(ReadOnlySpan<byte> b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    [GeneratedRegex(@"(?:\b(?:server|data source|address|addr|initial catalog|database|user id|userid|uid|user|username|password|pwd|integrated security|trusted_connection|encrypt|trustservercertificate|persist security info|multipleactiveresultsets|connection timeout|host|port|accountname|accountkey|sharedaccesskeyname|sharedaccesskey|sharedaccesssignature|endpoint|defaultendpointsprotocol|endpointsuffix)\s*=\s*[^;\r\n""]*(?:;|$|(?=""))[ \t]*){2,}",
        RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ConexaoDeBanco();

    [GeneratedRegex(@"(?<chave>[""']?\b(?:password|passwd|pwd|senha|secret|segredo|client[_-]?secret|api[_-]?key|apikey|access[_-]?key|access[_-]?token|refresh[_-]?token|auth[_-]?token|token|pat|private[_-]?key)\b[""']?)(?<sep>\s*[:=]\s*)(?<valor>""[^""]*""|'[^']*'|[^\s;,&""'}]+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ChaveValor();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|xox[abprs]-[A-Za-z0-9-]{10,}|sk-[A-Za-z0-9_-]{20,}|[A-Za-z0-9]{84}|[a-z0-9]{52})\b")]
    private static partial Regex TokenConhecido();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();
}
