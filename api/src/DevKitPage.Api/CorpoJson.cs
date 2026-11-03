using System.Text;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace DevKitPage.Api;

/// <summary>
/// A leitura do corpo JSON com a recusa CERTA para cada caso, sem captura genérica de exceção: o tipo que não é JSON ou o
/// charset desconhecido é 415, o corpo acima do teto é 413, o JSON inválido ou vazio é 400. O resto (o stream que falha,
/// um defeito da API) segue para o <see cref="ErroUtilDaApi"/> como 500 — nunca vira 415, que diria ao dev.kit que o
/// formato dele é que está errado.
/// <para>
/// O charset é conferido ANTES da leitura (<c>ReadFromJsonAsync</c> lança a mesma <see cref="InvalidOperationException"/>
/// para charset desconhecido e para outras falhas), e a leitura é limitada aqui, também no corpo sem Content-Length.
/// </para>
/// </summary>
public static class CorpoJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>O corpo lido como <typeparamref name="T"/>, ou a recusa (415, 413 ou 400) — nunca os dois.</summary>
    public static async Task<(T? Valor, IResult? Recusa)> LerAsync<T>(HttpRequest pedido, int limite, CancellationToken ct) where T : class
    {
        if (!pedido.HasJsonContentType())
            return (null, Results.Problem("Envie o corpo como application/json.", statusCode: StatusCodes.Status415UnsupportedMediaType));
        var codificacao = Codificacao(pedido.ContentType);
        if (codificacao is null)
            return (null, Results.Problem("O charset do corpo não é conhecido: envie em UTF-8.", statusCode: StatusCodes.Status415UnsupportedMediaType));
        if (pedido.ContentLength > limite)
            return (null, Grande(limite));

        var bytes = await LerAteAsync(pedido.Body, limite, ct);
        if (bytes is null)
            return (null, Grande(limite));

        try
        {
            var valor = codificacao.CodePage == Encoding.UTF8.CodePage
                ? JsonSerializer.Deserialize<T>(bytes, Json)
                : JsonSerializer.Deserialize<T>(codificacao.GetString(bytes), Json);
            return valor is null
                ? (null, Results.Problem("Envie o corpo.", statusCode: StatusCodes.Status400BadRequest))
                : (valor, null);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem($"O corpo não é um JSON válido do contrato: {ex.Message}", statusCode: StatusCodes.Status400BadRequest));
        }
    }

    /// <summary>A codificação do <c>charset</c> (sem ele, UTF-8); nula quando o .NET não a conhece.</summary>
    public static Encoding? Codificacao(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var tipo) || tipo.Charset.Length == 0)
            return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(tipo.Charset.Value!.Trim('"'));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // O utf-7 no .NET moderno.
            return null;
        }
    }

    /// <summary>Os bytes do corpo, ou nulo quando ele passa de <paramref name="limite"/>.</summary>
    private static async Task<byte[]?> LerAteAsync(Stream corpo, int limite, CancellationToken ct)
    {
        using var lido = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await corpo.ReadAsync(buffer, ct)) > 0)
        {
            if (lido.Length + n > limite)
                return null;
            lido.Write(buffer, 0, n);
        }
        return lido.ToArray();
    }

    private static IResult Grande(int limite)
        => Results.Problem($"O corpo passa de {limite} bytes.", statusCode: StatusCodes.Status413PayloadTooLarge);
}
