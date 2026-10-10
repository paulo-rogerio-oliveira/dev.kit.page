using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DevKitPage.Api;

/// <summary>
/// As DUAS autenticações da API, separadas de propósito: o USUÁRIO do dashboard entra por JWT
/// (login e senha), e a MÁQUINA do dev.kit entra pela chave no cabeçalho <c>X-Machine-Key</c>. A
/// ingestão aceita só a segunda — um JWT de usuário não ingere, e uma chave de máquina não lê o
/// dashboard.
/// </summary>
public static class Seguranca
{
    /// <summary>A claim do token emitido para quem ainda tem de trocar a senha.</summary>
    public const string ClaimTrocaPendente = "troca_pendente";

    /// <summary>A política da troca de senha: basta estar autenticado (mesmo com a troca pendente).</summary>
    public const string PoliticaAutenticado = "Autenticado";

    /// <summary>A política da ingestão: só a chave da máquina.</summary>
    public const string PoliticaMaquina = "Maquina";

    /// <summary>
    /// A política do que é só do admin (US #381): as empresas, os pedidos de demonstração e a reação
    /// aos erros. Inclui a regra da troca pendente, porque substitui a política padrão.
    /// </summary>
    public const string PoliticaAdmin = "Admin";

    /// <summary>
    /// A política do painel (<c>/api/dashboard</c>, US #405): o admin e o gestor, sem troca pendente. O dev
    /// (o usuário do dev.kit) entra no app e baixa a versão, mas recebe 403 aqui — a regra mora no
    /// <see cref="Papeis.EntraNoPainel"/>, e o <see cref="EscopoDoPainel"/> dele também é nulo (defesa em dobro).
    /// </summary>
    public const string PoliticaPainel = "Painel";

    /// <summary>A claim da empresa do gestor (US #381) — o <see cref="EscopoDoPainel"/> sai dela e do papel.</summary>
    public const string ClaimEmpresa = "empresa";

    public static IServiceCollection AdicionarSeguranca(this IServiceCollection servicos, IHostEnvironment ambiente)
    {
        // Sem segredo configurado FORA de Production, um segredo efêmero (os tokens morrem na
        // reinicialização — é desenvolvimento). Em Production a API se recusa a subir: ver ValidarSegredo.
        servicos.PostConfigure<OpcoesDeAutenticacao>(opcoes =>
        {
            if (!ambiente.IsProduction() && (opcoes.Segredo?.Length ?? 0) < OpcoesDeAutenticacao.TamanhoMinimoDoSegredo)
                opcoes.Segredo = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        });

        servicos.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer()
            .AddScheme<AuthenticationSchemeOptions, AutenticacaoDeMaquina>(AutenticacaoDeMaquina.Esquema, _ => { });

        servicos.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OpcoesDeAutenticacao>>((jwt, opcoes) =>
            {
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters = Parametros(opcoes.Value);
            });

        servicos.AddAuthorizationBuilder()
            // TUDO exige usuário autenticado e SEM troca de senha pendente, a menos que diga o contrário.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(c => !c.User.HasClaim(ClaimTrocaPendente, "true"))
                .Build())
            .AddPolicy(PoliticaAutenticado, p => p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser())
            .AddPolicy(PoliticaAdmin, p => p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireRole(Papeis.Admin)
                .RequireAssertion(c => !c.User.HasClaim(ClaimTrocaPendente, "true")))
            .AddPolicy(PoliticaPainel, p => p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(c => !c.User.HasClaim(ClaimTrocaPendente, "true") && Papeis.EntraNoPainel(c.User.FindFirstValue("role"))))
            .AddPolicy(PoliticaMaquina, p => p.AddAuthenticationSchemes(AutenticacaoDeMaquina.Esquema).RequireClaim(AutenticacaoDeMaquina.ClaimDaMaquina));

