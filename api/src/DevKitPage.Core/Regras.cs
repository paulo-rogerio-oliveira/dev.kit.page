using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// A chave de uma máquina: aleatória, devolvida UMA vez no registro, guardada só como hash SHA-256.
/// SHA-256 sem sal é suficiente aqui (e não o PasswordHasher): a chave tem 256 bits de entropia, não
/// é uma senha escolhida por gente, e o hash precisa ser BUSCÁVEL — cada lote procura a máquina por ele.
/// </summary>
public static class ChaveDeMaquina
{
    /// <summary>Uma chave nova e o hash que vai para o banco.</summary>
    public static (string Chave, string Hash) Gerar()
    {
        var chave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (chave, Hash(chave));
    }

    /// <summary>O hash da chave (hex minúsculo).</summary>
    public static string Hash(string chave) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chave ?? string.Empty)));

    /// <summary>Compara dois segredos em tempo constante (o código de registro, por exemplo).</summary>
    public static bool Iguais(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a ?? string.Empty), Encoding.UTF8.GetBytes(b ?? string.Empty));
}

/// <summary>Por que um lote foi recusado: o status HTTP e a mensagem.</summary>
public sealed record RecusaDoLote(int Status, string Mensagem);

/// <summary>A validação do lote v1 ANTES de tocar no banco.</summary>
public static class ValidadorDeLote
{
    /// <summary>Tamanho máximo dos textos de um evento (o que passar disto é cortado, não recusado).</summary>
    public const int TamanhoMaximoDoTexto = 200;

    /// <summary>A recusa, ou nula quando o lote pode seguir.</summary>
    public static RecusaDoLote? Validar(TelemetryBatchV1? lote, string maquinaAutenticada, int maxEventos)
    {
        if (lote is null)
            return new RecusaDoLote(400, "O corpo do lote é obrigatório.");
        if (!string.Equals(lote.Versao, ContratoV1.Versao, StringComparison.Ordinal))
            return new RecusaDoLote(400, $"Versão do contrato não suportada: '{lote.Versao}'. Esta rota aceita '{ContratoV1.Versao}'.");
        if (!string.Equals(lote.MaquinaId, maquinaAutenticada, StringComparison.OrdinalIgnoreCase))
            return new RecusaDoLote(400, "O lote diz ser de outra máquina que não a da chave.");
        if (lote.Eventos is null)
            return new RecusaDoLote(400, "O lote não tem a lista de eventos.");
        if (lote.Eventos.Count > maxEventos)
            return new RecusaDoLote(413, $"O lote tem {lote.Eventos.Count} eventos; o máximo é {maxEventos}.");

        foreach (var evento in lote.Eventos)
        {
            if (evento is null || string.IsNullOrWhiteSpace(evento.EventId) || evento.EventId.Length > 64)
                return new RecusaDoLote(400, "Todo evento precisa de um eventId (até 64 caracteres).");
            if (evento.Quantidade < 0)
                return new RecusaDoLote(400, $"Quantidade negativa no evento {evento.EventId}.");
        }

        return null;
    }

    /// <summary>O texto cortado no tamanho que o banco guarda.</summary>
    public static string Cortar(string? texto)
    {
        var limpo = (texto ?? string.Empty).Trim();
        return limpo.Length <= TamanhoMaximoDoTexto ? limpo : limpo[..TamanhoMaximoDoTexto];
    }
}

/// <summary>
/// A validação do pedido de demonstração ANTES de tocar no banco — a mesma que a web aplica no
/// formulário. Os erros vêm por campo (o <c>ValidationProblem</c> da API).
/// </summary>
public static class ValidadorDeDemonstracao
{
    public const int TamanhoMaximoDoNome = 100;
    public const int TamanhoMaximoDoEmail = 254;
    public const int TamanhoMaximoDaEmpresa = 100;
    public const int TamanhoMaximoDaMensagem = 1000;

    /// <summary>Os erros por campo (nome do campo em camelCase); vazio quando o pedido pode seguir.</summary>
    public static Dictionary<string, string[]> Validar(PedidoDeDemonstracaoV1? pedido)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (pedido is null)
        {
            erros["pedido"] = ["O corpo do pedido é obrigatório."];
            return erros;
        }

        var nome = (pedido.Nome ?? string.Empty).Trim();
        if (nome.Length == 0)
            erros["nome"] = ["Informe o seu nome."];
        else if (nome.Length > TamanhoMaximoDoNome)
            erros["nome"] = [$"O nome tem até {TamanhoMaximoDoNome} caracteres."];

        var email = (pedido.Email ?? string.Empty).Trim();
        if (email.Length == 0)
            erros["email"] = ["Informe o seu e-mail."];
        else if (email.Length > TamanhoMaximoDoEmail || !EmailValido(email))
            erros["email"] = ["Informe um e-mail válido."];

