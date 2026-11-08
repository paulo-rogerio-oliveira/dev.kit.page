using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

public static class EndpointsDasDecisoes
{
    public static void MapearDecisoes(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/remoto").WithTags("Decisões remotas").RequireRateLimiting(LimiteDeTaxa.PoliticaDaConversa);
        // (correções do board remoto) ?dispensadas=true traz também os cards fechados no site, marcados — o filtro "Dispensadas".
        grupo.MapGet("/maquinas/{maquina}/decisoes", (string maquina, bool? dispensadas, ClaimsPrincipal quem, IDecisoesRemotas decisoes, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
                await decisoes.PendentesAsync(usuario, maquina, ct, dispensadas == true) is { } lista ? Results.Ok(lista) : Results.NotFound()));
        grupo.MapPost("/maquinas/{maquina}/decisoes/{pendencia}", (string maquina, string pendencia, HttpContext http, ClaimsPrincipal quem, IDecisoesRemotas decisoes, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                var (pedido, recusa) = await CorpoJson.LerAsync<ResponderDecisaoV1>(http.Request, 4096, ct);
                if (recusa is not null) return recusa;
                // Dispensar e restaurar são só do site: não pedem snapshot nem vão à máquina.
                if (pedido!.Acao is DecisoesV1.AcaoDispensar or DecisoesV1.AcaoRestaurar)
                    return pendencia.Length > 64 ? Results.Problem("Pendência inválida.", statusCode: 400)
                        : Resultado(await decisoes.DispensarAsync(usuario, maquina, pendencia, pedido.Acao == DecisoesV1.AcaoDispensar, ct));
                if (pedido.SnapshotId is not { Length: 64 } || pedido.Acao is not (DecisoesV1.AcaoResponder or DecisoesV1.AcaoAprovar or DecisoesV1.AcaoAjuste or DecisoesV1.AcaoAutorizar)
                    || pedido.Opcao?.Length > 200 || pedido.Codigo?.Length > 6)
                    return Results.Problem("Resposta inválida.", statusCode: 400);
                return Resultado(await decisoes.ResponderAsync(usuario, maquina, pendencia, pedido, ct));
            }));
        // A máquina consome uma autorização estruturada, nunca o texto da conversa.
        grupo.MapGet("/maquinas/{maquina}/decisoes/respostas", (string maquina, ClaimsPrincipal quem, IDecisoesRemotas decisoes, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario => Results.Ok(await decisoes.RespostasAsync(usuario, maquina, ct))));
        grupo.MapPost("/maquinas/{maquina}/decisoes/respostas/{id:long}/recebida", (string maquina, long id, ClaimsPrincipal quem, IDecisoesRemotas decisoes, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario => Resultado(await decisoes.ReceberAsync(usuario, maquina, id, ct))));
        grupo.MapGet("/segundo-fator", (ClaimsPrincipal quem, ISegundoFator fator, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario => Results.Ok(new EstadoDoAutenticadorV1(await fator.AtivoAsync(usuario, ct)))));
        grupo.MapPost("/segundo-fator/preparar", (HttpContext http, ClaimsPrincipal quem, ISegundoFator fator, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                http.Response.Headers.CacheControl = "no-store";
                var (pedido, recusa) = await CorpoJson.LerAsync<PrepararAutenticadorV1>(http.Request, 4096, ct);
                if (recusa is not null) return recusa;
                return Resultado(await fator.PrepararAsync(usuario, pedido!.Senha, ct));
            }));
        grupo.MapPost("/segundo-fator/confirmar", (HttpContext http, ClaimsPrincipal quem, ISegundoFator fator, CancellationToken ct)
            => EndpointsDoRemoto.ComUsuario(quem, async usuario =>
            {
                var (pedido, recusa) = await CorpoJson.LerAsync<ConfirmarAutenticadorV1>(http.Request, 1024, ct);
                if (recusa is not null) return recusa;
                var erro = await fator.VerificarAsync(usuario, pedido!.Codigo, ativar: true, ct);
                return erro is null ? Results.Ok(new EstadoDoAutenticadorV1(true)) : Results.Problem(erro, statusCode: 403);
            }));
    }
    private static IResult Resultado<T>(ResultadoDaDecisao<T> r) where T : class
        => r.Erro is null ? Results.Ok(r.Valor) : Results.Problem(r.Erro, statusCode: r.Status);
}
