using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas do CONTEXTO do dev.kit (US #556), no lugar das convenções da US #491:
/// <list type="bullet">
/// <item><c>/api/contexto</c> — do dev e do gestor, no escopo da empresa do token: o composto que o dev.kit sincroniza, a
/// visão da landing, gravar (instrução ou, pelo devcli, a sugestão do agente), excluir, promover e a participação nas
/// equipes;</item>
/// <item><c>/api/admin/empresas/{id}/contexto</c> — do admin do board (<see cref="Seguranca.PoliticaAdmin"/>), em qualquer
/// empresa, pelo MESMO serviço (<see cref="EscopoDoContexto.DoAdmin"/>).</item>
/// </list>
/// A permissão é do serviço (a <see cref="RegrasDoContexto"/>), e nenhuma regra mora no front. Credencial no texto é 422,
/// sem ecoar o valor; a edição sobre uma versão velha é 409, com o item atual.
/// </summary>
public static class EndpointsDoContexto
{
    public static void MapearContexto(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/contexto").WithTags("Contexto");

        grupo.MapGet("/", (string? equipe, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => ComContexto(quem, async escopo => Results.Ok(await contexto.ComporAsync(escopo, equipe, ct))));

        grupo.MapGet("/visao", (ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => ComContexto(quem, async escopo => Results.Ok(await contexto.VisaoAsync(escopo, ct))));

        grupo.MapGet("/equipes/{equipe}", (string equipe, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => ComContexto(quem, async escopo => EndpointsDaUs425.Resultado(await contexto.DaEquipeAsync(escopo, equipe, ct))));

        grupo.MapPut("/", (GravacaoDeContextoV1? gravacao, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => ComContexto(quem, escopo => GravarAsync(contexto, escopo, gravacao is null ? null : gravacao with { UsuarioId = null }, ct)))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        grupo.MapDelete("/{id:long}", (long id, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => ComContexto(quem, async escopo => Resultado(await contexto.ExcluirAsync(escopo, id, ct))));

        grupo.MapPost("/{id:long}/promover", (long id, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => ComContexto(quem, async escopo => Resultado(await contexto.PromoverAsync(escopo, id, ct))));

        grupo.MapPut("/equipes/{equipe}/participantes", (string equipe, DecisaoDeParticipacaoV1? decisao, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => ComContexto(quem, async escopo => decisao is null
                ? Results.Problem("Informe o usuário e a decisão.", statusCode: StatusCodes.Status400BadRequest)
                : EndpointsDaUs425.Resultado(await contexto.DecidirParticipacaoAsync(escopo, equipe, decisao, ct))));

        var admin = app.MapGroup("/api/admin/empresas/{empresa:int}/contexto").WithTags("Contexto (admin)")
            .RequireAuthorization(Seguranca.PoliticaAdmin);

        admin.MapGet("/", async (int empresa, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => await contexto.VisaoAsync(DoAdmin(empresa, quem), ct) is { } visao ? Results.Ok(visao) : Results.NotFound());

        admin.MapPut("/", (int empresa, GravacaoDeContextoV1? gravacao, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => GravarAsync(contexto, DoAdmin(empresa, quem), gravacao, ct));

        admin.MapDelete("/{id:long}", async (int empresa, long id, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => Resultado(await contexto.ExcluirAsync(DoAdmin(empresa, quem), id, ct)));

        admin.MapPost("/{id:long}/promover", async (int empresa, long id, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => Resultado(await contexto.PromoverAsync(DoAdmin(empresa, quem), id, ct)));

        admin.MapPut("/equipes/{equipe}/participantes", async (int empresa, string equipe, DecisaoDeParticipacaoV1? decisao, ClaimsPrincipal quem, IContextoDaEmpresa contexto, CancellationToken ct)
            => decisao is null
                ? Results.Problem("Informe o usuário e a decisão.", statusCode: StatusCodes.Status400BadRequest)
                : EndpointsDaUs425.Resultado(await contexto.DecidirParticipacaoAsync(DoAdmin(empresa, quem), equipe, decisao, ct)));
    }

    /// <summary>
    /// A gravação de qualquer rota (a do token, a do admin e a convenção antiga): 400 para o que não vale, 422 para a
    /// credencial — sem ecoar o valor —, e o resultado do serviço.
    /// </summary>
    internal static async Task<IResult> GravarAsync(IContextoDaEmpresa contexto, EscopoDoContexto escopo, GravacaoDeContextoV1? gravacao, CancellationToken ct)
    {
        var problema = RegrasDoContexto.Validar(gravacao);
        if (problema.Length > 0)
            return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);
        if (RegrasDoContexto.ContemSegredo(gravacao!.Valor))
            return Results.Problem(RegrasDoContexto.MensagemDeSegredo, statusCode: StatusCodes.Status422UnprocessableEntity);
        return Resultado(await contexto.GravarAsync(escopo, gravacao, ct));
    }

    /// <summary>A ação no escopo do contexto de quem tem empresa no token; sem empresa (o admin usa as rotas dele), 403.</summary>
    internal static Task<IResult> ComContexto(ClaimsPrincipal quem, Func<EscopoDoContexto, Task<IResult>> acao)
        => EndpointsDaUs425.ComEmpresa(quem, escopo => acao(EscopoDoContexto.DoToken(escopo)));

    /// <summary>O item em 200; o conflito em 409 com o item atual; as outras falhas no status delas.</summary>
    internal static IResult Resultado(ResultadoDoContexto resultado) => resultado switch
    {
        { Valor: { } valor } => Results.Ok(valor),
        { Falha: FalhaNaGestao.Conflito } => Results.Conflict(new ConflitoDeContextoV1(resultado.Mensagem, resultado.Atual)),
        _ => EndpointsDaUs425.Resultado(ResultadoDaGestao<ContextoV1>.Falhou(resultado.Falha, resultado.Mensagem)),
    };

    private static EscopoDoContexto DoAdmin(int empresa, ClaimsPrincipal quem) => EscopoDoContexto.DoAdmin(empresa, Seguranca.UsuarioId(quem) ?? 0);
}
