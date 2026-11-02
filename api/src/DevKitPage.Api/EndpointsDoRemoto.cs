using System.Security.Claims;
using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api;

/// <summary>
/// As rotas do controle remoto (projeto controle-remoto, fase 1), o contrato v1 em <c>docs/contrato-remoto-v1.md</c>. Todas
/// ficam na política padrão, o JWT do usuário sem troca de senha pendente: a chave de máquina não entra aqui, porque a
/// telemetria é anônima e isto tem dono. O usuário sai SEMPRE do token.
/// <list type="bullet">
/// <item><c>PUT /heartbeat</c>: a máquina diz que está viva, com o limite por usuário;</item>
/// <item><c>PUT /retrato</c>: a foto de projetos, fluxos e pendências, recusada inteira com token ou dado de cliente (422);</item>
/// <item><c>GET /maquinas</c>: as máquinas do dono;</item>
/// <item><c>GET /maquinas/{maquinaId}</c>: a máquina e o último retrato — a de outro usuário é 404, sem revelar que existe.</item>
/// </list>
/// </summary>
public static class EndpointsDoRemoto
{
    public static void MapearRemoto(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup(RemotoV1.Rotas).WithTags("Controle remoto");

        grupo.MapPut("/heartbeat", (HttpContext http, IOptions<OpcoesDoRemoto> opcoes, IControleRemoto remoto, CancellationToken ct)
            => ComUsuario(http.User, async usuario =>
            {
                var (heartbeat, recusa) = await LerAsync<HeartbeatV1>(http, opcoes.Value.MaxBytesPorEnvio, "heartbeat", ct);
                if (recusa is not null)
                    return recusa;
                return RegrasDoRemoto.Validar(heartbeat) is { } problema
                    ? Recusa(problema)
                    : EndpointsDaUs425.Resultado(await remoto.RegistrarHeartbeatAsync(usuario, heartbeat!, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDoRemoto);

        grupo.MapPut("/retrato", (HttpContext http, IOptions<OpcoesDoRemoto> opcoes, IControleRemoto remoto, CancellationToken ct)
            => ComUsuario(http.User, async usuario =>
            {
                var (retrato, recusa) = await LerAsync<RetratoV1>(http, opcoes.Value.MaxBytesPorEnvio, "retrato", ct);
                if (recusa is not null)
                    return recusa;
                return RegrasDoRemoto.Validar(retrato) is { } problema
                    ? Recusa(problema)
                    : EndpointsDaUs425.Resultado(await remoto.RegistrarRetratoAsync(usuario, retrato!, ct));
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDoRemoto);

        grupo.MapGet("/maquinas", (ClaimsPrincipal quem, IControleRemoto remoto, CancellationToken ct)
            => ComUsuario(quem, async usuario => Results.Ok(await remoto.MaquinasAsync(usuario, ct))));

        grupo.MapGet("/maquinas/{maquinaId}", (string maquinaId, ClaimsPrincipal quem, IControleRemoto remoto, CancellationToken ct)
            => ComUsuario(quem, async usuario => await remoto.PainelAsync(usuario, maquinaId, ct) is { } painel
                ? Results.Ok(painel)
                : Results.Problem("Máquina não encontrada.", statusCode: StatusCodes.Status404NotFound)));
    }

    /// <summary>
    /// O corpo lido à mão, com o teto em bytes, o <c>Content-Type</c> que não é JSON em 415 e o JSON quebrado ou fora do
    /// contrato em 400. A mensagem não traz o erro do serializador: ele citaria o que veio no corpo.
    /// </summary>
    private static async Task<(T? Corpo, IResult? Recusa)> LerAsync<T>(HttpContext http, long limite, string oQue, CancellationToken ct) where T : class
    {
        var acimaDoTeto = Results.Problem($"O {oQue} passa de {limite} bytes.", statusCode: StatusCodes.Status413PayloadTooLarge);
        var tipoErrado = Results.Problem($"Envie o {oQue} como application/json, numa codificação conhecida.", statusCode: StatusCodes.Status415UnsupportedMediaType);
        if (http.Request.ContentLength > limite)
            return (null, acimaDoTeto);
        if (!http.Request.HasJsonContentType())
            return (null, tipoErrado);
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } corpo)
            corpo.MaxRequestBodySize = limite; // o corpo sem Content-Length (chunked) também para no limite

        try
        {
            return (await http.Request.ReadFromJsonAsync<T>(ct), null);
        }
        catch (JsonException)
        {
            return (null, Results.Problem($"O corpo não é um {oQue} v1: JSON inválido, tipo errado ou campo fora do contrato.", statusCode: StatusCodes.Status400BadRequest));
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return (null, acimaDoTeto);
        }
        catch (InvalidOperationException)
        {
            // O tipo é JSON, mas com um charset que não existe: é assim que a leitura o recusa.
            return (null, tipoErrado);
        }
    }

    /// <summary>A recusa no status dela; no 422, o campo e o motivo vão à parte, para o dev.kit registrar sem o valor.</summary>
    private static IResult Recusa(RecusaDoRemoto recusa)
        => recusa.Status == StatusCodes.Status422UnprocessableEntity
            ? Results.Problem(recusa.Mensagem, statusCode: recusa.Status, extensions: new Dictionary<string, object?> { ["campo"] = recusa.Campo, ["motivo"] = recusa.Motivo })
            : Results.Problem(recusa.Mensagem, statusCode: recusa.Status);

    private static Task<IResult> ComUsuario(ClaimsPrincipal quem, Func<int, Task<IResult>> acao)
        => Seguranca.UsuarioId(quem) is { } usuario ? acao(usuario) : Task.FromResult(Results.Unauthorized());
}
