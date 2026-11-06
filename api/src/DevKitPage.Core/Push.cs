using System.Security.Cryptography;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

public interface IPushDoBoard
{
    ConfiguracaoDePushV1 Configuracao();
    Task<ResultadoDaDecisao<AssinaturaRegistradaV1>> AssinarAsync(int usuario, AssinaturaDePushV1 pedido, CancellationToken ct);
    Task<bool> AssinadaAsync(int usuario, string id, CancellationToken ct);
    Task CancelarAsync(int usuario, string id, CancellationToken ct);
    Task<DestinoDoPushV1?> DestinoAsync(int usuario, string entrega, CancellationToken ct);
}

public interface IProcessamentoDePush
{
    Task<int> ProcessarAsync(CancellationToken ct);
}

public enum ResultadoDoEnvioDePush { Aceito, AssinaturaInvalida, Repetir }
public interface ITransporteDePush
{
    Task<ResultadoDoEnvioDePush> EnviarAsync(AssinaturaDePushV1 assinatura, string entrega, CancellationToken ct);
}

public sealed class OpcoesDoPush
{
    public const string Secao = "Push";
    public string ChavePublica { get; set; } = "";
    public string ChavePrivada { get; set; } = "";
    public string Assunto { get; set; } = "";
    public bool Disponivel => RegrasDoPush.ChavePublicaValida(ChavePublica) && RegrasDoPush.Bytes(ChavePrivada)?.Length == 32
        && Uri.TryCreate(Assunto, UriKind.Absolute, out var contato) && (contato.Scheme == "https" || contato.Scheme == "mailto");
}

public static class RegrasDoPush
{
    public const int LimiteDeDispositivos = 8;
    public const int RetencaoDias = 30;
    public const int TtlSegundos = 300;
    public static bool IdValido(string? id) => id?.Length == 32 && Guid.TryParseExact(id, "N", out _);

    // Só serviços de push conhecidos: a assinatura não pode transformar a API num cliente de URLs arbitrárias.
    public static bool EndpointValido(string? texto)
    {
        if (texto is null || texto.Length > 2048 || !Uri.TryCreate(texto, UriKind.Absolute, out var u)
            || u.Scheme != "https" || u.Port != 443 || u.UserInfo.Length > 0 || u.Fragment.Length > 0) return false;
        return u.Host is "fcm.googleapis.com" or "updates.push.services.mozilla.com" or "web.push.apple.com"
            || u.Host.EndsWith(".notify.windows.com", StringComparison.OrdinalIgnoreCase);
    }

    public static string? Validar(AssinaturaDePushV1? p)
        => p is null || !EndpointValido(p.Endpoint) ? "Serviço de push inválido ou não suportado."
            : p.Keys is null || !ChavePublicaValida(p.Keys.P256dh) || Bytes(p.Keys.Auth)?.Length != 16 ? "Chaves da assinatura inválidas." : null;

    public static byte[]? Bytes(string? texto)
    {
        if (string.IsNullOrEmpty(texto) || texto.Length > 128 || texto.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_'))) return null;
        try { return Convert.FromBase64String(texto.Replace('-', '+').Replace('_', '/').PadRight((texto.Length + 3) / 4 * 4, '=')); }
        catch (FormatException) { return null; }
    }

    public static bool ChavePublicaValida(string? texto)
    {
        var bytes = Bytes(texto);
        if (bytes is not { Length: 65 } || bytes[0] != 4) return false;
        try
        {
            using var chave = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = bytes[1..33], Y = bytes[33..65] } });
            return true;
        }
        catch (CryptographicException) { return false; }
    }
}

public sealed class AssinaturaDoPush
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int UsuarioId { get; set; }
    public string EndpointHash { get; set; } = "";
    public string DadosProtegidos { get; set; } = "";
    public string ChavePublicaServidor { get; set; } = "";
    public DateTime AtualizadaEmUtc { get; set; }
    public DateTime VerificadaEmUtc { get; set; }
}

public sealed class EntregaDoPush
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AssinaturaId { get; set; } = "";
    public string MaquinaId { get; set; } = "";
    public string PendenciaId { get; set; } = "";
    public string SnapshotId { get; set; } = "";
    public string Estado { get; set; } = "pendente";
    public string Reserva { get; set; } = "";
    public int Tentativas { get; set; }
    public DateTime CriadaEmUtc { get; set; }
    public DateTime ExpiraEmUtc { get; set; }
    public DateTime ProximaTentativaUtc { get; set; }
}