        if ((pedido.Empresa ?? string.Empty).Trim().Length > TamanhoMaximoDaEmpresa)
            erros["empresa"] = [$"A empresa tem até {TamanhoMaximoDaEmpresa} caracteres."];
        if ((pedido.Mensagem ?? string.Empty).Trim().Length > TamanhoMaximoDaMensagem)
            erros["mensagem"] = [$"A mensagem tem até {TamanhoMaximoDaMensagem} caracteres."];
        if (!pedido.Consentimento)
            erros["consentimento"] = ["É preciso concordar com o uso dos dados para o contato."];

        return erros;
    }

    /// <summary>Um endereço com uma arroba, sem espaço, e um ponto no domínio — o resto o contato confirma.</summary>
    public static bool EmailValido(string email)
    {
        var arroba = email.IndexOf('@');
        if (arroba <= 0 || arroba != email.LastIndexOf('@') || email.Any(char.IsWhiteSpace))
            return false;
        var dominio = email[(arroba + 1)..];
        var ponto = dominio.LastIndexOf('.');
        return ponto > 0 && ponto < dominio.Length - 1;
    }
}

/// <summary>
/// As regras da foto do ROI (US #387). A foto que vem torta (item sem id) é DESCARTADA, e não recusa o
/// lote: o evento continua contando no total diário, e o dev.kit não fica reenviando um lote que a API
/// nunca aceitaria. Os textos são cortados e os números, trazidos para a faixa que a coluna guarda — o
/// mesmo espírito do <see cref="ValidadorDeLote.Cortar"/>.
/// </summary>
public static class RegrasDeRoi
{
    /// <summary>O tamanho máximo do tipo e do estado do work item.</summary>
    public const int TamanhoMaximoDoTexto = 50;

    /// <summary>A precisão das horas (decimal(10,2)): o maior valor que a coluna guarda.</summary>
    public const decimal HorasMaximas = 99_999_999.99m;

    /// <summary>A foto pronta para gravar (ainda sem máquina, evento e instante), ou nula quando não há foto válida.</summary>
    public static RoiDeWorkItem? Foto(RoiV1? roi)
    {
        if (roi is null || roi.WorkItem <= 0)
            return null;

        return new RoiDeWorkItem
        {
            WorkItemId = roi.WorkItem,
            Tipo = Cortar(roi.Tipo),
            Estado = Cortar(roi.Estado),
            De = roi.De <= roi.Ate ? roi.De : roi.Ate,
            Ate = roi.De <= roi.Ate ? roi.Ate : roi.De,
            TurnosDoAgente = Math.Max(0, roi.TurnosDoAgente),
            Sessoes = Math.Max(0, roi.Sessoes),
            Horas = Horas(roi.Horas),
            HorasNoBoard = Horas(roi.HorasNoBoard),
            HorasNoTimesheet = roi.HorasNoTimesheet is { } timesheet ? Horas(timesheet) : null,
            LeadTimeDias = roi.LeadTimeDias is { } lead && double.IsFinite(lead) && lead >= 0 ? Math.Round(lead, 2) : null,
            Aberto = roi.Aberto,
            PullRequests = Math.Max(0, roi.PullRequests),
            // A mergeada é uma das PRs: nunca mais que o total.
            PullRequestsMergeadas = Math.Clamp(roi.PullRequestsMergeadas, 0, Math.Max(0, roi.PullRequests)),
        };
    }

    /// <summary>Horas por turno do agente, com duas casas; nula sem turno (o divisor zero vira traço na tela).</summary>
    public static decimal? HorasPorTurno(decimal horas, int turnos) => turnos <= 0 ? null : Math.Round(horas / turnos, 2);

    private static decimal Horas(decimal horas) => Math.Round(Math.Clamp(horas, 0m, HorasMaximas), 2);

    private static string Cortar(string? texto)
    {
        var limpo = (texto ?? string.Empty).Trim();
        return limpo.Length <= TamanhoMaximoDoTexto ? limpo : limpo[..TamanhoMaximoDoTexto];
    }
}

/// <summary>A política de senha do dashboard.</summary>
public static class PoliticaDeSenha
{
    public const int TamanhoMinimo = 10;

    /// <summary>O que falta na senha, ou vazio quando ela serve.</summary>
    public static string Validar(string? senha)
    {
        senha ??= string.Empty;
        if (senha.Length < TamanhoMinimo)
            return $"A senha precisa de pelo menos {TamanhoMinimo} caracteres.";
        if (!senha.Any(char.IsLetter) || !senha.Any(char.IsDigit))
            return "A senha precisa de letras e números.";
        return string.Empty;
    }
}

