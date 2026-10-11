using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O que uma licença assinada diz (US #425) — o payload do <c>DKL2</c>, em JSON camelCase. É o MESMO JSON que o
/// <c>LicencaAssinada</c> do git.kit lê: um campo novo é opcional, e mudar um nome quebra as máquinas instaladas.
/// </summary>
/// <param name="Versao">A versão do formato (2).</param>
/// <param name="Tipo">Um de <see cref="TiposDeLicenca"/>.</param>
/// <param name="Maquina">O id anônimo da máquina a que a licença foi emitida.</param>
/// <param name="Usuario">O login de quem renovou; nulo no trial.</param>
public sealed record ConteudoDaLicenca(int Versao, string Tipo, string Maquina, string? Usuario, DateTimeOffset EmitidaEm, DateTimeOffset ValidaAte);

/// <summary>
/// A licença assinada do dev.kit (US #425): <c>DKL2.&lt;payload&gt;.&lt;assinatura&gt;</c>, os dois em base64url, a
/// assinatura Ed25519 sobre os BYTES do payload. Substitui o <c>DKL1</c> (AES-GCM com chave simétrica no binário,
/// que só impedia a edição casual): a chave privada fica no Key Vault, e o app só tem a pública.
/// </summary>
public static class LicencaAssinada
{
    /// <summary>O prefixo do formato.</summary>
    public const string Prefixo = "DKL2";

    /// <summary>A versão do payload.</summary>
    public const int VersaoDoFormato = 2;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>A licença assinada com a <paramref name="semente"/> (a chave privada Ed25519).</summary>
    public static string Emitir(ConteudoDaLicenca conteudo, ReadOnlySpan<byte> semente)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(conteudo, Json);
        return $"{Prefixo}.{Base64Url(payload)}.{Base64Url(Ed25519.Assinar(semente, payload))}";
    }

    /// <summary>O conteúdo da licença, se a assinatura confere com a <paramref name="chavePublica"/>; nulo em qualquer outro caso.</summary>
    public static ConteudoDaLicenca? Ler(string? texto, ReadOnlySpan<byte> chavePublica)
    {
        var partes = (texto ?? string.Empty).Trim().Split('.');
        if (partes.Length != 3 || partes[0] != Prefixo)
            return null;
        try
        {
            var payload = DeBase64Url(partes[1]);
            if (!Ed25519.Verificar(chavePublica, payload, DeBase64Url(partes[2])))
                return null;
            var conteudo = JsonSerializer.Deserialize<ConteudoDaLicenca>(payload, Json);
            return conteudo is { Versao: VersaoDoFormato } ? conteudo : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] DeBase64Url(string texto)
    {
        var base64 = texto.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}

/// <summary>Por que a renovação foi recusada — a API traduz em status HTTP e mensagem própria.</summary>
public enum RecusaDaRenovacao
{
    Nenhuma,

    /// <summary>O usuário foi bloqueado pelo gestor ou pelo admin (403).</summary>
    UsuarioBloqueado,

    /// <summary>O usuário não é de nenhuma empresa — quem renova é quem assina (403).</summary>
    SemEmpresa,

    /// <summary>A assinatura da empresa foi cancelada no gateway (402).</summary>
    AssinaturaCancelada,

    /// <summary>A licença atual ainda não chegou ao dia da renovação (409).</summary>
    CedoDemais,

    /// <summary>A empresa do dev não tem assento para ele (403).</summary>
    SemAssento,
}

/// <summary>As regras puras da licença assinada (US #425, fase 4): o trial, a validade e a janela de renovação.</summary>
public static partial class RegrasDeLicenca
{
    /// <summary>A licença atual pode ser renovada? Só a partir do <paramref name="aPartirDoDia"/>º dia da emissão.</summary>
    public static bool NaJanela(DateTimeOffset emitidaEm, DateTimeOffset agora, int aPartirDoDia)
        => agora >= emitidaEm.AddDays(Math.Max(0, aPartirDoDia));

    /// <summary>
    /// A recusa da renovação, ou <see cref="RecusaDaRenovacao.Nenhuma"/>. A <paramref name="atual"/> é a licença que a
    /// máquina apresentou (já conferida): a de assinatura fora da janela é cedo demais; o trial renova a qualquer hora
    /// (é a primeira licença de quem acabou de entrar).
    /// </summary>
    public static RecusaDaRenovacao Avaliar(Usuario usuario, Empresa? empresa, ConteudoDaLicenca? atual, DateTimeOffset agora, int aPartirDoDia)
    {
        if (usuario.BloqueadoPeloAdmin)
            return RecusaDaRenovacao.UsuarioBloqueado;
        if (empresa is null)
            return RecusaDaRenovacao.SemEmpresa;
        if (empresa.AssinaturaCanceladaEmUtc is not null)
            return RecusaDaRenovacao.AssinaturaCancelada;
        if (atual is { Tipo: TiposDeLicenca.Assinatura } && !NaJanela(atual.EmitidaEm, agora, aPartirDoDia))
            return RecusaDaRenovacao.CedoDemais;
        return RecusaDaRenovacao.Nenhuma;
    }

    /// <summary>A impressão da máquina como o banco a guarda: hex minúsculo de até 64 caracteres; nula quando não é uma.</summary>
    public static string? Impressao(string? texto)
    {
        var limpo = (texto ?? string.Empty).Trim().ToLowerInvariant();
        return Hex().IsMatch(limpo) ? limpo : null;
    }

    [GeneratedRegex("^[0-9a-f]{16,64}$")]
    private static partial Regex Hex();
}

/// <summary>
/// As regras da conta do dev (US #425, fase 3): a adesão por código e e-mail, o link de definir a senha e a
/// política de login por empresa.
/// </summary>
public static class RegrasDaConta
{
    /// <summary>O e-mail como se compara e se guarda: sem espaços e em minúsculas.</summary>
    public static string Email(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Os erros por campo da adesão; vazio quando ela pode seguir.</summary>
    public static Dictionary<string, string[]> Validar(AdesaoV1? adesao)
    {
        var erros = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (adesao is null)
        {
            erros["adesao"] = ["O corpo do pedido é obrigatório."];
            return erros;
        }

        if (CodigoDeAdesao.Normalizar(adesao.Codigo).Length == 0)
            erros["codigo"] = ["Informe o código de adesão que o gestor entregou."];
        var email = Email(adesao.Email);
        if (email.Length is 0 or > ValidadorDeDemonstracao.TamanhoMaximoDoEmail || !ValidadorDeDemonstracao.EmailValido(email))
            erros["email"] = ["Informe um e-mail válido."];
        if ((adesao.Nome ?? string.Empty).Trim().Length > ValidadorDeUsuario.TamanhoMaximoDoNome)
            erros["nome"] = [$"O nome tem até {ValidadorDeUsuario.TamanhoMaximoDoNome} caracteres."];
        return erros;
    }

    /// <summary>O token do link de definir a senha (vai no e-mail) e o hash dele (vai para o banco) — a mesma geração da chave da máquina.</summary>
    public static (string Token, string Hash) TokenDoLink() => ChaveDeMaquina.Gerar();

    /// <summary>O link do e-mail: a raiz do site (sem barra no fim) e o token na query.</summary>
    public static string Link(string urlDoSite, string token)
        => $"{(urlDoSite ?? string.Empty).TrimEnd('/')}/definir-senha?token={Uri.EscapeDataString(token)}";

    /// <summary>A política de login que vale: a da empresa, quando ela escolheu; senão o padrão global.</summary>
    public static bool LoginObrigatorio(bool? daEmpresa, bool global) => daEmpresa ?? global;
}

/// <summary>
/// A verificação do aviso do gateway de pagamento (US #425, fase 5): o cabeçalho <c>X-Gateway-Assinatura</c> é
/// <c>t=&lt;unix&gt;,v1=&lt;hmac&gt;</c>, o HMAC-SHA256 do segredo sobre <c>"&lt;t&gt;.&lt;corpo&gt;"</c> — o esquema do Stripe, que
/// amarra o corpo ao instante e impede repetir um aviso velho.
/// </summary>
public static class RegrasDoGateway
{
    /// <summary>O cabeçalho da assinatura.</summary>
    public const string Cabecalho = "X-Gateway-Assinatura";

    /// <summary>O cabeçalho de um corpo assinado com o segredo, no instante dado (o que o gateway — e o teste — enviam).</summary>
    public static string Assinar(string segredo, string corpo, DateTimeOffset em)
    {
        var t = em.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return $"t={t},v1={Hmac(segredo, t, corpo)}";
    }

    /// <summary>A assinatura confere com o segredo e o carimbo está dentro da tolerância? Sem segredo, nada confere.</summary>
    public static bool Valida(string? cabecalho, string segredo, string corpo, DateTimeOffset agora, int toleranciaSegundos)
    {
        if (string.IsNullOrEmpty(segredo) || string.IsNullOrWhiteSpace(cabecalho))
            return false;

        string? t = null, v1 = null;
        foreach (var parte in cabecalho.Split(','))
        {
            var par = parte.Trim().Split('=', 2);
            if (par.Length != 2)
                continue;
            if (par[0] == "t")
                t = par[1];
            else if (par[0] == "v1")
                v1 = par[1];
        }

        if (t is null || v1 is null || !long.TryParse(t, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var segundos))
            return false;
        if (Math.Abs(agora.ToUnixTimeSeconds() - segundos) > Math.Max(1, toleranciaSegundos))
            return false;
        return ChaveDeMaquina.Iguais(Hmac(segredo, t, corpo), v1.ToLowerInvariant());
    }

    private static string Hmac(string segredo, string t, string corpo)
        => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes($"{t}.{corpo}")));
}
