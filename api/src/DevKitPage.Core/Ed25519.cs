using System.Numerics;
using System.Security.Cryptography;

namespace DevKitPage.Core;

/// <summary>
/// A assinatura Ed25519 do RFC 8032 (seção 5.1), em C# puro: o .NET 10 não a expõe sozinha (só dentro do
/// ML-DSA composto), e um pacote de terceiros com binário nativo (libsodium) seria mais uma dependência a
/// publicar no Azure. É a referência do RFC (seção 6) com <see cref="BigInteger"/> — lenta para um servidor
/// de alto volume, e mais que suficiente para assinar uma licença por renovação. A mesma implementação está
/// no git.kit (<c>GitKit.Core</c>), que só VERIFICA: os dois lados conferem os vetores de teste do RFC.
/// </summary>
public static class Ed25519
{
    /// <summary>O tamanho da semente (a chave privada) e da chave pública.</summary>
    public const int TamanhoDaChave = 32;

    /// <summary>O tamanho da assinatura.</summary>
    public const int TamanhoDaAssinatura = 64;

    private static readonly BigInteger P = BigInteger.Pow(2, 255) - 19;
    private static readonly BigInteger Q = BigInteger.Pow(2, 252) + BigInteger.Parse("27742317777372353535851937790883648493", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly BigInteger D = Mod(-121665 * Inverso(121666));
    private static readonly BigInteger RaizDeMenosUm = BigInteger.ModPow(2, (P - 1) / 4, P);
    private static readonly Ponto Base = BasePadrao();

    /// <summary>A chave pública da <paramref name="semente"/> (32 bytes).</summary>
    public static byte[] ChavePublica(ReadOnlySpan<byte> semente)
    {
        var (a, _) = Expandir(semente);
        return Comprimir(Multiplicar(a, Base));
    }

    /// <summary>Assina a <paramref name="mensagem"/> com a <paramref name="semente"/>: 64 bytes (R || S).</summary>
    public static byte[] Assinar(ReadOnlySpan<byte> semente, ReadOnlySpan<byte> mensagem)
    {
        var (a, prefixo) = Expandir(semente);
        var publica = Comprimir(Multiplicar(a, Base));
        var r = HashModQ(prefixo, mensagem.ToArray());
        var rComprimido = Comprimir(Multiplicar(r, Base));
        var h = HashModQ(rComprimido, publica, mensagem.ToArray());
        var s = Mod(r + h * a, Q);
        return [.. rComprimido, .. Bytes(s)];
    }

    /// <summary>A assinatura confere com a chave pública e a mensagem? Falso para qualquer entrada malformada.</summary>
    public static bool Verificar(ReadOnlySpan<byte> chavePublica, ReadOnlySpan<byte> mensagem, ReadOnlySpan<byte> assinatura)
    {
        if (chavePublica.Length != TamanhoDaChave || assinatura.Length != TamanhoDaAssinatura)
            return false;
        if (Descomprimir(chavePublica) is not { } a)
            return false;
        var rComprimido = assinatura[..32].ToArray();
        if (Descomprimir(rComprimido) is not { } r)
            return false;
        var s = new BigInteger(assinatura[32..], isUnsigned: true, isBigEndian: false);
        if (s >= Q)
            return false;

        var h = HashModQ(rComprimido, chavePublica.ToArray(), mensagem.ToArray());
        return Iguais(Multiplicar(s, Base), Somar(r, Multiplicar(h, a)));
    }

    private readonly record struct Ponto(BigInteger X, BigInteger Y, BigInteger Z, BigInteger T);

    private static BigInteger Mod(BigInteger x, BigInteger? m = null)
    {
        var modulo = m ?? P;
        var resto = x % modulo;
        return resto.Sign < 0 ? resto + modulo : resto;
    }

    private static BigInteger Inverso(BigInteger x) => BigInteger.ModPow(Mod(x), P - 2, P);

    private static Ponto Somar(Ponto p, Ponto q)
    {
        var a = Mod((p.Y - p.X) * (q.Y - q.X));
        var b = Mod((p.Y + p.X) * (q.Y + q.X));
        var c = Mod(2 * p.T * q.T * D);
        var d = Mod(2 * p.Z * q.Z);
        var (e, f, g, h) = (b - a, d - c, d + c, b + a);
        return new Ponto(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
    }

    private static Ponto Multiplicar(BigInteger escalar, Ponto ponto)
    {
        var resultado = new Ponto(0, 1, 1, 0);
        while (escalar > 0)
        {
            if (!escalar.IsEven)
                resultado = Somar(resultado, ponto);
            ponto = Somar(ponto, ponto);
            escalar >>= 1;
        }

        return resultado;
    }

    private static bool Iguais(Ponto p, Ponto q)
        => Mod(p.X * q.Z - q.X * p.Z).IsZero && Mod(p.Y * q.Z - q.Y * p.Z).IsZero;

    private static BigInteger? RecuperarX(BigInteger y, bool impar)
    {
        if (y >= P)
            return null;
        var x2 = Mod((y * y - 1) * Inverso(D * y * y + 1));
        if (x2.IsZero)
            return impar ? null : BigInteger.Zero;

        var x = BigInteger.ModPow(x2, (P + 3) / 8, P);
        if (!Mod(x * x - x2).IsZero)
            x = Mod(x * RaizDeMenosUm);
        if (!Mod(x * x - x2).IsZero)
            return null;
        if (!x.IsEven != impar)
            x = P - x;
        return x;
    }

    private static byte[] Comprimir(Ponto ponto)
    {
        var zInverso = Inverso(ponto.Z);
        var x = Mod(ponto.X * zInverso);
        var y = Mod(ponto.Y * zInverso);
        var bytes = Bytes(y);
        if (!x.IsEven)
            bytes[31] |= 0x80;
        return bytes;
    }

    private static Ponto? Descomprimir(ReadOnlySpan<byte> comprimido)
    {
        if (comprimido.Length != 32)
            return null;
        var copia = comprimido.ToArray();
        var impar = (copia[31] & 0x80) != 0;
        copia[31] &= 0x7F;
        var y = new BigInteger(copia, isUnsigned: true, isBigEndian: false);
        return RecuperarX(y, impar) is { } x ? new Ponto(x, y, 1, Mod(x * y)) : null;
    }

    private static (BigInteger A, byte[] Prefixo) Expandir(ReadOnlySpan<byte> semente)
    {
        if (semente.Length != TamanhoDaChave)
            throw new ArgumentException($"A semente Ed25519 tem {TamanhoDaChave} bytes.", nameof(semente));
        var h = SHA512.HashData(semente);
        var a = new BigInteger(h.AsSpan(0, 32), isUnsigned: true, isBigEndian: false);
        a &= (BigInteger.One << 254) - 8;
        a |= BigInteger.One << 254;
        return (a, h[32..]);
    }

    private static BigInteger HashModQ(params byte[][] partes)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
        foreach (var parte in partes)
            sha.AppendData(parte);
        return Mod(new BigInteger(sha.GetHashAndReset(), isUnsigned: true, isBigEndian: false), Q);
    }

    private static byte[] Bytes(BigInteger valor)
    {
        var bytes = new byte[32];
        valor.ToByteArray(isUnsigned: true, isBigEndian: false).CopyTo(bytes, 0);
        return bytes;
    }

    private static Ponto BasePadrao()
    {
        var y = Mod(4 * Inverso(5));
        var x = RecuperarX(y, impar: false)!.Value;
        return new Ponto(x, y, 1, Mod(x * y));
    }
}