/// <summary>
/// O gerador ÚNICO de senha temporária (US #405): a do admin semeado sem <c>Seed:AdminPassword</c>, a do
/// gestor convidado e a do usuário criado ou redefinido pelo admin. Aleatória (64 bits do gerador
/// criptográfico) e sempre dentro da <see cref="PoliticaDeSenha"/> — ela vale até o primeiro acesso, que
/// exige a troca.
/// </summary>
public static class GeradorDeSenha
{
    /// <summary>Uma senha temporária nova, como <c>Dk3F9A0C2E7B1D4A6a1</c>.</summary>
    public static string Temporaria() => "Dk" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + "a1";
}

/// <summary>A validação do usuário que o admin cria ou edita (US #405), antes do banco.</summary>
public static class ValidadorDeUsuario
{
    public const int TamanhoMaximoDoLogin = 100;
    public const int TamanhoMaximoDoNome = 100;

    /// <summary>O problema do login (sem espaços, até 100 caracteres), ou vazio — <paramref name="quem"/> nomeia o usuário na mensagem.</summary>
    public static string ValidarLogin(string? login, string quem = "usuário")
    {
        var limpo = (login ?? string.Empty).Trim();
        if (limpo.Length is 0 or > TamanhoMaximoDoLogin || limpo.Any(char.IsWhiteSpace))
            return $"Informe o login do {quem}, sem espaços (até {TamanhoMaximoDoLogin} caracteres).";
        return string.Empty;
    }

    /// <summary>Os erros por campo do usuário novo; vazio quando ele pode ser criado.</summary>
    public static Dictionary<string, string[]> Validar(UsuarioNovo? usuario)
    {
        if (usuario is null)
            return new(StringComparer.Ordinal) { ["usuario"] = ["O corpo do pedido é obrigatório."] };

        var erros = Validar(usuario.Nome, usuario.Papel, usuario.EmpresaId);
        var login = ValidarLogin(usuario.Login);
        if (login.Length > 0)
            erros["login"] = [login];
        return erros;
    }

    /// <summary>Os erros por campo da edição; vazio quando ela pode ser gravada.</summary>
    public static Dictionary<string, string[]> Validar(UsuarioEditado? edicao)
        => edicao is null
            ? new(StringComparer.Ordinal) { ["usuario"] = ["O corpo do pedido é obrigatório."] }
            : Validar(edicao.Nome, edicao.Papel, edicao.EmpresaId);

    /// <summary>
    /// O nome (até 100), o papel (um de <see cref="Papeis.Todos"/>) e a empresa: o gestor PRECISA de uma (é
    /// o escopo dele no painel); o admin não tem (vê tudo); o dev pode ter ou não.
    /// </summary>
    private static Dictionary<string, string[]> Validar(string? nome, string? papel, int? empresaId)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if ((nome ?? string.Empty).Trim().Length > TamanhoMaximoDoNome)
            erros["nome"] = [$"O nome tem até {TamanhoMaximoDoNome} caracteres."];
        if (!Papeis.Todos.Contains(papel ?? string.Empty))
            erros["papel"] = [$"Papel inválido: use {string.Join(", ", Papeis.Todos)}."];
        else if (papel == Papeis.Gestor && empresaId is null or <= 0)
            erros["empresaId"] = ["O gestor precisa de uma empresa."];
        else if (papel == Papeis.Admin && empresaId is not null)
            erros["empresaId"] = ["O admin não pertence a uma empresa: ele vê todas."];
        return erros;
    }
}

/// <summary>
/// A versão do dev.kit a partir das GitHub Releases (US #405) — regra pura, sem HTTP. A tag <c>vNNN</c> é o
/// número do dev.kit; a legada <c>vX.Y.Z</c> vale X; prerelease, rascunho e tag ilegível ficam de fora. Os
/// destaques são os itens de lista do corpo da release, sem markdown.
/// </summary>
public static partial class RegrasDeVersao
{
    /// <summary>Quantos destaques a versão mostra (o app e a landing não são o changelog).</summary>
    public const int MaximoDeDestaques = 5;

    /// <summary>O tamanho máximo de um destaque.</summary>
    public const int TamanhoMaximoDoDestaque = 200;

    /// <summary>
    /// A versão do dev.kit da tag: <c>v136</c> é 136, a legada <c>v1.2.3</c> é 1. Nula para a tag ilegível
    /// (<c>latest</c>, <c>v1.2.3-beta</c>, <c>v0</c>) — que fica fora da lista, em vez de virar a "última".
    /// </summary>
    public static int? Versao(string? tag)
    {
        var lido = Tag().Match((tag ?? string.Empty).Trim());
        return lido.Success && int.TryParse(lido.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var versao) && versao > 0
            ? versao
            : null;
    }

