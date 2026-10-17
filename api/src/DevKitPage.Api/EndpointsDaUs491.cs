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
/// outras rotas dali: consultar e gravar o catálogo é de quem trabalha. Desde a US #556 as convenções são o CONTEXTO
/// (<see cref="EndpointsDoContexto"/>), e as rotas de convenção daqui são o adaptador dele para o dev.kit antigo — com as
/// permissões do contexto: a da EMPRESA é do gestor; a da EQUIPE (o time), de quem participa dela aprovado; a do USUÁRIO,
/// dele mesmo. O admin, sem empresa no token, recebe 403.
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

        // OBSOLETAS desde a US #556: as convenções viraram o contexto (EndpointsDoContexto). Estas três rotas ficam como
        // adaptador fino do MESMO serviço, para o dev.kit já instalado: só as instruções, time = equipe.
        grupo.MapGet("/convencoes", (string? time, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => EndpointsDoContexto.ComContexto(quem, async escopo
                => Results.Ok(AdaptadorDeConvencoes.Convencoes((await contexto.ComporAsync(escopo, time, ct)).Itens))));

        grupo.MapGet("/convencoes/todas", (ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => EndpointsDoContexto.ComContexto(quem, async escopo
                => Results.Ok(AdaptadorDeConvencoes.Convencoes((await contexto.VisaoAsync(escopo, ct))!.Itens))));

        grupo.MapPut("/convencoes", (ConvencaoV1? convencao, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => EndpointsDoContexto.ComContexto(quem, async escopo =>
            {
                if (convencao is null)
                    return Results.Problem("Informe a convenção.", statusCode: StatusCodes.Status400BadRequest);
                var gravacao = AdaptadorDeConvencoes.ParaGravacao(convencao);
                if (!string.IsNullOrWhiteSpace(convencao.Valor))
                {
                    var gravada = await EndpointsDoContexto.GravarAsync(contexto, escopo, gravacao, ct);
                    return gravada is IValueHttpResult { Value: ContextoV1 item } ? Results.Ok(AdaptadorDeConvencoes.ParaConvencao(item)) : gravada;
                }

                // Vazio APAGA a convenção do nível (a regra da US #491): volta a valer a de baixo. A visão só traz o item de
                // nível usuário do próprio usuário, então o alvo é sempre o dele.
                var problema = RegrasDoContexto.Validar(gravacao with { Valor = "-" });
                if (problema.Length > 0)
                    return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);
                var equipe = gravacao.Nivel == NiveisDeContexto.Equipe ? RegrasDoContexto.NormalizarEquipe(gravacao.Equipe) : string.Empty;
                var chave = gravacao.Chave!.Trim().ToLowerInvariant();
                var alvo = (await contexto.VisaoAsync(escopo, ct))!.Itens.FirstOrDefault(i => i.Nivel == gravacao.Nivel && i.Equipe == equipe
                    && i.Chave == chave && i.Origem == OrigensDeContexto.Usuario);
                if (alvo is not null)
                {
                    var excluida = EndpointsDoContexto.Resultado(await contexto.ExcluirAsync(escopo, alvo.Id, ct));
                    if (excluida is not IValueHttpResult { Value: ContextoV1 })
                        return excluida;
                }

                return Results.Ok(new ConvencaoV1(convencao.Nivel!.Trim().ToLowerInvariant(), chave, string.Empty, equipe));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);
    }
}
