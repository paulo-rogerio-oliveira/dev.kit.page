using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DevKitPage.Api;

/// <summary>
/// O CLIENT CREDENTIALS da máquina (remote-control-v3 item 9): o serviço do dev.kit deixa de depender do token do usuário.
/// <list type="bullet">
/// <item><c>POST /api/auth/cliente-da-maquina</c> — o VÍNCULO: o app logado (o JWT do usuário e a <c>X-Machine-Key</c>) manda a
/// chave pública do par que o dev.kit gerou. O client_id é o id anônimo da máquina. A resposta é o anúncio do client
/// credentials: com 404 (a API antiga, ou <see cref="OpcoesDoClienteDaMaquina.Habilitado"/> desligado), o dev.kit segue na
/// renovação pela máquina da t_194, que continua aceita.</item>
/// <item><c>POST /api/auth/token</c> — o TOKEN (RFC 6749 4.4, com o <c>private_key_jwt</c> da RFC 7523): a asserção ES256
/// assinada pela chave privada, curta e de uso único (<c>jti</c>). O token devolvido vale só nas rotas da
/// <see cref="Seguranca.PoliticaRemotoDaMaquina"/>, daquela máquina e daquele usuário.</item>
/// <item><c>DELETE /api/remoto/maquinas/{maquinaId}/vinculo</c> — o "Desvincular" do site (só o JWT do usuário): o token
/// emitido para a máquina deixa de valer na chamada seguinte.</item>
/// </list>
/// A troca de senha e o bloqueio do usuário revogam todas as máquinas dele: o vínculo guarda o carimbo da senha, e a emissão e
/// cada chamada conferem o usuário de novo.
/// </summary>
public static class EndpointsDoClienteDaMaquina
{
    public static void MapearClienteDaMaquina(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/auth").WithTags("Autenticação");

        grupo.MapPost("/cliente-da-maquina", async (ClienteDaMaquinaV1? pedido, HttpContext http, ClaimsPrincipal quem, IUsuarios usuarios,
            IMaquinas maquinas, IClientesDaMaquina clientes, IOptions<OpcoesDoClienteDaMaquina> opcoes, IOptions<OpcoesDeAutenticacao> autenticacao,
            CancellationToken ct) =>
        {
            if (!opcoes.Value.Habilitado)
                return Results.NotFound();
            if (Seguranca.UsuarioId(quem) is not { } id || await usuarios.ObterAsync(id, ct) is not { } usuario)
                return Results.Unauthorized();
            if (await maquinas.AutenticarAsync(http.Request.Headers[ContratoV1.CabecalhoDaChave].ToString(), ct) is not { } maquina)
                return Results.Problem("Informe a chave da máquina (X-Machine-Key): o vínculo é de um usuário numa máquina.", statusCode: StatusCodes.Status401Unauthorized);
            if (!RegrasDoClienteDaMaquina.ChavePublicaValida(pedido?.ChavePublica))
                return Results.Problem("A chave pública é um SubjectPublicKeyInfo ECDSA P-256 em base64.", statusCode: StatusCodes.Status400BadRequest);

            await clientes.RegistrarAsync(maquina.Id, usuario.Id, pedido!.ChavePublica!, EmissorDeToken.Carimbo(usuario), ct);
            return Results.Ok(new ClienteDaMaquinaRegistradoV1(maquina.MaquinaId, ClientCredentialsV1.Escopo, ClientCredentialsV1.RotaDoToken,
                TokenDaMaquina.AudienciaDaAssercao(autenticacao.Value), ClientCredentialsV1.Algoritmo, opcoes.Value.ValidadeMaximaDaAssercaoSegundos));
        });

        grupo.MapPost("/token", async (HttpContext http, IClientesDaMaquina clientes, IUsuarios usuarios, EmissorDeToken emissor,
            IOptions<OpcoesDoClienteDaMaquina> opcoes, IOptions<OpcoesDeAutenticacao> autenticacao, TimeProvider relogio, CancellationToken ct) =>
        {
            var cfg = opcoes.Value;
            if (!cfg.Habilitado)
                return Results.NotFound();
            // O token não fica em cache de proxy nenhum (RFC 6749 5.1).
            http.Response.Headers.CacheControl = "no-store";
            if (!http.Request.HasFormContentType)
                return Recusa(StatusCodes.Status400BadRequest, ClientCredentialsV1.ErroPedidoInvalido, "Envie o pedido como application/x-www-form-urlencoded.");
            var formulario = await http.Request.ReadFormAsync(ct);
            if (formulario["grant_type"].ToString() != ClientCredentialsV1.GrantType)
                return Recusa(StatusCodes.Status400BadRequest, ClientCredentialsV1.ErroConcessaoNaoSuportada, $"Só o {ClientCredentialsV1.GrantType} é aceito.");
            if (formulario["client_assertion_type"].ToString() != ClientCredentialsV1.TipoDaAssercao || formulario["client_assertion"].ToString() is not { Length: > 0 } assercao)
                return Recusa(StatusCodes.Status400BadRequest, ClientCredentialsV1.ErroPedidoInvalido, "Envie a asserção assinada (client_assertion, do tipo jwt-bearer).");
            if (formulario["scope"].ToString() is { Length: > 0 } escopo && escopo != ClientCredentialsV1.Escopo)
                return Recusa(StatusCodes.Status400BadRequest, "invalid_scope", $"O token da máquina só tem o escopo {ClientCredentialsV1.Escopo}.");

            var agora = relogio.GetUtcNow().UtcDateTime;
            var lida = await TokenDaMaquina.LerAssercaoAsync(assercao, formulario["client_id"].ToString(), clientes, cfg, autenticacao.Value, agora, ct);
            if (lida.Recusa is { } recusa)
                return Recusa(StatusCodes.Status401Unauthorized, ClientCredentialsV1.ErroClienteInvalido, recusa);
            var cliente = lida.Cliente!;

            // O usuário de novo, como na renovação: bloqueado, sem assento, apagado, com a senha trocada ou a troca pendente.
            var (resultado, usuario) = await usuarios.RenovarAsync(cliente.UsuarioId, ct);
            var senhaTrocada = usuario is not null && (EmissorDeToken.Carimbo(usuario) != cliente.Carimbo || usuario.DeveTrocarSenha);
            if (resultado != ResultadoDoLogin.Ok || senhaTrocada)
            {
                // O assento pode voltar; a senha trocada, o bloqueio e o usuário apagado não: o vínculo cai.
                if (resultado != ResultadoDoLogin.SemAssento)
                    await clientes.RevogarAsync(cliente.Id, ct);
                return Recusa(StatusCodes.Status400BadRequest, ClientCredentialsV1.ErroConcessaoInvalida,
                    "O vínculo desta máquina não vale mais (senha trocada, usuário bloqueado ou sem assento): entre no dev.kit com a sua conta.");
            }

            await clientes.MarcarEmissaoAsync(cliente.Id, ct);
            return Results.Ok(emissor.EmitirDaMaquina(cliente, cfg.ValidadeDoTokenMinutos));
        }).AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDaConta);

