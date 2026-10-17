using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da US #491: o que a empresa compartilha com o dev.kit além do catálogo de skills — o CATÁLOGO DE
/// REPOSITÓRIOS (o caminho e a descrição que o agente grava ao terminar cada task, e que todos da empresa consultam
/// para resolver o repositório de um pedido) e as CONVENÇÕES da empresa e dos times (hoje, o nome do branch da task).
/// <para>
/// Ficam sob <c>/api/empresa</c>, no escopo da empresa do token, mas abertas ao DEV — e não só ao gestor, como as
/// outras rotas dali: consultar e gravar o catálogo é de quem trabalha. A convenção da EMPRESA é do gestor; a do
/// TIME, de qualquer um da empresa; a do USUÁRIO, dele mesmo (o serviço decide). A tela do site audita e edita tudo
/// isso (<c>/convencoes/todas</c> traz o autor e a data). O admin, sem empresa no token, recebe 403.
/// </para>
/// </summary>
public static class EndpointsDaUs491
{
    public static void MapearConhecimentoDaEmpresa(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/empresa").WithTags("Conhecimento da empresa");

        grupo.MapGet("/repositorios", (string? filtro, ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo => Results.Ok(await conhecimento.RepositoriosAsync(escopo, filtro, ct))));

        grupo.MapPost("/repositorios", (GravacaoDeRepositorioV1? gravacao, ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo =>
            {
                var problema = RegrasDoConhecimentoDaEmpresa.ValidarRepositorio(gravacao);
                return problema.Length > 0
                    ? Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest)
                    : EndpointsDaUs425.Resultado(await conhecimento.GravarRepositorioAsync(escopo, gravacao!, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        // A origem vai na consulta, e não no caminho: é uma URL (ou uma pasta), cheia de barras.
        grupo.MapDelete("/repositorios", (string? origem, ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo => string.IsNullOrWhiteSpace(origem)
                ? Results.Problem("Informe a origem do repositório a remover (?origem=).", statusCode: StatusCodes.Status400BadRequest)
                : EndpointsDaUs425.Resultado(await conhecimento.RemoverRepositorioAsync(escopo, origem, ct))));

        grupo.MapGet("/convencoes", (string? time, ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo => Results.Ok(await conhecimento.ConvencoesAsync(escopo, time, ct))));

        // A AUDITORIA do site: todas as que quem consulta pode ver, com o autor e a data.
        grupo.MapGet("/convencoes/todas", (ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo => Results.Ok(await conhecimento.AuditoriaDeConvencoesAsync(escopo, ct))));

        grupo.MapPut("/convencoes", (ConvencaoV1? convencao, ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo =>
            {
                var problema = RegrasDoConhecimentoDaEmpresa.ValidarConvencao(convencao);
                return problema.Length > 0
                    ? Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest)
                    : EndpointsDaUs425.Resultado(await conhecimento.GravarConvencaoAsync(escopo, convencao!, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);
    }
}
