using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da provisão (projeto controle-remoto, item 4), no JWT do usuário sem troca pendente. A API pública só grava o
/// pedido e lê o estado: quem cria no Azure é o trabalhador <c>DevKitPage.Provisao</c>, com a identidade dele, e esta API não
/// referencia nem o provedor nem o motor. O usuário sai SEMPRE do token, e a liberação é conferida no serviço.
/// <list type="bullet">
/// <item><c>GET /</c>: se a provisão está liberada, as regiões e os recursos do usuário;</item>
/// <item><c>POST /filas</c>: pede a fila do usuário (202), ou devolve a que já existe (200); sem a liberação, 403;</item>
/// <item><c>GET /recursos/{id}</c>: o recurso com a auditoria — o de outro usuário é 404;</item>
/// <item><c>POST /recursos/{id}/repetir</c>: repete a operação que falhou (202);</item>
/// <item><c>DELETE /recursos/{id}</c>: pede a remoção (202);</item>
/// <item><c>GET|PUT /liberacoes</c>: a liberação usuário a usuário, só do admin.</item>
/// </list>
/// </summary>
public static class EndpointsDaProvisao
{
    public static void MapearProvisao(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup(ProvisaoV1.Rotas).WithTags("Provisão");

        grupo.MapGet("/", (ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComUsuario(quem, async usuario => Results.Ok(await provisao.SituacaoAsync(usuario, ct))));

        grupo.MapPost("/filas", (PedidoDeFilaV1? pedido, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComUsuario(quem, async usuario =>
            {
                var resultado = await provisao.PedirFilaAsync(usuario, pedido ?? new PedidoDeFilaV1(string.Empty), ct);
                if (resultado.Valor is not { } aceito)
                    return EndpointsDaUs425.Resultado(resultado);
                return aceito.Novo ? Results.Accepted(Local(aceito.Recurso), aceito.Recurso) : Results.Ok(aceito.Recurso);
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        grupo.MapGet("/recursos/{id:long}", (long id, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComUsuario(quem, async usuario => await provisao.RecursoAsync(usuario, id, ct) is { } recurso
                ? Results.Ok(recurso)
                : Results.Problem("Recurso não encontrado.", statusCode: StatusCodes.Status404NotFound)));

        grupo.MapPost("/recursos/{id:long}/repetir", (long id, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComUsuario(quem, async usuario => Aceito(await provisao.RepetirAsync(usuario, id, ct))))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        grupo.MapDelete("/recursos/{id:long}", (long id, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComUsuario(quem, async usuario => Aceito(await provisao.RemoverAsync(usuario, id, ct))))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        var admin = grupo.MapGroup("/liberacoes").RequireAuthorization(Seguranca.PoliticaAdmin);

        admin.MapGet("/", async (ILiberacoesDaProvisao liberacoes, CancellationToken ct) => Results.Ok(await liberacoes.ListarAsync(ct)));

        admin.MapPut("/{usuarioId:int}", async (int usuarioId, DefinicaoDaLiberacaoV1? definicao, ClaimsPrincipal quem, ILiberacoesDaProvisao liberacoes, CancellationToken ct)
            => definicao is null
                ? Results.Problem("Informe se a provisão fica liberada.", statusCode: StatusCodes.Status400BadRequest)
                : EndpointsDaUs425.Resultado(await liberacoes.DefinirAsync(usuarioId, definicao.Liberada, EndpointsDaUs687.Autor(quem), ct)))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);
    }

    /// <summary>O pedido aceito para o trabalhador: 202 com o recurso e o endereço dele; a falha no status dela.</summary>
    private static IResult Aceito(ResultadoDaGestao<RecursoNaNuvemV1> resultado)
        => resultado.Valor is { } recurso ? Results.Accepted(Local(recurso), recurso) : EndpointsDaUs425.Resultado(resultado);

    private static string Local(RecursoNaNuvemV1 recurso) => $"{ProvisaoV1.Rotas}/recursos/{recurso.Id}";

    private static Task<IResult> ComUsuario(ClaimsPrincipal quem, Func<int, Task<IResult>> acao)
        => Seguranca.UsuarioId(quem) is { } usuario ? acao(usuario) : Task.FromResult(Results.Unauthorized());
}