        // O "Desvincular" do site: só o JWT do usuário (a política padrão) — o token da máquina não se revoga a si mesmo.
        app.MapDelete("/api/remoto/maquinas/{maquinaId}/vinculo", (string maquinaId, ClaimsPrincipal quem, IClientesDaMaquina clientes, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
                RegrasDoRemoto.MaquinaIdValido(maquinaId) && await clientes.DesvincularAsync(usuario, maquinaId, ct)
                    ? Results.NoContent()
                    : Results.Problem("Máquina sem vínculo, ou não é sua.", statusCode: StatusCodes.Status404NotFound)))
            .WithTags("Controle remoto");
    }

    /// <summary>A recusa no formato da RFC 6749 (5.2), com o <c>no-store</c> já posto.</summary>
    private static IResult Recusa(int status, string erro, string descricao)
        => Results.Json(new RecusaDoTokenV1(erro, descricao), statusCode: status);
}

/// <summary>
/// O token da MÁQUINA no lado de quem o confere: o esquema JWT próprio (outra audiência), as claims, a leitura da asserção do
/// <c>private_key_jwt</c> e a conferência do vínculo a cada chamada.
/// </summary>
public static class TokenDaMaquina
{
    /// <summary>O esquema de autenticação do token da máquina.</summary>
    public const string Esquema = "TokenDaMaquina";

