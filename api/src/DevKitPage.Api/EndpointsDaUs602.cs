using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da US #602: os RELATOS dos usuários do dev.kit (defeito, sugestão, dúvida).
/// <list type="bullet">
/// <item><c>POST /api/relatos</c>: quem está logado relata — o usuário e a empresa saem das claims do JWT, nunca do
/// corpo —, com o limite de envios por usuário (<see cref="LimiteDeTaxa.PoliticaDosRelatos"/>, 429 acima dele);</item>
/// <item><c>GET /api/relatos/meus</c>: o autor acompanha o estado e a resposta dos SEUS relatos;</item>
/// <item>a gestão (<c>GET /api/relatos</c>, o detalhe, <c>PATCH .../estado</c> e <c>POST .../resposta</c>), sob a
/// <see cref="Seguranca.PoliticaPainel"/>: o admin geral vê todos, o gestor só os da empresa dele
/// (<see cref="EscopoDoPainel"/>) — o relato de outra empresa é 404, e o dev recebe 403.</item>
/// </list>
/// </summary>
public static class EndpointsDaUs602
{
    public static void MapearRelatos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/relatos").WithTags("Relatos");

        grupo.MapPost("/", async (RelatoNovoV1? relato, ClaimsPrincipal quem, IRelatos relatos, CancellationToken ct) =>
        {
            if (Seguranca.UsuarioId(quem) is not { } usuario)
                return Results.Unauthorized();
            var problema = RegrasDosRelatos.Validar(relato);
            if (problema.Length > 0)
                return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);

            RegrasDosRelatos.LerCaptura(relato!.Captura, out var captura);
            var recebido = await relatos.RegistrarAsync(usuario, EmpresaDoToken(quem), relato, captura, ct);
            return Results.Created($"/api/relatos/{recebido.Id}", recebido);
        }).RequireAuthorization(Seguranca.PoliticaAutenticado).RequireRateLimiting(LimiteDeTaxa.PoliticaDosRelatos);

        grupo.MapGet("/meus", async (ClaimsPrincipal quem, IRelatos relatos, CancellationToken ct)
            => Seguranca.UsuarioId(quem) is { } usuario ? Results.Ok(await relatos.MeusAsync(usuario, ct)) : Results.Unauthorized());

        var gestao = app.MapGroup("/api/relatos").WithTags("Relatos (gestão)").RequireAuthorization(Seguranca.PoliticaPainel);

        gestao.MapGet("/", (string? estado, string? categoria, int? empresa, DateOnly? de, DateOnly? ate, int? pagina, int? tamanho,
                ClaimsPrincipal quem, IRelatos relatos, CancellationToken ct)
            => ComEscopo(quem, async escopo =>
            {
                if (!string.IsNullOrWhiteSpace(estado) && !EstadosDeRelato.Todos.Contains(RegrasDosRelatos.Normalizar(estado)))
                    return Results.Problem($"O estado é {string.Join(", ", EstadosDeRelato.Todos)}.", statusCode: StatusCodes.Status400BadRequest);
                if (!string.IsNullOrWhiteSpace(categoria) && !CategoriasDeRelato.Todas.Contains(RegrasDosRelatos.Normalizar(categoria)))
                    return Results.Problem($"A categoria é {string.Join(", ", CategoriasDeRelato.Todas)}.", statusCode: StatusCodes.Status400BadRequest);
                var filtro = new FiltroDeRelatos(estado, categoria, empresa, de, ate, pagina ?? 1, tamanho ?? 20);
                return Results.Ok(await relatos.ListarAsync(escopo, filtro, ct));
            }));

        gestao.MapGet("/{id:long}", (long id, ClaimsPrincipal quem, IRelatos relatos, CancellationToken ct)
            => ComEscopo(quem, async escopo => Encontrado(await relatos.DetalheAsync(escopo, id, ct))));

        gestao.MapPatch("/{id:long}/estado", (long id, AlterarEstadoDoRelatoV1? pedido, ClaimsPrincipal quem, IRelatos relatos, CancellationToken ct)
            => ComEscopo(quem, async escopo =>
            {
                var problema = RegrasDosRelatos.ValidarEstado(pedido);
                return problema.Length > 0
                    ? Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest)
                    : Encontrado(await relatos.AlterarEstadoAsync(escopo, id, pedido!.Estado!, ct));
            }));

        gestao.MapPost("/{id:long}/resposta", (long id, RespostaAoRelatoV1? resposta, ClaimsPrincipal quem, IRelatos relatos, CancellationToken ct)
            => ComEscopo(quem, async escopo =>
            {
                var problema = RegrasDosRelatos.ValidarResposta(resposta);
                if (problema.Length > 0)
                    return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);
                if (Seguranca.UsuarioId(quem) is not { } quemResponde)
                    return Results.Unauthorized();
                return Encontrado(await relatos.ResponderAsync(escopo, id, quemResponde, resposta!.Texto!, ct));
            }));
    }

    /// <summary>A empresa da claim (o gestor e o dev a levam); nula para quem não tem.</summary>
    private static int? EmpresaDoToken(ClaimsPrincipal quem)
        => int.TryParse(quem.FindFirstValue(Seguranca.ClaimEmpresa), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;

    /// <summary>A ação da gestão com o escopo do painel; sem escopo válido (o gestor sem empresa), 403.</summary>
    private static Task<IResult> ComEscopo(ClaimsPrincipal quem, Func<EscopoDoPainel, Task<IResult>> acao)
        => Seguranca.Escopo(quem) is { } escopo ? acao(escopo) : Task.FromResult(Results.Forbid());

    private static IResult Encontrado(RelatoDetalheV1? detalhe)
        => detalhe is null ? Results.Problem("Relato não encontrado.", statusCode: StatusCodes.Status404NotFound) : Results.Ok(detalhe);
}
