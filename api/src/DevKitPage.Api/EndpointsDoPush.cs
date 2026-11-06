using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

public static class EndpointsDoPush
{
    public static void MapearPush(this IEndpointRouteBuilder app)
    {
        // A política fallback global também recusa a troca de senha pendente; a política default não a substitui.
        var grupo = app.MapGroup("/api/remoto/push").WithTags("Push do Board")
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaConversa);
        grupo.AddEndpointFilter(async (contexto, proximo) =>
        {
            contexto.HttpContext.Response.Headers.CacheControl = "no-store";
            return await proximo(contexto);
        });
        grupo.MapGet("", (IPushDoBoard push) => Results.Ok(push.Configuracao()));
        grupo.MapPost("/assinaturas", (HttpContext http, ClaimsPrincipal quem, IPushDoBoard push, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                var (pedido, recusa) = await CorpoJson.LerAsync<AssinaturaDePushV1>(http.Request, 4096, ct);
                if (recusa is not null) return recusa;
                var resultado = await push.AssinarAsync(usuario, pedido!, ct);
                return resultado.Erro is null ? Results.Ok(resultado.Valor) : Results.Problem(resultado.Erro, statusCode: resultado.Status);
            }));
        grupo.MapGet("/assinaturas/{id}", (string id, ClaimsPrincipal quem, IPushDoBoard push, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario => RegrasDoPush.IdValido(id) && await push.AssinadaAsync(usuario, id, ct)
                ? Results.Ok(new AssinaturaRegistradaV1(id)) : Results.NotFound()));
        grupo.MapDelete("/assinaturas/{id}", (string id, ClaimsPrincipal quem, IPushDoBoard push, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                if (!RegrasDoPush.IdValido(id)) return Results.BadRequest();
                await push.CancelarAsync(usuario, id, ct);
                return Results.NoContent();
            }));
        grupo.MapGet("/entregas/{id}", (string id, ClaimsPrincipal quem, IPushDoBoard push, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario => RegrasDoPush.IdValido(id) && await push.DestinoAsync(usuario, id, ct) is { } destino
                ? Results.Ok(destino) : Results.Problem("Esta decisão não está mais pendente ou não pertence à sua conta. Consulte o Board atualizado.", statusCode: 404)));
    }
}
