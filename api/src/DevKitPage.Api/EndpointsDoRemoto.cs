using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas do controle remoto, fase 1 (<c>docs/contrato-remoto-v1.md</c>): o dev.kit publica, com o JWT do USUÁRIO, o
/// heartbeat e o retrato de cada máquina, e o site lê — só o dono. Todas exigem o usuário autenticado sem troca pendente
/// (a política padrão); a chave de máquina (<c>X-Machine-Key</c>) sozinha não entra.
/// <list type="bullet">
/// <item><c>POST /heartbeat</c>: 200 com o intervalo esperado;</item>
/// <item><c>PUT /retrato</c>: 200, com <c>aplicado</c> falso para o retrato mais velho que o gravado;</item>
/// <item><c>GET /maquinas</c>: as máquinas do usuário, com <c>conectada</c>;</item>
/// <item><c>GET /maquinas/{maquinaId}/retrato</c>: o último retrato; a máquina de outro usuário é 404.</item>
/// </list>
/// Os envios recusam, cada um com o seu status: 415 (não é JSON), 413 (corpo grande), 400 (JSON inválido, forma do
/// contrato, contrato desconhecido — com <c>codigo</c>) e 422 (dado sensível — com <c>codigo</c> e <c>campos</c>, nunca o
/// valor). Nenhuma recusa nasce de captura genérica de exceção (<see cref="CorpoJson"/>).
/// </summary>
public static class EndpointsDoRemoto
{
    /// <summary>A recusa do envio com dado sensível (422): o texto não volta na resposta.</summary>
    public const string MensagemDeDadoSensivel =
        "O envio tem token ou dado de cliente (e-mail, CPF, CNPJ ou telefone) e não foi gravado. Sanitize os campos indicados e envie de novo.";

    public static void MapearRemoto(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/remoto").WithTags("Controle remoto");

        grupo.MapPost("/heartbeat", (HttpContext http, ClaimsPrincipal quem, IRemoto remoto, CancellationToken ct)
            => ComUsuario(quem, async usuario =>
            {
                var (heartbeat, recusa) = await CorpoJson.LerAsync<HeartbeatV1>(http.Request, RemotoV1.TamanhoMaximoDoHeartbeat, ct);
                if (recusa is not null)
                    return recusa;
                if (Recusar(heartbeat!.Contrato, RegrasDoRemoto.Validar(heartbeat), () => RegrasDoRemoto.CamposSensiveis(heartbeat)) is { } problema)
                    return problema;
                return Results.Ok(await remoto.RegistrarHeartbeatAsync(usuario, heartbeat, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDoRemoto);

        grupo.MapPut("/retrato", (HttpContext http, ClaimsPrincipal quem, IRemoto remoto, CancellationToken ct)
            => ComUsuario(quem, async usuario =>
            {
                var (retrato, recusa) = await CorpoJson.LerAsync<RetratoV1>(http.Request, RemotoV1.TamanhoMaximoDoRetrato, ct);
                if (recusa is not null)
                    return recusa;
                if (Recusar(retrato!.Contrato, RegrasDoRemoto.Validar(retrato), () => RegrasDoRemoto.CamposSensiveis(retrato)) is { } problema)
                    return problema;
                return Results.Ok(await remoto.GravarRetratoAsync(usuario, retrato, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDoRemoto);

        grupo.MapGet("/maquinas", (ClaimsPrincipal quem, IRemoto remoto, CancellationToken ct)
            => ComUsuario(quem, async usuario => Results.Ok(await remoto.MaquinasAsync(usuario, ct))));

        grupo.MapGet("/maquinas/{maquinaId}/retrato", (string maquinaId, ClaimsPrincipal quem, IRemoto remoto, CancellationToken ct)
            => ComUsuario(quem, async usuario =>
                RegrasDoRemoto.MaquinaIdValido(maquinaId) && await remoto.RetratoAsync(usuario, maquinaId, ct) is { } retrato
                    ? Results.Ok(retrato)
                    : Results.Problem("Máquina sem retrato, ou não é sua.", statusCode: StatusCodes.Status404NotFound)));
    }

    /// <summary>
    /// A recusa do envio já lido, na ordem: o contrato desconhecido (400 com <c>codigo</c> e o contrato aceito), a forma
    /// (400) e o dado sensível (422 com os campos); nula quando ele pode ser gravado.
    /// </summary>
    private static IResult? Recusar(int contrato, string problema, Func<IReadOnlyList<CampoSensivelV1>> sensiveis)
    {
        if (!RegrasDoRemoto.ContratoConhecido(contrato))
            return Results.Problem($"Esta API fala o contrato {RemotoV1.Contrato} do controle remoto, e o envio é do {contrato}.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["codigo"] = RemotoV1.CodigoContratoDesconhecido, ["contratoAceito"] = RemotoV1.Contrato });
        if (problema.Length > 0)
            return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);
        var campos = sensiveis();
        return campos.Count == 0
            ? null
            : Results.Problem(MensagemDeDadoSensivel, statusCode: StatusCodes.Status422UnprocessableEntity,
                extensions: new Dictionary<string, object?> { ["codigo"] = RemotoV1.CodigoDadoSensivel, ["campos"] = campos });
    }

    /// <summary>A ação com o usuário do token; o token sem usuário é 403.</summary>
    private static Task<IResult> ComUsuario(ClaimsPrincipal quem, Func<int, Task<IResult>> acao)
        => Seguranca.UsuarioId(quem) is { } usuario ? acao(usuario) : Task.FromResult(Results.Forbid());
}
