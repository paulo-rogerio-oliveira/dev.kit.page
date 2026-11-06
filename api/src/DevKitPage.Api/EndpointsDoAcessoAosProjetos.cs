using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

public static class EndpointsDoAcessoAosProjetos
{
    public static void MapearAcessosDosProjetos(this RouteGroupBuilder grupo)
    {
        grupo.MapGet("/{id:long}/acessos", (long id, ClaimsPrincipal quem, IAcessosAosProjetos acessos, CancellationToken ct)
            => ComEscopo(quem, async e => EndpointsDaUs425.Resultado(await acessos.ListarAsync(e, AcessoV1.RecursoProjeto, id, ct))));
        grupo.MapPost("/{id:long}/acessos", (long id, ConcessaoDeAcessoV1 pedido, ClaimsPrincipal quem, IAcessosAosProjetos acessos, CancellationToken ct)
            => ComEscopo(quem, async e => EndpointsDaUs425.Resultado(await acessos.ConcederAsync(e, AcessoV1.RecursoProjeto, id, pedido, ct))));
        grupo.MapPut("/{id:long}/acessos/{usuario:int}", (long id, int usuario, ConcessaoDeAcessoV1 pedido, ClaimsPrincipal quem, IAcessosAosProjetos acessos, CancellationToken ct)
            => ComEscopo(quem, async e => EndpointsDaUs425.Resultado(await acessos.AlterarAsync(e, AcessoV1.RecursoProjeto, id, usuario, pedido.Papel, ct))));
        grupo.MapDelete("/{id:long}/acessos/{usuario:int}", (long id, int usuario, ClaimsPrincipal quem, IAcessosAosProjetos acessos, CancellationToken ct)
            => ComEscopo(quem, async e => EndpointsDaUs425.Resultado(await acessos.RemoverAsync(e, AcessoV1.RecursoProjeto, id, usuario, ct))));
        grupo.MapPost("/{id:long}/dono", (long id, TransferenciaDeDonoV1 pedido, ClaimsPrincipal quem, IAcessosAosProjetos acessos, CancellationToken ct)
            => ComEscopo(quem, async e => EndpointsDaUs425.Resultado(await acessos.TransferirAsync(e, AcessoV1.RecursoProjeto, id, pedido, ct))));
    }

    private static Task<IResult> ComEscopo(ClaimsPrincipal quem, Func<EscopoDoDesign, Task<IResult>> acao)
        => Seguranca.EscopoDoDesign(quem) is { } e ? acao(e) : Task.FromResult(Results.Forbid());
}
