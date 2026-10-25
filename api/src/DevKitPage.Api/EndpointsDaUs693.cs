using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da US #693: os PROJETOS do dev.kit compartilhados por escopo — o projeto (repositórios, branch, definição,
/// autonomia, paralelismo, destino da PR) e o plano dele (itens e ondas). Ficam sob <c>/api/projetos</c>, com o escopo do
/// design (<see cref="EscopoDoDesign"/>): quem não tem empresa guarda o projeto no nível usuário.
/// <list type="bullet">
/// <item><c>GET /?time=&amp;desde=</c>: os visíveis, com os itens — o <c>desde</c> é a sincronização incremental;</item>
/// <item><c>GET /{id}</c>: um projeto visível (404 quando não é);</item>
/// <item><c>PUT /</c>: cria pelo (escopo, slug) — 201 — ou substitui com a versão lida — 200; outra versão, 409;</item>
/// <item><c>DELETE /{id}</c>: só o autor ou o gestor.</item>
/// </list>
/// </summary>
public static class EndpointsDaUs693
{
    public static void MapearProjetos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/projetos").WithTags("Projetos");

        grupo.MapGet("/", (string? time, DateTimeOffset? desde, ClaimsPrincipal quem, IProjetosDaEmpresa projetos, CancellationToken ct)
            => ComEscopo(quem, async escopo => Results.Ok(await projetos.ListarAsync(escopo, time, desde, ct))));

        grupo.MapGet("/{id:long}", (long id, ClaimsPrincipal quem, IProjetosDaEmpresa projetos, CancellationToken ct)
            => ComEscopo(quem, async escopo => await projetos.ObterAsync(escopo, id, ct) is { } projeto
                ? Results.Ok(projeto)
                : Results.Problem("Projeto não encontrado.", statusCode: StatusCodes.Status404NotFound)));

        grupo.MapPut("/", (EnvioDeProjetoV1? envio, ClaimsPrincipal quem, IProjetosDaEmpresa projetos, CancellationToken ct)
            => ComEscopo(quem, async escopo =>
            {
                var problema = RegrasDosProjetos.Validar(envio);
                if (problema.Length > 0)
                    return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);

                var gravado = await projetos.GravarAsync(escopo, envio!, ct);
                if (gravado.Valor is not { } feito)
                    return EndpointsDaUs425.Resultado(gravado);
                return feito.Novo ? Results.Created($"/api/projetos/{feito.Projeto.Id}", feito.Projeto) : Results.Ok(feito.Projeto);
            }));

        grupo.MapDelete("/{id:long}", (long id, ClaimsPrincipal quem, IProjetosDaEmpresa projetos, CancellationToken ct)
            => ComEscopo(quem, async escopo => EndpointsDaUs425.Resultado(await projetos.ApagarAsync(escopo, id, ct))));
    }

    /// <summary>A ação com o escopo do design (qualquer usuário autenticado, com ou sem empresa); token torto, 403.</summary>
    private static Task<IResult> ComEscopo(ClaimsPrincipal quem, Func<EscopoDoDesign, Task<IResult>> acao)
        => Seguranca.EscopoDoDesign(quem) is { } escopo ? acao(escopo) : Task.FromResult(Results.Forbid());
}
