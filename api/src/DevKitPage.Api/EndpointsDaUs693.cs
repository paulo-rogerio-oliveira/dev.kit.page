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
    public static void MapearExclusaoDeProjetos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/projetos").WithTags("Projetos");

        grupo.MapDelete("/{id:long}", (long id, ClaimsPrincipal quem, IProjetosDaEmpresa projetos, CancellationToken ct)
            => ComEscopo(quem, async escopo => EndpointsDaUs425.Resultado(await projetos.ApagarAsync(escopo, id, ct))));
    }

    /// <summary>A ação com o escopo do design (qualquer usuário autenticado, com ou sem empresa); token torto, 403.</summary>
    private static Task<IResult> ComEscopo(ClaimsPrincipal quem, Func<EscopoDoDesign, Task<IResult>> acao)
        => Seguranca.EscopoDoDesign(quem) is { } escopo ? acao(escopo) : Task.FromResult(Results.Forbid());
}
