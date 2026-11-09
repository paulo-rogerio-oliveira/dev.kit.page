using System.Security.Cryptography;

namespace DevKitPage.Core;

/// <summary>
/// O par VAPID do push do board (<see cref="OpcoesDoPush"/>): P-256, a pública no formato não comprimido (65 bytes, <c>04||X||Y</c>)
/// e a privada como o escalar de 32 bytes, as duas em base64url sem preenchimento — o formato que a <see cref="OpcoesDoPush.Disponivel"/>
/// aceita. Gerado UMA vez por ambiente (<c>dotnet DevKitPage.Api.dll --gerar-chaves-vapid</c>): trocar o par invalida todas as
/// assinaturas do push, que o navegador refaz na próxima visita ao board.
/// </summary>
public static class ChavesVapid
{
    /// <summary>O argumento da API que imprime um par novo e sai, sem subir o site.</summary>
    public const string Argumento = "--gerar-chaves-vapid";

    public sealed record Par(string Publica, string Privada);

    public static Par Gerar()
    {
        using var chave = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = chave.ExportParameters(includePrivateParameters: true);
        // O .NET devolve as coordenadas e o escalar com o tamanho da curva (32 bytes), já com os zeros à esquerda.
        return new Par(Base64Url([4, .. p.Q.X!, .. p.Q.Y!]), Base64Url(p.D!));
    }

    /// <summary>
    /// O texto que o gerador imprime: as três app settings e o comando do App Service, com o assunto de exemplo. Sai na saída
    /// padrão de quem roda, e nunca num log da API.
    /// </summary>
    public static string Instrucoes(Par par, string assunto = "mailto:suporte@devkit.page") =>
        $"""
        # Par VAPID novo (P-256). Grave as três app settings do App Service da API e reinicie.
        # A privada é SEGREDO: não a cole em chamado nem em commit.
        Push__ChavePublica={par.Publica}
        Push__ChavePrivada={par.Privada}
        Push__Assunto={assunto}

        # az webapp config appsettings set -g <grupo> -n <api> --settings "Push__ChavePublica={par.Publica}" "Push__ChavePrivada=<a privada acima>" "Push__Assunto={assunto}"
        """;

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