    /// <summary>A claim do vínculo (<see cref="ClienteDaMaquina.Id"/>).</summary>
    public const string ClaimDoCliente = "cliente";

    /// <summary>A claim do escopo (<see cref="ClientCredentialsV1.Escopo"/>).</summary>
    public const string ClaimDoEscopo = "scope";

    /// <summary>A asserção mais longa aceita, antes de qualquer leitura.</summary>
    public const int TamanhoMaximoDaAssercao = 4096;

    /// <summary>A audiência do token da máquina: nenhuma outra rota a aceita.</summary>
    public static string Audiencia(OpcoesDeAutenticacao cfg) => cfg.Audiencia + "/remoto";

    /// <summary>O <c>aud</c> que a asserção leva: a rota do token, sob o emissor desta API.</summary>
    public static string AudienciaDaAssercao(OpcoesDeAutenticacao cfg) => cfg.Emissor + "/" + ClientCredentialsV1.RotaDoToken;

    /// <summary>A asserção lida: o cliente que ela autentica, ou a recusa (a frase, nunca o motivo técnico da biblioteca).</summary>
    public sealed record AssercaoLida(ClienteParaEmitir? Cliente, string? Recusa);

    /// <summary>
    /// Confere a asserção: o cliente ATIVO do <c>iss</c>, a assinatura ES256 com a chave pública dele, a audiência, o
    /// <c>sub</c> igual ao <c>iss</c>, a validade (curta: o <c>exp</c> até <see cref="OpcoesDoClienteDaMaquina.ValidadeMaximaDaAssercaoSegundos"/>
    /// depois do <c>iat</c>) e o <c>jti</c> ainda não gasto — gastando-o.
    /// </summary>
    public static async Task<AssercaoLida> LerAssercaoAsync(
        string assercao, string clientIdDoFormulario, IClientesDaMaquina clientes, OpcoesDoClienteDaMaquina cfg, OpcoesDeAutenticacao autenticacao,
        DateTime agora, CancellationToken ct)
    {
        const string NaoConfere = "A asserção não confere (assinatura, emissor, audiência ou validade).";
        var leitor = new JsonWebTokenHandler { MapInboundClaims = false };
        if (assercao.Length > TamanhoMaximoDaAssercao || !leitor.CanReadToken(assercao))
            return new(null, NaoConfere);
        string clientId;
        try
        {
            clientId = leitor.ReadJsonWebToken(assercao).Issuer;
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenMalformedException)
        {
            return new(null, NaoConfere);
        }
        if (!RegrasDoRemoto.MaquinaIdValido(clientId) || (clientIdDoFormulario.Length > 0 && clientIdDoFormulario != clientId))
            return new(null, NaoConfere);
        if (await clientes.AtivoAsync(clientId, ct) is not { } cliente)
            return new(null, "A máquina não está vinculada (ou foi desvinculada): entre no dev.kit com a sua conta.");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(cliente.ChavePublica), out _);
        // Sem o cache de provedores da biblioteca: a chave muda por cliente, e o ECDsa é descartado no fim desta leitura.
        var chave = new ECDsaSecurityKey(ecdsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } };
        var validacao = await leitor.ValidateTokenAsync(assercao, new TokenValidationParameters
        {
            ValidIssuer = clientId,
            ValidAudience = AudienciaDaAssercao(autenticacao),
            IssuerSigningKey = chave,
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(cfg.ToleranciaDoRelogioSegundos),
            LifetimeValidator = (antes, depois, _, _) => antes is null || antes <= agora.AddSeconds(cfg.ToleranciaDoRelogioSegundos)
                && depois is { } fim && fim >= agora.AddSeconds(-cfg.ToleranciaDoRelogioSegundos),
        });
        if (!validacao.IsValid || validacao.SecurityToken is not JsonWebToken token)
            return new(null, NaoConfere);

        var jti = token.Id;
        if (token.Subject != clientId || jti is not { Length: > 0 and <= ClientCredentialsV1.TamanhoMaximoDoJti})
            return new(null, "A asserção precisa do sub igual ao client_id e de um jti (até 128 caracteres).");
        if (!token.TryGetPayloadValue<long>(JwtRegisteredClaimNames.Iat, out _))
            return new(null, "A asserção precisa do iat.");
        // Curta: o exp até o teto depois do iat — e também depois de AGORA, para um iat inventado no passado não esticá-la.
        var teto = TimeSpan.FromSeconds(cfg.ValidadeMaximaDaAssercaoSegundos);
        if (token.ValidTo - token.IssuedAt > teto || token.ValidTo - agora > teto + TimeSpan.FromSeconds(cfg.ToleranciaDoRelogioSegundos))
            return new(null, $"A asserção vale no máximo {cfg.ValidadeMaximaDaAssercaoSegundos} s.");
        if (!await clientes.GastarAssercaoAsync(cliente.Id, jti, token.ValidTo.AddSeconds(cfg.ToleranciaDoRelogioSegundos), ct))
            return new(null, "A asserção já foi usada: cada pedido de token leva um jti novo.");
        return new(cliente, null);
    }

    /// <summary>
    /// A conferência a cada chamada (<see cref="JwtBearerEvents.OnTokenValidated"/>): o escopo é o do remoto, e o vínculo
    /// continua valendo (<see cref="IClientesDaMaquina.ValeAsync"/>) — o "Desvincular", a senha trocada e o bloqueio valem já.
    /// </summary>
    public static async Task ConferirVinculoAsync(TokenValidatedContext contexto)
    {
        var quem = contexto.Principal;
        if (quem?.FindFirstValue(ClaimDoEscopo) != ClientCredentialsV1.Escopo
            || !int.TryParse(quem.FindFirstValue(ClaimDoCliente), NumberStyles.None, CultureInfo.InvariantCulture, out var cliente)
            || Seguranca.UsuarioId(quem) is not { } usuario
            || !await contexto.HttpContext.RequestServices.GetRequiredService<IClientesDaMaquina>().ValeAsync(cliente, usuario, contexto.HttpContext.RequestAborted))
            contexto.Fail("O vínculo desta máquina não vale mais.");
    }

    /// <summary>A máquina pública do token da máquina; nula no JWT do usuário.</summary>
    public static string? MaquinaDoToken(ClaimsPrincipal quem)
        => quem.HasClaim(ClaimDoEscopo, ClientCredentialsV1.Escopo) ? quem.FindFirstValue(AutenticacaoDeMaquina.ClaimDoIdPublico) ?? string.Empty : null;

    /// <summary>
    /// O token da máquina só fala por ELA: o envio com outro <c>maquinaId</c> é 403. Nulo quando pode seguir (o JWT do usuário
    /// fala por todas as máquinas dele, como sempre).
    /// </summary>
    public static IResult? OutraMaquina(ClaimsPrincipal quem, string? maquinaId)
        => MaquinaDoToken(quem) is { } dela && !string.Equals(dela, maquinaId, StringComparison.Ordinal)
            ? Results.Problem("O token desta máquina só vale para o controle remoto dela.", statusCode: StatusCodes.Status403Forbidden)
            : null;
}
