using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da US #550: o CONTEXTO do dev.kit gerenciável por nível. O contexto é a família de chaves
/// <c>contexto.&lt;tema&gt;</c> das convenções da #491 — mesma tabela, mesmo contrato (<see cref="ConvencaoV1"/>) e a
/// mesma rota que o dev.kit já lê (<c>GET /api/empresa/convencoes?time=</c>, que não muda). Aqui ficam só:
/// <list type="bullet">
/// <item>as EQUIPES CONHECIDAS da empresa, para o gestor escolher a equipe do contexto (<c>/api/empresa/equipes</c>);</item>
/// <item>as rotas do ADMIN GERAL numa empresa (<c>/api/empresas/{id}/...</c>, sob a <see cref="Seguranca.PoliticaAdmin"/>):
/// o admin não tem empresa no token e recebe 403 em <c>/api/empresa/*</c> — em vez de afrouxar aquelas rotas, estas dizem
/// a empresa no caminho e montam o <see cref="EscopoDaEmpresa.DoAdmin"/>, atendido pelo MESMO serviço.</item>
/// </list>
/// Empresa inexistente é 404; login de outra empresa no nível usuário também (sem revelar que ele existe).
/// </summary>
public static class EndpointsDaUs550
{
    public static void MapearContextoPorNivel(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/empresa/equipes", (ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo => Results.Ok(await conhecimento.EquipesAsync(escopo, ct))))
            .WithTags("Conhecimento da empresa");

        var admin = app.MapGroup("/api/empresas/{id:int}").WithTags("Contexto da empresa (admin)").RequireAuthorization(Seguranca.PoliticaAdmin);

        admin.MapGet("/convencoes/todas", (int id, ClaimsPrincipal quem, IEmpresas empresas, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => ComoAdmin(id, quem, empresas, ct, async escopo => Results.Ok(await conhecimento.AuditoriaDeConvencoesAsync(escopo, ct))));

        admin.MapGet("/equipes", (int id, ClaimsPrincipal quem, IEmpresas empresas, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => ComoAdmin(id, quem, empresas, ct, async escopo => Results.Ok(await conhecimento.EquipesAsync(escopo, ct))));

        admin.MapPut("/convencoes", (int id, ConvencaoV1? convencao, ClaimsPrincipal quem, IEmpresas empresas, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => ComoAdmin(id, quem, empresas, ct, async escopo =>
            {
                var problema = RegrasDoConhecimentoDaEmpresa.ValidarConvencao(convencao);
                return problema.Length > 0
                    ? Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest)
                    : EndpointsDaUs425.Resultado(await conhecimento.GravarConvencaoAsync(escopo, convencao!, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);
    }

    /// <summary>A ação do admin geral na empresa do caminho; empresa que não existe é 404.</summary>
    private static async Task<IResult> ComoAdmin(
        int empresaId, ClaimsPrincipal quem, IEmpresas empresas, CancellationToken ct, Func<EscopoDaEmpresa, Task<IResult>> acao)
    {
        if (Seguranca.UsuarioId(quem) is not { } adminId)
            return Results.Unauthorized();
        if (!(await empresas.ListarAsync(ct)).Any(e => e.Id == empresaId))
            return Results.Problem("Empresa não encontrada.", statusCode: StatusCodes.Status404NotFound);
        return await acao(EscopoDaEmpresa.DoAdmin(empresaId, adminId));
    }
}