    /// <summary>O que a regra lê de uma release do GitHub.</summary>
    public sealed record Candidata(string? Tag, bool Prerelease, bool Rascunho, DateTimeOffset? PublicadaEm);

    /// <summary>
    /// As releases ESTÁVEIS com a versão de cada uma, da mais nova para a mais velha — pela versão NUMÉRICA
    /// (v136 vem antes de v99, que a ordem de texto inverteria). Duas tags da mesma versão (a legada v1.2.3
    /// e a v1.5.0) ficam uma só: a publicada por último.
    /// </summary>
    public static IReadOnlyList<(T Release, int Versao)> Estaveis<T>(IEnumerable<T> releases, Func<T, Candidata> ler)
        => releases
            .Select(r => (Release: r, Lida: ler(r)))
            .Where(x => !x.Lida.Prerelease && !x.Lida.Rascunho)
            .Select(x => (x.Release, x.Lida, Versao: Versao(x.Lida.Tag)))
            .Where(x => x.Versao is not null)
            .OrderByDescending(x => x.Versao).ThenByDescending(x => x.Lida.PublicadaEm ?? DateTimeOffset.MinValue)
            .GroupBy(x => x.Versao!.Value)
            .Select(g => (g.First().Release, g.Key))
            .ToArray();

    /// <summary>Os itens de lista (<c>- </c>, <c>* </c>, <c>+ </c>) do corpo da release, sem markdown, no máximo <see cref="MaximoDeDestaques"/>.</summary>
    public static IReadOnlyList<string> Destaques(string? corpo)
        => (corpo ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 2 && (l[0] is '-' or '*' or '+') && l[1] == ' ')
            .Select(l => SemMarkdown(l[2..]))
            .Where(l => l.Length > 0)
            .Take(MaximoDeDestaques)
            .ToArray();

    /// <summary>O texto de um item sem a marcação: caixa de seleção, imagem, link (fica o texto), ênfase, código e HTML.</summary>
    public static string SemMarkdown(string texto)
    {
        var limpo = Caixa().Replace(texto, string.Empty);
        limpo = Imagem().Replace(limpo, string.Empty);
        limpo = Link().Replace(limpo, "$1");
        limpo = Html().Replace(limpo, string.Empty);
        limpo = limpo.Replace("**", string.Empty, StringComparison.Ordinal).Replace("__", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);
        limpo = Espacos().Replace(limpo, " ").Trim();
        return limpo.Length <= TamanhoMaximoDoDestaque ? limpo : limpo[..TamanhoMaximoDoDestaque].TrimEnd() + "…";
    }

    /// <summary>O SHA-256 do <c>digest</c> do asset (<c>sha256:…</c>) em hex minúsculo; nulo quando não é um.</summary>
    public static string? Sha256DoDigest(string? digest)
    {
        var texto = (digest ?? string.Empty).Trim();
        return texto.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? Sha256DoArquivo(texto["sha256:".Length..]) : null;
    }

    /// <summary>
    /// O SHA-256 do arquivo <c>&lt;zip&gt;.sha256</c> — o formato do <c>sha256sum</c> (<c>hash  nome</c>) ou só o
    /// hash, em qualquer caixa —, em hex minúsculo; nulo sem um hash de 64 caracteres.
    /// </summary>
    public static string? Sha256DoArquivo(string? conteudo)
    {
        var achado = Hash().Match(conteudo ?? string.Empty);
        return achado.Success ? achado.Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"^[vV](\d{1,9})(?:\.\d{1,9}){0,3}$")]
    private static partial Regex Tag();

    [GeneratedRegex(@"^\[[ xX]\]\s*")]
    private static partial Regex Caixa();

    [GeneratedRegex(@"!\[[^\]]*\]\([^)]*\)")]
    private static partial Regex Imagem();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Html();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    [GeneratedRegex(@"(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])")]
    private static partial Regex Hash();
}

/// <summary>O período de uma consulta: os dois dias inclusive.</summary>
public readonly record struct Periodo(DateOnly De, DateOnly Ate)
{
    /// <summary>Dias padrão quando a consulta não diz.</summary>
    public const int DiasPadrao = 30;

    /// <summary>O período pedido, com os padrões (os últimos 30 dias até hoje) e a ordem garantida.</summary>
    public static Periodo Pedido(DateOnly? de, DateOnly? ate, DateOnly hoje)
    {
        var fim = ate ?? hoje;
        var inicio = de ?? fim.AddDays(-(DiasPadrao - 1));
        return inicio <= fim ? new Periodo(inicio, fim) : new Periodo(fim, inicio);
    }
}

