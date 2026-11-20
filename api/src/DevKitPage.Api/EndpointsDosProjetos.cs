using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

public static class EndpointsDosProjetos
{
    public static void MapearProjetos(this IEndpointRouteBuilder app)
    {
        app.MapearMatrix();
        var grupo = app.MapGroup("/api/projetos").WithTags("Projetos");
        grupo.MapearAcessosDosProjetos();
        grupo.MapGet("/recursos", () => Results.Ok(new { recursos = new[] { CicloDosItensV1.Recurso } }));
        grupo.MapPost("/{id:long}/itens/{numero:int}/parada", (long id, int numero, ParadaDeEscritaDoItemV1 parada,
            HttpRequest request, ClaimsPrincipal quem, IMaquinas maquinas, IProjetosDaEmpresa projetos, CancellationToken ct)
            => ComEscopo(quem, async e =>
            {
                var maquina = await maquinas.AutenticarAsync(request.Headers[ContratoV1.CabecalhoDaChave].ToString(), ct);
                if (maquina is null) return Results.Problem("Confirmação de parada exige a credencial da máquina do host.", statusCode: 401);
                var resultado = await projetos.ConfirmarParadaAsync(e, id, numero, maquina.MaquinaId, parada, ct);
                return EndpointsDaUs425.Resultado(resultado);
            }));
        grupo.MapPost("/{id:long}/itens/{numero:int}/decisao", (long id, int numero, PedidoDeDecisaoDoItemV1 pedido,
            HttpRequest request, ClaimsPrincipal quem, IProjetosDaEmpresa projetos, CancellationToken ct)
            => ComEscopo(quem, async e =>
            {
                if (!Compativel(request)) return ClienteAntigo();
                var resultado = await projetos.DecidirItemAsync(e, id, numero, pedido, ct);
                return resultado.Status == 200 ? Results.Ok(resultado.Impacto)
                    : Results.Problem(resultado.Mensagem, statusCode: resultado.Status);
            }));
        grupo.MapGet("", (string? time, DateTimeOffset? desde, HttpRequest request, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e =>
            {
                var lista = await projetos.ListarAsync(e, time, desde, ct);
                return !Compativel(request) && lista.Any(p => p.Itens.Any(CicloDosItensV1.Retirado)) ? ClienteAntigo() : Results.Ok(lista);
            }));
        grupo.MapGet("/{id:long}", (long id, HttpRequest request, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e => await projetos.ObterAsync(e, id, ct) is { } p
                ? !Compativel(request) && p.Itens.Any(CicloDosItensV1.Retirado) ? ClienteAntigo() : Results.Ok(p)
                : Results.Problem("Projeto não encontrado.", statusCode: 404)));
        grupo.MapPut("", (EnvioDeProjetoV1 envio, HttpRequest request, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => Gravar(quem, request, projetos, null, envio, ct));
        grupo.MapPut("/{id:long}", (long id, EnvioDeProjetoV1 envio, HttpRequest request, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => Gravar(quem, request, projetos, id, envio, ct));
    }

    private static bool Compativel(HttpRequest request) => request.Headers[CicloDosItensV1.Cabecalho] == "1";

    private static IResult ClienteAntigo() => Results.Problem("Este projeto exige " + CicloDosItensV1.Recurso
        + "; atualize o cliente para preservar as decisões humanas dos itens.", statusCode: 426);

    private static Task<IResult> Gravar(ClaimsPrincipal quem, HttpRequest request, IProjetosCompartilhados projetos, long? id, EnvioDeProjetoV1 envio, CancellationToken ct)
        => ComEscopo(quem, async e =>
        {
            if (!Compativel(request))
            {
                var atual = id is { } numero ? await projetos.ObterAsync(e, numero, ct)
                    : (await projetos.ListarAsync(e, null, null, ct)).FirstOrDefault(p => p.Slug == envio.Slug);
                if ((envio.Itens ?? []).Any(CicloDosItensV1.Retirado) || atual?.Itens.Any(CicloDosItensV1.Retirado) == true)
                    return ClienteAntigo();
            }
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
