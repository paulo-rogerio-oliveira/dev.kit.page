using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da provisão no Azure (<c>docs/arquitetura.md</c>, "Motor de provisão"). A API pública só GRAVA o pedido — não tem
/// credencial do Azure; o worker (<c>DevKitPage.Provisao</c>) executa com a identidade gerenciada dele.
/// <list type="bullet">
/// <item><c>GET /api/provisao/permissao</c>: de todo usuário logado — é o que decide se a ação aparece no site;</item>
/// <item><c>GET /recursos</c>, <c>GET /recursos/{id}</c> (com a auditoria), <c>POST /filas</c> (202 o pedido novo, 200 a fila que
/// já existe), <c>POST /recursos/{id}/repetir</c> e <c>DELETE /recursos/{id}</c> (202 a remoção pedida, 200 a já pedida): só com
/// a provisão LIBERADA — sem ela, 403 com <c>codigo</c> <see cref="ProvisaoV1.CodigoNaoLiberada"/>, mesmo que o site esconda a ação;</item>
/// <item><c>PUT /api/usuarios/{id}/recursos/{recurso}</c>: o admin libera ou retira.</item>
/// </list>
/// O recurso de outro usuário é 404, sem revelar que existe. As escritas do usuário têm o limite por usuário
/// (<see cref="LimiteDeTaxa.PoliticaDaProvisao"/>).
/// </summary>
public static class EndpointsDaProvisao
{
    public const string MensagemNaoLiberada =
        "A provisão no Azure não está liberada para o seu usuário. Peça a liberação ao administrador do dev.kit.";

    public static void MapearProvisao(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/provisao").WithTags("Provisão");

        grupo.MapGet("/permissao", async (ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => Seguranca.UsuarioId(quem) is { } usuario ? Results.Ok(await provisao.PermissaoAsync(usuario, ct)) : Results.Forbid());

        grupo.MapGet("/recursos", (ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, _) => Results.Ok(await provisao.RecursosAsync(usuario, ct))));

        grupo.MapGet("/recursos/{id:long}", (long id, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, _) => await provisao.DetalheAsync(usuario, id, ct) is { } detalhe
                ? Results.Ok(detalhe)
                : Results.Problem("Recurso não encontrado.", statusCode: StatusCodes.Status404NotFound)));

        grupo.MapPost("/filas", (PedidoDeFilaV1? pedido, ClaimsPrincipal quem, IProvisao provisao, IOptions<OpcoesDaProvisao> opcoes, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, autor) =>
            {
                if (RegrasDaProvisao.Regiao(pedido?.Regiao, opcoes.Value) is not { } regiao)
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["regiao"] = [$"A região é uma de: {string.Join(", ", RegrasDaProvisao.Regioes(opcoes.Value))}."] },
                        "Confira a região.");
                return Aceito(await provisao.PedirFilaAsync(usuario, autor, regiao, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        grupo.MapPost("/recursos/{id:long}/repetir", (long id, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, autor) => Aceito(await provisao.RepetirAsync(usuario, autor, id, ct))))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        grupo.MapDelete("/recursos/{id:long}", (long id, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, autor) => Aceito(await provisao.RemoverAsync(usuario, autor, id, ct))))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        app.MapPut("/api/usuarios/{id:int}/recursos/{recurso}",
                async (int id, string recurso, LiberacaoDeRecursoV1? liberacao, ClaimsPrincipal quem, IGestaoDeUsuarios usuarios, CancellationToken ct) =>
                {
                    if (liberacao is null)
                        return Results.Problem("Informe se o recurso fica liberado.", statusCode: StatusCodes.Status400BadRequest);
                    var resultado = await usuarios.LiberarRecursoAsync(id, recurso, liberacao.Liberado, Autor(quem), ct);
                    return resultado.Valor is { } valor ? Results.Ok(valor) : Falha(resultado);
                })
            .WithTags("Usuários").RequireAuthorization(Seguranca.PoliticaAdmin);
    }

    /// <summary>
    /// A ação só com a provisão liberada (a permissão do admin ou o plano), conferida a CADA pedido — a retirada vale na hora,
    /// sem esperar o token vencer. O token sem usuário é 403.
    /// </summary>
    private static async Task<IResult> ComLiberacao(ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct, Func<int, string, Task<IResult>> acao)
    {
        if (Seguranca.UsuarioId(quem) is not { } usuario)
            return Results.Forbid();
        if (!(await provisao.PermissaoAsync(usuario, ct)).Liberada)
            return Results.Problem(MensagemNaoLiberada, statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?> { ["codigo"] = ProvisaoV1.CodigoNaoLiberada });
        return await acao(usuario, Autor(quem));
    }

    /// <summary>O pedido NOVO é 202 (o worker ainda vai executar); o que já existia volta como está, 200.</summary>
    private static IResult Aceito(ResultadoDaGestao<PedidoAceito> resultado)
        => resultado.Valor is not { } valor
            ? Falha(resultado)
            : valor.Novo
                ? Results.Accepted($"/api/provisao/recursos/{valor.Recurso.Id}", valor.Recurso)
                : Results.Ok(valor.Recurso);

    private static IResult Falha<T>(ResultadoDaGestao<T> resultado) where T : class
        => Results.Problem(resultado.Mensagem, statusCode: resultado.Falha switch
        {
            FalhaNaGestao.NaoEncontrado => StatusCodes.Status404NotFound,
            FalhaNaGestao.Conflito => StatusCodes.Status409Conflict,
            FalhaNaGestao.Proibida => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        });

    /// <summary>O login de quem fez, para a auditoria.</summary>
    private static string Autor(ClaimsPrincipal quem) => quem.Identity?.Name ?? $"usuario {Seguranca.UsuarioId(quem)}";
}