/// <summary>
/// As regras das exceções não classificadas (US #381): o limite e a máscara do trace, a assinatura,
/// as transições de estado e a regressão por versão.
/// <para>
/// O trace chega JÁ sanitizado pelo dev.kit (<c>TraceSanitizado</c>, no git.kit); a máscara daqui é
/// a DEFESA EM PROFUNDIDADE — um dev.kit com defeito no sanitizador não grava caminho, e-mail nem
/// URL no banco do dashboard. O <see cref="ValidadorDeLote.TamanhoMaximoDoTexto"/> do recorte não
/// muda: o trace tem campo e limite próprios.
/// </para>
/// </summary>
public static partial class RegrasDeErro
{
    /// <summary>O tamanho máximo do trace guardado (8 KB); o que passar é cortado, não recusado.</summary>
    public const int TamanhoMaximoDoTrace = 8192;

    /// <summary>O tamanho máximo da assinatura.</summary>
    public const int TamanhoMaximoDaAssinatura = 64;

    /// <summary>O tamanho máximo da versão informada ao resolver.</summary>
    public const int TamanhoMaximoDaVersao = 50;

    /// <summary>Quantas ocorrências (com o trace) cada grupo guarda: as mais recentes.</summary>
    public const int OcorrenciasGuardadasPorGrupo = 20;

