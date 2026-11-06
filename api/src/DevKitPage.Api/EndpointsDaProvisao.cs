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
/// <item>o App Service e o Static Web App (item 12): <c>GET /cota</c> (o uso de cada tipo e o custo do mês), <c>POST /aplicativos</c>
/// (409 com <see cref="ProvisaoV1.CodigoCotaCheia"/> ou <see cref="ProvisaoV1.CodigoCustoNoLimite"/>) e
/// <c>POST /recursos/{id}/remover</c> com o código do segundo fator (403 com <see cref="ProvisaoV1.CodigoSegundoFator"/> sem ele);</item>
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

        grupo.MapGet("/cota", (ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, _) => Results.Ok(await provisao.CotaAsync(usuario, ct))));

        grupo.MapPost("/aplicativos", (PedidoDeAplicativoV1? pedido, ClaimsPrincipal quem, IProvisao provisao, IOptions<OpcoesDaProvisao> opcoes, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, autor) =>
            {
                var (valido, erros) = RegrasDaProvisao.ValidarAplicativo(pedido, opcoes.Value);
                if (valido is null)
                    return Results.ValidationProblem(erros, "Confira o pedido do aplicativo.");
                return Aceito(await provisao.PedirAplicativoAsync(usuario, autor, valido, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        grupo.MapPost("/recursos/{id:long}/repetir", (long id, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, autor) => Aceito(await provisao.RepetirAsync(usuario, autor, id, ct))))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        // A fila se remove pelo DELETE; o aplicativo, só com o código do segundo fator (o DELETE dele é 403 com o código).
        grupo.MapDelete("/recursos/{id:long}", (long id, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, autor) => Aceito(await provisao.RemoverAsync(usuario, autor, id, null, ct))))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaProvisao);

        grupo.MapPost("/recursos/{id:long}/remover", (long id, RemocaoComSegundoFatorV1? remocao, ClaimsPrincipal quem, IProvisao provisao, CancellationToken ct)
            => ComLiberacao(quem, provisao, ct, async (usuario, autor) => Aceito(await provisao.RemoverAsync(usuario, autor, id, remocao?.Codigo, ct))))
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

    /// <summary>A falha em status HTTP, com o <c>codigo</c> quando o site precisa distinguir a recusa (a cota, o custo, o segundo fator).</summary>
    private static IResult Falha<T>(ResultadoDaGestao<T> resultado) where T : class
        => Results.Problem(resultado.Mensagem, statusCode: resultado.Falha switch
        {
            FalhaNaGestao.NaoEncontrado => StatusCodes.Status404NotFound,
            FalhaNaGestao.Conflito => StatusCodes.Status409Conflict,
            FalhaNaGestao.Proibida => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        }, extensions: resultado.Codigo.Length > 0 ? new Dictionary<string, object?> { ["codigo"] = resultado.Codigo } : null);

    /// <summary>O login de quem fez, para a auditoria.</summary>
    private static string Autor(ClaimsPrincipal quem) => quem.Identity?.Name ?? $"usuario {Seguranca.UsuarioId(quem)}";
}
