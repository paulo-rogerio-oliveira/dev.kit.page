using System.Text.Json.Serialization;

namespace DevKitPage.Contracts.V1;

/// <summary>
/// O registro da máquina como CLIENTE (client credentials, remote-control-v3 item 9): o app logado manda a chave PÚBLICA do
/// par que o dev.kit gerou (ECDSA P-256, SubjectPublicKeyInfo em base64) com o JWT do usuário e a chave da máquina
/// (<c>X-Machine-Key</c>). A privada nunca sai da máquina.
/// </summary>
public sealed record ClienteDaMaquinaV1(string? ChavePublica);

/// <summary>
/// O registro aceito — e o ANÚNCIO do client credentials: a API que responde isto aceita o <see cref="ClientCredentialsV1.GrantType"/>
/// na <see cref="RotaDoToken"/>. O serviço do dev.kit monta a asserção com estes campos; sem eles (404 no registro), segue na
/// renovação pela máquina.
/// </summary>
/// <param name="ClientId">O id do cliente: o id anônimo da máquina (o mesmo da telemetria e do remoto).</param>
/// <param name="Escopo">O único escopo do token da máquina (<see cref="ClientCredentialsV1.Escopo"/>).</param>
/// <param name="RotaDoToken">A rota, relativa à raiz da API, que troca a asserção pelo token.</param>
/// <param name="Audiencia">O <c>aud</c> que a asserção leva.</param>
/// <param name="Algoritmo">O <c>alg</c> da asserção (<see cref="ClientCredentialsV1.Algoritmo"/>).</param>
/// <param name="ValidadeMaximaDaAssercaoSegundos">O maior intervalo entre o <c>iat</c> e o <c>exp</c> que a API aceita.</param>
public sealed record ClienteDaMaquinaRegistradoV1(
    string ClientId, string Escopo, string RotaDoToken, string Audiencia, string Algoritmo, int ValidadeMaximaDaAssercaoSegundos);

/// <summary>A resposta do token (RFC 6749, seção 5.1): o token da máquina, só para o remoto dela.</summary>
public sealed record TokenDoClienteV1(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope);

/// <summary>A recusa do token (RFC 6749, seção 5.2): o <c>error</c> do padrão e a frase.</summary>
public sealed record RecusaDoTokenV1(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);

/// <summary>As constantes do client credentials da máquina (RFC 6749 seção 4.4 e RFC 7523, o <c>private_key_jwt</c>).</summary>
public static class ClientCredentialsV1
{
    public const string RotaDoRegistro = "api/auth/cliente-da-maquina";
    public const string RotaDoToken = "api/auth/token";

    public const string GrantType = "client_credentials";
    public const string TipoDaAssercao = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
    public const string Algoritmo = "ES256";

    /// <summary>O escopo do token da máquina: o controle remoto daquela máquina e daquele usuário, e nada mais.</summary>
    public const string Escopo = "remoto";

    /// <summary>O maior <c>jti</c> aceito.</summary>
    public const int TamanhoMaximoDoJti = 128;

    /// <summary>O maior texto da chave pública (o SPKI do P-256 tem 91 bytes, 124 em base64).</summary>
    public const int TamanhoMaximoDaChave = 200;

    public const string ErroPedidoInvalido = "invalid_request";
    public const string ErroClienteInvalido = "invalid_client";
    public const string ErroConcessaoInvalida = "invalid_grant";
    public const string ErroConcessaoNaoSuportada = "unsupported_grant_type";
}