    /// <summary>O trace mascarado e cortado no tamanho que o banco guarda.</summary>
    public static string Mascarar(string? trace)
    {
        var texto = (trace ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        texto = Url().Replace(texto, "<url>");
        texto = RemotoSsh().Replace(texto, "<url>");
        texto = Email().Replace(texto, "<email>");
        texto = CaminhoUnc().Replace(texto, "<caminho>");
        texto = CaminhoWindows().Replace(texto, "<caminho>");
        texto = CaminhoGitBash().Replace(texto, "<caminho>");
        texto = CaminhoUnix().Replace(texto, "<caminho>");
        texto = UsuarioDeDominio().Replace(texto, "<usuario>");
        texto = Guid().Replace(texto, "<guid>");
        return texto.Length <= TamanhoMaximoDoTrace ? texto : texto[..TamanhoMaximoDoTrace];
    }

    /// <summary>
    /// A assinatura do evento — a que o dev.kit mandou ou, sem ela (um cliente com defeito), a
    /// derivada do trace: o grupo nunca fica sem chave.
    /// </summary>
    public static string Assinatura(string? assinatura, string trace)
    {
        var informada = (assinatura ?? string.Empty).Trim();
        if (informada.Length > 0)
            return informada.Length <= TamanhoMaximoDaAssinatura ? informada : informada[..TamanhoMaximoDaAssinatura];

        var base_ = string.Join('\n', trace.Split('\n').Take(6));
        return "t" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(base_)))[..15];
    }

    /// <summary>O tipo da exceção: a primeira linha do trace até os dois-pontos.</summary>
    public static string Tipo(string trace)
    {
        var primeira = trace.Split('\n', 2)[0].Trim();
        var doisPontos = primeira.IndexOf(": ", StringComparison.Ordinal);
        var tipo = doisPontos > 0 ? primeira[..doisPontos] : primeira;
        tipo = tipo.Length == 0 ? "Exceção sem trace" : tipo;
        return tipo.Length <= ValidadorDeLote.TamanhoMaximoDoTexto ? tipo : tipo[..ValidadorDeLote.TamanhoMaximoDoTexto];
    }

    /// <summary>O problema da reação pedida, ou vazio quando ela pode seguir.</summary>
    public static string Validar(AlterarEstadoDoGrupo? pedido)
    {
        if (pedido is null || !EstadosDoGrupo.Escolhiveis.Contains(pedido.Estado ?? string.Empty))
            return $"Estado inválido: use {string.Join(", ", EstadosDoGrupo.Escolhiveis)}.";
        if ((pedido.Versao ?? string.Empty).Trim().Length > TamanhoMaximoDaVersao)
            return $"A versão tem até {TamanhoMaximoDaVersao} caracteres.";
        return string.Empty;
    }

    /// <summary>
    /// O estado do grupo quando chega uma ocorrência nova: o grupo RESOLVIDO que volta numa versão
    /// igual ou maior que a da correção (ou resolvido sem versão) REGREDIU; os outros ficam como estão
    /// — o ignorado continua ignorado, e é para isso que ele existe.
    /// </summary>
    public static string EstadoAoReceber(string estado, string? resolvidoNaVersao, string versaoDaOcorrencia)
    {
        if (estado != EstadosDoGrupo.Resolvido)
            return estado;
        if (string.IsNullOrWhiteSpace(resolvidoNaVersao))
            return EstadosDoGrupo.Regrediu;
        return CompararVersoes(versaoDaOcorrencia, resolvidoNaVersao) >= 0 ? EstadosDoGrupo.Regrediu : estado;
    }

    /// <summary>
    /// Compara duas versões do dev.kit (<c>1.4.0</c>, <c>v1.10.2-beta</c>): numérica por parte, sem o
    /// prefixo <c>v</c> e sem o sufixo; o que não é versão compara como texto.
    /// </summary>
    public static int CompararVersoes(string a, string b)
    {
        static Version? Ler(string texto)
        {
            var limpo = (texto ?? string.Empty).Trim().TrimStart('v', 'V').Split('-', '+', ' ')[0];
            return Version.TryParse(limpo.Contains('.', StringComparison.Ordinal) ? limpo : limpo + ".0", out var versao) ? versao : null;
        }

        return Ler(a) is { } va && Ler(b) is { } vb ? va.CompareTo(vb) : string.CompareOrdinal(a, b);
    }

    [GeneratedRegex(@"\b(?:https?|ftp|ssh|git|file)://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"\b[\w.\-]+@[\w.\-]+:[^\s""'<>]+")]
    private static partial Regex RemotoSsh();

    [GeneratedRegex(@"(?<![\w.<>])/[A-Za-z]/[^\s:""'<>|]*")]
    private static partial Regex CaminhoGitBash();

    [GeneratedRegex(@"\b[A-Za-z][\w.\-]*\\[A-Za-z][\w.$\-]*")]
    private static partial Regex UsuarioDeDominio();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    [GeneratedRegex(@"\\\\[^\s\\""'<>|]+[^\n""'<>|]*?(?=:line\b|:\d|[""'<>|\n]|$)", RegexOptions.Multiline)]
    private static partial Regex CaminhoUnc();

    [GeneratedRegex(@"\b[A-Za-z]:[\\/][^\n""'<>|]*?(?=:line\b|:\d|[""'<>|\n]|$)", RegexOptions.Multiline)]
    private static partial Regex CaminhoWindows();

    [GeneratedRegex(@"(?<![\w.<>])/(?:home|Users|usr|var|tmp|opt|mnt|root|etc|srv|private|Volumes)/[^\s:""'<>|]*")]
    private static partial Regex CaminhoUnix();

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}\b")]
    private static partial Regex Guid();
}

/// <summary>
/// O ESCOPO de quem consulta o painel (US #381) — o filtro ÚNICO de isolamento entre empresas. Toda
/// consulta do painel o recebe e o aplica num ponto só (<c>ConsultasDoPainel</c>), em vez de cada
/// rota repetir a regra por papel: o admin vê tudo; o gestor, só as máquinas da empresa dele — e,
/// delas, o dado INDIVIDUAL só das que consentiram.
/// </summary>
/// <param name="EmpresaId">A empresa do gestor; nula é o admin (todas as máquinas).</param>
public sealed record EscopoDoPainel(int? EmpresaId)
{
    /// <summary>O escopo do admin.</summary>
    public static EscopoDoPainel Tudo { get; } = new((int?)null);

    /// <summary>O admin vê tudo, inclusive as máquinas anônimas.</summary>
    public bool EhAdmin => EmpresaId is null;

    /// <summary>
    /// O escopo das claims do token: <c>admin</c> é tudo; <c>gestor</c> com a empresa é ela. Qualquer
    /// outra combinação (gestor sem empresa, o <c>dev</c> — que entra no app, mas não no painel —, papel
    /// desconhecido) é NULO — a rota responde 403, e um token malformado nunca vira "ver tudo".
    /// </summary>
    public static EscopoDoPainel? DasClaims(string? papel, string? empresa)
        => papel switch
        {
            Papeis.Admin => Tudo,
            Papeis.Gestor when int.TryParse(empresa, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0
                => new EscopoDoPainel(id),
            _ => null,
        };
}

/// <summary>A validação de uma empresa nova e do convite de gestor, antes do banco.</summary>
public static class ValidadorDeEmpresa
{
    public const int TamanhoMaximoDoNome = 100;
    public const int TamanhoMaximoDoPlano = 50;
    public const int TamanhoMaximoDoLogin = 100;
    public const int TamanhoMaximoDoColaborador = 100;
    public const int AssentosMaximos = 10_000;

    /// <summary>Os erros por campo; vazio quando a empresa pode ser criada.</summary>
    public static Dictionary<string, string[]> Validar(EmpresaNova? empresa)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (empresa is null)
        {
            erros["empresa"] = ["O corpo do pedido é obrigatório."];
            return erros;
        }

        var nome = (empresa.Nome ?? string.Empty).Trim();
        if (nome.Length is 0 or > TamanhoMaximoDoNome)
            erros["nome"] = [$"Informe o nome da empresa (até {TamanhoMaximoDoNome} caracteres)."];
        if ((empresa.Plano ?? string.Empty).Trim().Length is 0 or > TamanhoMaximoDoPlano)
            erros["plano"] = [$"Informe o plano (até {TamanhoMaximoDoPlano} caracteres)."];
        if (empresa.Assentos is < 1 or > AssentosMaximos)
            erros["assentos"] = [$"Os assentos vão de 1 a {AssentosMaximos}."];
        return erros;
    }

    /// <summary>O problema do login do gestor, ou vazio — a mesma regra de todo usuário (<see cref="ValidadorDeUsuario"/>).</summary>
    public static string ValidarLogin(string? login) => ValidadorDeUsuario.ValidarLogin(login, "gestor");

    /// <summary>O nome do colaborador como o banco o guarda.</summary>
    public static string Colaborador(string? nome)
    {
        var limpo = (nome ?? string.Empty).Trim();
        return limpo.Length <= TamanhoMaximoDoColaborador ? limpo : limpo[..TamanhoMaximoDoColaborador];
    }
}

/// <summary>O código de adesão de uma empresa: curto para ditar, sem letras que se confundem.</summary>
public static class CodigoDeAdesao
{
    private const string Alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Um código novo, como <c>DK-7QH4-M2XA</c>.</summary>
    public static string Gerar()
    {
        var bytes = RandomNumberGenerator.GetBytes(8);
        var letras = bytes.Select(b => Alfabeto[b % Alfabeto.Length]).ToArray();
        return $"DK-{new string(letras, 0, 4)}-{new string(letras, 4, 4)}";
    }

    /// <summary>O código como se compara: sem espaços e em maiúsculas (o colaborador digita de qualquer jeito).</summary>
    public static string Normalizar(string? codigo) => (codigo ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>
/// A exportação em CSV (US #381): separador ponto e vírgula (o Excel em pt-BR abre direto), aspas
/// quando preciso e a célula que começa com <c>= + - @</c> neutralizada com apóstrofo — o nome do
/// colaborador é texto dele, e uma planilha não pode executar fórmula vinda dali.
/// </summary>
public static class ExportacaoCsv
{
    public static readonly string[] Cabecalho =
    [
        "colaborador", "maquina", "empresa", "dia", "sessoes", "turnos", "turnos_com_falha", "ferramentas",
        "comandos_delegados", "arquivos_alterados", "objetivos_cumpridos", "objetivos_recusados",
    ];

    public static string Gerar(IEnumerable<LinhaExportada> linhas)
    {
        var texto = new StringBuilder();
        texto.Append(string.Join(';', Cabecalho)).Append("\r\n");
        foreach (var l in linhas)
        {
            string[] celulas =
            [
                Celula(l.Colaborador), Celula(l.Apelido), Celula(l.Empresa), l.Dia.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Numero(l.Sessoes), Numero(l.Turnos), Numero(l.TurnosComFalha), Numero(l.Ferramentas),
                Numero(l.ComandosDelegados), Numero(l.ArquivosAlterados), Numero(l.ObjetivosCumpridos), Numero(l.ObjetivosRecusados),
            ];
            texto.Append(string.Join(';', celulas)).Append("\r\n");
        }

        return texto.ToString();
    }

    private static string Numero(long valor) => valor.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A célula de texto: neutraliza fórmula e põe aspas quando há separador, aspas ou quebra.</summary>
    public static string Celula(string? valor)
    {
        var texto = valor ?? string.Empty;
        if (texto.Length > 0 && "=+-@\t\r".Contains(texto[0], StringComparison.Ordinal))
            texto = "'" + texto;
        return texto.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{texto.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : texto;
    }
}

/// <summary>Uma soma de um tipo (e recorte) no período — o que as consultas tiram do banco.</summary>
public sealed record SomaPorTipo(string Tipo, string Detalhe, long Quantidade, long Eventos, long Valor);

/// <summary>
/// A QUALIDADE de uso a partir das somas — a mesma definição do <c>devcli telemetria</c> do dev.kit.
/// Toda razão sem denominador é NULA (a tela mostra um traço). Os cartões Impasses e Árbitro (US #405)
/// saem das MESMAS somas (tipo e recorte, já no escopo e no período): nenhuma consulta a mais.
/// </summary>
public static class CalculoDeQualidade
{
    public static QualidadeResposta Calcular(Periodo periodo, IReadOnlyCollection<SomaPorTipo> somas)
    {
        long Eventos(string tipo) => somas.Where(s => s.Tipo == tipo).Sum(s => s.Eventos);
        long Valor(string tipo) => somas.Where(s => s.Tipo == tipo).Sum(s => s.Valor);

        var turnos = Eventos(TiposDeEvento.TurnoExecutado);
        var falhas = Eventos(TiposDeEvento.TurnoFalhou);
        var avaliacoes = Eventos(TiposDeEvento.ObjetivoAvaliado);
        var cumpridos = Eventos(TiposDeEvento.ObjetivoCumprido);
        var recusados = Eventos(TiposDeEvento.ObjetivoRecusado);

        var porCausa = somas
            .Where(s => s.Tipo == TiposDeEvento.TurnoFalhou)
            .GroupBy(s => s.Detalhe.Length == 0 ? "nao-classificada" : s.Detalhe)
            .Select(g => new CausaDeFalha(g.Key, g.Sum(s => s.Eventos)))
            .OrderByDescending(c => c.Quantidade).ThenBy(c => c.Causa, StringComparer.Ordinal)
            .ToArray();

        var detectados = Eventos(TiposDeEvento.ImpasseDetectado);
        var destravados = Eventos(TiposDeEvento.ImpasseResolvido);
        var impasses = new ImpassesResumo(
            detectados, PorRecorte(somas.Where(s => s.Tipo == TiposDeEvento.ImpasseDetectado).Select(s => (s.Detalhe, s.Eventos))),
            Razao(Valor(TiposDeEvento.ImpasseDetectado), detectados),
            destravados, Razao(Valor(TiposDeEvento.ImpasseResolvido), destravados),
            PorRecorte(somas.Where(s => s.Tipo == TiposDeEvento.ImpasseResolvido).Select(s => (s.Detalhe, s.Eventos))));

        return new QualidadeResposta(
            periodo.De, periodo.Ate, turnos, falhas,
            Razao(falhas, turnos), Razao(Valor(TiposDeEvento.TurnoExecutado), turnos),
            porCausa, avaliacoes, Razao(Valor(TiposDeEvento.ObjetivoAvaliado), avaliacoes),
            cumpridos, recusados, Razao(cumpridos, recusados), Razao(turnos, cumpridos),
            impasses, Arbitro(somas));
    }

    /// <summary>
    /// O cartão Árbitro: as ações pelo recorte <c>acao|seção</c> (<see cref="RegrasDoArbitro.Separar"/>), as
    /// cobranças por regra, a taxa de correção (corrigidas sobre cobranças, nula sem cobrança) e as escaladas.
    /// </summary>
    private static ArbitroResumo Arbitro(IReadOnlyCollection<SomaPorTipo> somas)
    {
        var acoes = somas
            .Where(s => s.Tipo == TiposDeEvento.ArbitroAgiu)
            .Select(s => (Partes: RegrasDoArbitro.Separar(s.Detalhe), s.Eventos))
            .ToList();
        long DaAcao(string acao) => acoes.Where(a => a.Partes.Acao == acao).Sum(a => a.Eventos);

        var cobrancas = DaAcao(AcoesDoArbitro.Cobrou);
        var corrigidas = DaAcao(AcoesDoArbitro.Corrigido);
        return new ArbitroResumo(
            acoes.Sum(a => a.Eventos), cobrancas,
            PorRecorte(acoes.Where(a => a.Partes.Acao == AcoesDoArbitro.Cobrou).Select(a => (a.Partes.Secao, a.Eventos))),
            corrigidas, Razao(corrigidas, cobrancas), DaAcao(AcoesDoArbitro.EscalouAoDev),
            PorRecorte(acoes.Select(a => (a.Partes.Acao, a.Eventos))));
    }

    /// <summary>As contagens por recorte, da mais comum à mais rara (o empate, em ordem alfabética).</summary>
    private static ContagemPorRecorte[] PorRecorte(IEnumerable<(string Recorte, long Eventos)> itens)
        => itens
            .GroupBy(i => i.Recorte, StringComparer.Ordinal)
            .Select(g => new ContagemPorRecorte(g.Key, g.Sum(i => i.Eventos)))
            .OrderByDescending(c => c.Quantidade).ThenBy(c => c.Recorte, StringComparer.Ordinal)
            .ToArray();

    /// <summary>A razão, ou nula sem denominador.</summary>
    public static double? Razao(double parte, long todo) => todo == 0 ? null : Math.Round(parte / todo, 4);
}

/// <summary>O recorte do <see cref="TiposDeEvento.ArbitroAgiu"/> (US #405): <c>acao</c> ou <c>acao|seção da regra</c>.</summary>
public static class RegrasDoArbitro
{
    /// <summary>A ação e a seção, separadas pelo PRIMEIRO <c>|</c> (a seção pode ter outros); sem ele, a seção é vazia.</summary>
    public static (string Acao, string Secao) Separar(string? detalhe)
    {
        var texto = detalhe ?? string.Empty;
        var corte = texto.IndexOf(AcoesDoArbitro.Separador, StringComparison.Ordinal);
        return corte < 0 ? (texto.Trim(), string.Empty) : (texto[..corte].Trim(), texto[(corte + 1)..].Trim());
    }
}
