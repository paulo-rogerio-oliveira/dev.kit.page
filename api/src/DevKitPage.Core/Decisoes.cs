using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

public interface IDecisoesRemotas
{
    Task<IReadOnlyList<DecisaoPendenteV1>?> PendentesAsync(int usuario, string maquina, CancellationToken ct);
    Task<ResultadoDaDecisao<RespostaDaDecisaoV1>> ResponderAsync(int usuario, string maquina, string pendencia, ResponderDecisaoV1 pedido, CancellationToken ct);
    Task<IReadOnlyList<RespostaDaDecisaoV1>> RespostasAsync(int usuario, string maquina, CancellationToken ct);
    Task<ResultadoDaDecisao<RespostaDaDecisaoV1>> ReceberAsync(int usuario, string maquina, long id, CancellationToken ct);
}
public interface ISegundoFator
{
    Task<bool> AtivoAsync(int usuario, CancellationToken ct);
    Task<ResultadoDaDecisao<AutenticadorPreparadoV1>> PrepararAsync(int usuario, string senha, CancellationToken ct);
    Task<string?> VerificarAsync(int usuario, string? codigo, bool ativar, CancellationToken ct);
}
public sealed record ResultadoDaDecisao<T>(T? Valor, string? Erro = null, int Status = 409) where T : class;

public sealed class AutenticadorDoUsuario
{
    public int UsuarioId { get; set; }
    public string SegredoProtegido { get; set; } = string.Empty;
    public bool Ativo { get; set; }
    public DateTime PreparadoEmUtc { get; set; }
    public long UltimoPasso { get; set; } = -1;
    public int Tentativas { get; set; }
    public DateTime JanelaEmUtc { get; set; }
}
/// <summary>Outbox e auditoria imutável da autorização. Recebida não significa executada pela máquina.</summary>
public sealed class RespostaDaDecisao
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public string MaquinaId { get; set; } = string.Empty;
    public string PendenciaId { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
    public string Acao { get; set; } = string.Empty;
    public string? Opcao { get; set; }
    public string? Origem { get; set; }
    public string? Destino { get; set; }
    public string? Sha { get; set; }
    public DateTime EmUtc { get; set; }
    public bool SegundoFator { get; set; }
    public string Estado { get; set; } = "registrada";
}

public static class RegrasDaDecisao
{
    public static string SnapshotId(PendenciaNoRetratoV1 pendencia)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(pendencia)));

    public static string? Bloqueio(PendenciaNoRetratoV1 p)
    {
        if (p.Tipo != RemotoV1.PendenciaAprovacao) return null;
        var s = p.Snapshot;
        if (s is null) return "A máquina ainda não enviou as evidências deste snapshot.";
        if (Validar(s) is { } erro) return erro;
        if (s.Notas.Count == 0 || s.Notas.Any(n => n.Nota < n.Minima)) return "Nota insuficiente: aguarde todos os avaliadores.";
        if (!s.RegressaoConcluida || s.TestesFalhos != 0) return "Regressão pendente ou com falhas: a aprovação está bloqueada.";
        return null;
    }

    public static string? Validar(SnapshotDaDecisaoV1? s)
    {
        if (s is null) return null;
        if (string.IsNullOrWhiteSpace(s.Origem) || string.IsNullOrWhiteSpace(s.Destino) || s.Origem.Length > 300 || s.Destino.Length > 300
            || s.Sha is null || s.Sha.Length != 40 || !s.Sha.All(Uri.IsHexDigit)) return "Snapshot: informe origem, destino e SHA de 40 dígitos hexadecimais.";
        if (s.Notas is null || s.Notas.Count > 20 || s.Notas.Any(n => n is null || string.IsNullOrWhiteSpace(n.Avaliador) || n.Avaliador.Length > 200 || n.Nota is < 0 or > 100 || n.Minima is < 1 or > 100)) return "Snapshot: notas inválidas.";
        if (s.TestesAprovados < 0 || s.TestesFalhos < 0 || s.Arquivos < 0 || s.Commits < 0 || s.Alteracoes is null || s.Alteracoes.Length > 16000) return "Snapshot: contagens ou alterações inválidas.";
        if (s.Capturas is null || s.Capturas.Count > 20 || s.Capturas.Any(c => c is null || string.IsNullOrWhiteSpace(c.Titulo) || c.Titulo.Length > 200 || !UrlSegura(c.Url))) return "Snapshot: capturas precisam de título e URL HTTPS ou caminho da API de design.";
        return null;
    }

    public static bool UrlSegura(string? url)
        => url is { Length: <= 2000 } && (url.StartsWith("/api/design/", StringComparison.Ordinal) && !url.Contains('\\') && !url.Contains("..", StringComparison.Ordinal)
            || Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.UserInfo.Length == 0);
}

/// <summary>TOTP RFC 6238: SHA-1, seis dígitos, 30 s e tolerância de um passo. O serviço impede reuso.</summary>
public static class Totp
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public static string NovaChave() => Base32(RandomNumberGenerator.GetBytes(20));
    public static string Base32(byte[] bytes)
    {
        var texto = new StringBuilder();
        var acumulado = 0; var bits = 0;
        foreach (var b in bytes)
        {
            acumulado = (acumulado << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; texto.Append(Alfabeto[(acumulado >> bits) & 31]); }
        }
        if (bits > 0) texto.Append(Alfabeto[(acumulado << (5 - bits)) & 31]);
        return texto.ToString();
    }
    public static string Codigo(string chave, long passo)
    {
        var bytes = new List<byte>(); var acumulado = 0; var bits = 0;
        foreach (var c in chave)
        {
            var valor = Alfabeto.IndexOf(c);
            if (valor < 0) throw new ArgumentException("Chave Base32 inválida.", nameof(chave));
            acumulado = (acumulado << 5) | valor; bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(acumulado >> bits)); }
        }
        Span<byte> contador = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(contador, passo);
        var hash = HMACSHA1.HashData(bytes.ToArray(), contador);
        var offset = hash[^1] & 15;
        var valorFinal = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
        return (valorFinal % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }
    public static long? Conferir(string chave, string? codigo, DateTimeOffset agora, long ultimo)
    {
        if (codigo is null || codigo.Length != 6 || !codigo.All(char.IsAsciiDigit)) return null;
        var passo = agora.ToUnixTimeSeconds() / 30;
        foreach (var n in new[] { passo, passo - 1, passo + 1 })
            if (n > ultimo && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Codigo(chave, n)), Encoding.ASCII.GetBytes(codigo))) return n;
        return null;
    }
}
