using System.Security.Claims;
using DevKit.Matrix;
using DevKitPage.Core;

namespace DevKitPage.Api;

public static class EndpointsMatrix
{
    public static void MapearMatrix(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/matrix").WithTags("Matrix");
        grupo.MapGet("", async (HttpRequest request, ClaimsPrincipal quem, IMatrizes matrizes, CancellationToken ct) =>
        {
            if (Seguranca.EscopoDoDesign(quem) is not { } e) return Results.Forbid();
            var r = await matrizes.ListarAsync(e, ct);
            return r.Status == 200 ? CompatibilidadeMatrix.Responder(request, r.Valor) : Results.Problem(r.Erro, statusCode: r.Status);
        });
        grupo.MapGet("/{aplicacao}/{tipo}/{id}", async (string aplicacao, string tipo, string id, HttpRequest request, ClaimsPrincipal quem, IMatrizes matrizes, CancellationToken ct) =>
        {
            if (Seguranca.EscopoDoDesign(quem) is not { } e) return Results.Forbid();
            if (tipo == "evento" && !CompatibilidadeMatrix.Avaliacao(request)) return Results.NotFound();
            var r = await matrizes.HistoricoAsync(e, aplicacao, tipo, id, ct);
            return r.Status == 200 ? CompatibilidadeMatrix.Responder(request, r.Valor) : Results.Problem(r.Erro, statusCode: r.Status);
        });
        grupo.MapPut("/{aplicacao}/{tipo}/{id}", async (string aplicacao, string tipo, string id, bool? aplicar, EdicaoMatrix edicao,
            HttpRequest request, ClaimsPrincipal quem, IMatrizes matrizes, CancellationToken ct) =>
        {
            if (Seguranca.EscopoDoDesign(quem) is not { } e) return Results.Forbid();
            if (edicao.Documento is not { } d || d.Aplicacao != aplicacao || d.Tipo != tipo || d.Id != id)
                return Results.Problem("Alvo do arquivo difere da rota.", statusCode: 400);
            if (tipo == "evento" && !CompatibilidadeMatrix.Avaliacao(request)) return Results.Problem("Recibo exige recurso matrix-avaliacao-v1.", statusCode: 400);
            var r = await matrizes.EditarAsync(e, edicao, aplicar == true, ct);
            return r.Status == 200 ? CompatibilidadeMatrix.Responder(request, r.Valor) : Results.Problem(r.Erro, statusCode: r.Status);
        });
    }
}
