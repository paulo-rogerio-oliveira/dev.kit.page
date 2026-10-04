using System.Security.Cryptography;
using System.Text;
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