        servicos.AddSingleton<EmissorDeToken>();
        return servicos;
    }

    /// <summary>Recusa subir em Production sem um segredo JWT de verdade (Key Vault, variável de ambiente).</summary>
    public static void ValidarSegredo(IServiceProvider provedor, IHostEnvironment ambiente)
    {
        var segredo = provedor.GetRequiredService<IOptions<OpcoesDeAutenticacao>>().Value.Segredo ?? string.Empty;
        if (ambiente.IsProduction() && segredo.Length < OpcoesDeAutenticacao.TamanhoMinimoDoSegredo)
            throw new InvalidOperationException(
                $"Jwt:Segredo não configurado (mínimo {OpcoesDeAutenticacao.TamanhoMinimoDoSegredo} caracteres): em Production ele vem do Key Vault ou da variável Jwt__Segredo.");
    }

    public static TokenValidationParameters Parametros(OpcoesDeAutenticacao opcoes) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = opcoes.Emissor,
        ValidateAudience = true,
        ValidAudience = opcoes.Audiencia,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = Chave(opcoes),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero, // o token vencido é vencido: a web volta ao login na hora
        NameClaimType = JwtRegisteredClaimNames.Name,
        RoleClaimType = "role",
    };

    public static SymmetricSecurityKey Chave(OpcoesDeAutenticacao opcoes) => new(Encoding.UTF8.GetBytes(opcoes.Segredo));

    /// <summary>O id do usuário do token.</summary>
    public static int? UsuarioId(ClaimsPrincipal usuario)
        => int.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    /// <summary>
    /// O escopo do painel de quem fez a requisição (US #381), das claims <c>role</c> e
    /// <see cref="ClaimEmpresa"/>. Nulo é token sem escopo válido — a rota responde 403.
    /// </summary>
    public static EscopoDoPainel? Escopo(ClaimsPrincipal usuario)
        => EscopoDoPainel.DasClaims(usuario.FindFirstValue("role"), usuario.FindFirstValue(ClaimEmpresa));
}

/// <summary>Emite o JWT do usuário.</summary>
public sealed class EmissorDeToken(IOptions<OpcoesDeAutenticacao> opcoes, TimeProvider relogio)
{
    public LoginResponse Emitir(Usuario usuario)
    {
        var cfg = opcoes.Value;
        var agora = relogio.GetUtcNow();
        var expira = agora.AddMinutes(Math.Max(1, cfg.ExpiracaoMinutos));

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = usuario.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [JwtRegisteredClaimNames.Name] = usuario.Login,
        };
        // O papel (US #381): o admin semeado continua "admin" pelo EhAdmin; o gestor leva a empresa.
        var papel = usuario.EhAdmin ? Papeis.Admin : usuario.Papel;
        claims["role"] = papel;
        if (papel == Papeis.Gestor && usuario.EmpresaId is { } empresa)
            claims[Seguranca.ClaimEmpresa] = empresa.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (usuario.DeveTrocarSenha)
            claims[Seguranca.ClaimTrocaPendente] = "true";

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Claims = claims,
            Issuer = cfg.Emissor,
            Audience = cfg.Audiencia,
            IssuedAt = agora.UtcDateTime,
            NotBefore = agora.UtcDateTime,
            Expires = expira.UtcDateTime,
            SigningCredentials = new SigningCredentials(Seguranca.Chave(cfg), SecurityAlgorithms.HmacSha256),
        });

        return new LoginResponse(token, expira, usuario.DeveTrocarSenha, usuario.Login, usuario.EhAdmin, papel, usuario.Empresa?.Nome);
    }
}

/// <summary>
/// A autenticação da MÁQUINA: a chave do cabeçalho <see cref="ContratoV1.CabecalhoDaChave"/>,
/// conferida pelo hash. Sem cabeçalho, não opina (a política da ingestão devolve 401).
/// </summary>
public sealed class AutenticacaoDeMaquina(
    IOptionsMonitor<AuthenticationSchemeOptions> opcoes, ILoggerFactory log, UrlEncoder encoder, IMaquinas maquinas)
    : AuthenticationHandler<AuthenticationSchemeOptions>(opcoes, log, encoder)
{
    public const string Esquema = "Maquina";

    /// <summary>O id interno da máquina.</summary>
    public const string ClaimDaMaquina = "maquina";

    /// <summary>O id anônimo que o dev.kit gerou.</summary>
    public const string ClaimDoIdPublico = "maquina_publica";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ContratoV1.CabecalhoDaChave, out var valor) || string.IsNullOrWhiteSpace(valor))
            return AuthenticateResult.NoResult();

        var maquina = await maquinas.AutenticarAsync(valor.ToString(), Context.RequestAborted);
        if (maquina is null)
            return AuthenticateResult.Fail("Chave de máquina inválida.");

        var identidade = new ClaimsIdentity(new[]
        {
            new Claim(ClaimDaMaquina, maquina.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimDoIdPublico, maquina.MaquinaId),
        }, Esquema);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidade), Esquema));
    }
}
