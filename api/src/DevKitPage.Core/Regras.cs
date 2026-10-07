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
        texto = Email().Replace(texto, "<email>");
        texto = CaminhoUnc().Replace(texto, "<caminho>");
        texto = CaminhoWindows().Replace(texto, "<caminho>");
        texto = CaminhoUnix().Replace(texto, "<caminho>");
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

    [GeneratedRegex(@"\b(?:https?|ftp)://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

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
    /// outra combinação (gestor sem empresa, papel desconhecido) é NULO — a rota responde 403, e um
    /// token malformado nunca vira "ver tudo".
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

    /// <summary>O problema do login do gestor, ou vazio.</summary>
    public static string ValidarLogin(string? login)
    {
        var limpo = (login ?? string.Empty).Trim();
        if (limpo.Length is 0 or > TamanhoMaximoDoLogin || limpo.Any(char.IsWhiteSpace))
            return $"Informe o login do gestor, sem espaços (até {TamanhoMaximoDoLogin} caracteres).";
        return string.Empty;
    }

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
/// Toda razão sem denominador é NULA (a tela mostra um traço).
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

        return new QualidadeResposta(
            periodo.De, periodo.Ate, turnos, falhas,
            Razao(falhas, turnos), Razao(Valor(TiposDeEvento.TurnoExecutado), turnos),
            porCausa, avaliacoes, Razao(Valor(TiposDeEvento.ObjetivoAvaliado), avaliacoes),
            cumpridos, recusados, Razao(cumpridos, recusados), Razao(turnos, cumpridos));
    }

    /// <summary>A razão, ou nula sem denominador.</summary>
    public static double? Razao(double parte, long todo) => todo == 0 ? null : Math.Round(parte / todo, 4);
}
