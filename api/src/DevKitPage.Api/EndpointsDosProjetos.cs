using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

public static class EndpointsDosProjetos
{
    public static void MapearProjetos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/projetos").WithTags("Projetos");
        grupo.MapGet("", (string? time, DateTimeOffset? desde, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e => Results.Ok(await projetos.ListarAsync(e, time, desde, ct))));
        grupo.MapGet("/{id:long}", (long id, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e => await projetos.ObterAsync(e, id, ct) is { } p
                ? Results.Ok(p) : Results.Problem("Projeto não encontrado.", statusCode: 404)));
        grupo.MapPut("", (EnvioDeProjetoV1 envio, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => Gravar(quem, projetos, null, envio, ct));
        grupo.MapPut("/{id:long}", (long id, EnvioDeProjetoV1 envio, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => Gravar(quem, projetos, id, envio, ct));
    }

    private static Task<IResult> Gravar(ClaimsPrincipal quem, IProjetosCompartilhados projetos, long? id, EnvioDeProjetoV1 envio, CancellationToken ct)
        => ComEscopo(quem, async e =>
        {
            var r = await projetos.GravarAsync(e, id, envio, ct);
            return r.Status switch
            {
                201 => Results.Created($"/api/projetos/{r.Projeto!.Id}", r.Projeto),
                200 => Results.Ok(r.Projeto),
                409 => Results.Problem(r.Mensagem, statusCode: 409, extensions: new Dictionary<string, object?> { ["atual"] = r.Projeto }),
                _ => Results.Problem(r.Mensagem, statusCode: r.Status),
            };
        });

    private static Task<IResult> ComEscopo(ClaimsPrincipal quem, Func<EscopoDoDesign, Task<IResult>> acao)
        => Seguranca.EscopoDoDesign(quem) is { } e ? acao(e) : Task.FromResult(Results.Forbid());
}
