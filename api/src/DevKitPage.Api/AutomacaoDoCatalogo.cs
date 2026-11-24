using System.Security.Claims;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace DevKitPage.Api;

/// <summary>Identidade OIDC exclusiva do CD. Nao aceita JWT pessoal nem chave de maquina.</summary>
public static class AutomacaoDoCatalogo
{
    public const string Esquema = "CatalogoGitHub";
    public const string Politica = "PublicarBasicoGit";
    public const string Emissor = "https://token.actions.githubusercontent.com";

    public static IServiceCollection AdicionarAutomacaoDoCatalogo(this IServiceCollection services)
    {
        services.AddOptions<OpcoesAutomacaoCatalogo>().BindConfiguration("CatalogoAutomacao");
        services.AddAuthentication().AddJwtBearer(Esquema, _ => { });
        services.AddOptions<JwtBearerOptions>(Esquema).Configure<IOptions<OpcoesAutomacaoCatalogo>>((jwt, opcoes) =>
        {
            jwt.Authority = Emissor;
            jwt.MapInboundClaims = false;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = Emissor, ValidateAudience = true, ValidAudience = opcoes.Value.Audiencia,
                ValidateIssuerSigningKey = true, ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            };
            jwt.Events = new JwtBearerEvents
            {
                OnTokenValidated = contexto =>
                {
                    if (!Autorizado(contexto.Principal!, opcoes.Value)) contexto.Fail("Identidade ou ref sem permissao para publicar o basico.");
                    return Task.CompletedTask;
                },
            };
        });
        services.AddAuthorizationBuilder().AddPolicy(Politica, p => p.AddAuthenticationSchemes(Esquema).RequireAuthenticatedUser());
        return services;
    }

    public static bool Autorizado(ClaimsPrincipal quem, OpcoesAutomacaoCatalogo o)
    {
        var referencia = quem.FindFirstValue("ref") ?? "";
        return !string.IsNullOrWhiteSpace(o.Repositorio) && !string.IsNullOrWhiteSpace(o.RepositorioId)
            && !string.IsNullOrWhiteSpace(o.Ambiente) && !string.IsNullOrWhiteSpace(o.Audiencia)
            && quem.FindFirstValue("repository") == o.Repositorio && quem.FindFirstValue("repository_id") == o.RepositorioId
            && quem.FindFirstValue("environment") == o.Ambiente && quem.FindFirstValue("event_name") == "release"
            && Regex.IsMatch(referencia, @"\Arefs/tags/catalogo-v[1-9][0-9]*\z")
            && Regex.IsMatch(quem.FindFirstValue("sha") ?? "", @"\A[a-f0-9]{40}\z")
            && quem.FindFirstValue("workflow_ref") == $"{o.Repositorio}/.github/workflows/catalogo.yml@{referencia}";
    }

    public static void MapearAutomacaoDoCatalogo(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/catalogos/basico/automacao").RequireAuthorization(Politica).WithTags("CD do basico");
        grupo.MapGet("/recibos/{sha}", async (string sha, IPublicacaoGit publicacao, CancellationToken ct)
            => await publicacao.ReciboAsync(sha, ct) is { } recibo ? Results.Ok(recibo) : Results.NotFound());
        grupo.MapPost("/conferir", (HttpContext http, IPublicacaoGit publicacao, CancellationToken ct)
            => LerEExecutarAsync(http, false, publicacao, ct));
        grupo.MapPost("/publicar", (HttpContext http, IPublicacaoGit publicacao, CancellationToken ct)
            => LerEExecutarAsync(http, true, publicacao, ct));
    }

    private static async Task<IResult> LerEExecutarAsync(HttpContext http, bool aplicar, IPublicacaoGit publicacao, CancellationToken ct)
    {
        // Base64 + envelope: limitado tambem quando a requisicao usa chunked.
        var (pedido, recusa) = await CorpoJson.LerAsync<PublicacaoGitV1>(http.Request, 23 * 1024 * 1024, ct);
        return recusa ?? await ExecutarAsync(pedido!, aplicar, http.User, publicacao, ct);
    }

    private static async Task<IResult> ExecutarAsync(PublicacaoGitV1 pedido, bool aplicar, ClaimsPrincipal quem, IPublicacaoGit publicacao, CancellationToken ct)
    {
        if (pedido.Sha != quem.FindFirstValue("sha") || pedido.Referencia != quem.FindFirstValue("ref")) return Results.Forbid();
        var resultado = await publicacao.ExecutarAsync(pedido, aplicar, "GitHub:" + quem.FindFirstValue("repository_id"), ct);
        return resultado.Valor is { } valor ? Results.Ok(valor) : Results.Problem(resultado.Mensagem,
            statusCode: resultado.Falha == FalhaNaGestao.Conflito ? 409 : 400);
    }
}

public sealed class OpcoesAutomacaoCatalogo
{
    public string Repositorio { get; set; } = "";
    public string RepositorioId { get; set; } = "";
    public string Ambiente { get; set; } = "";
    public string Audiencia { get; set; } = "";
}
