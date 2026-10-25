using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da US #663: a ORIGEM do contexto. O que a pessoa escreve é REGRA para o agente (a gravação de sempre,
/// <c>PUT /api/empresa/convencoes</c>); o que o agente do dev.kit registra ao concluir uma task é SUGESTÃO — consulta,
/// nunca obrigação — até alguém com o papel da camada promovê-la:
/// <list type="bullet">
/// <item><c>POST /api/empresa/contexto/sugestoes</c>: o agente sugere, com o token do usuário; a origem é SEMPRE agente
/// (o corpo não a escolhe) e a regra do mesmo tema nunca é tocada;</item>
/// <item><c>POST /api/empresa/contexto/{id}/promover</c>: a sugestão vira a regra da camada (versão velha → 409);</item>
/// <item><c>DELETE /api/empresa/contexto/{id}</c>: descarta a sugestão;</item>
/// <item>os mesmos três do admin geral em <c>/api/empresas/{id}/contexto/*</c>, sob a <see cref="Seguranca.PoliticaAdmin"/>,
/// atendidos pelo MESMO serviço.</item>
/// </list>
/// Toda gravação de contexto (regra e sugestão) com um segredo é 422, sem ecoar o valor. Empresa alheia é 404; papel
/// sem direito, 403.
/// </summary>
public static class EndpointsDaUs663
{
    /// <summary>A resposta do contexto com segredo: 422, e o texto NÃO volta na resposta.</summary>
    public const string MensagemDeSegredo =
        "O texto parece ter um segredo (token, senha, chave ou connection string) e não foi gravado. Tire o segredo e grave de novo.";

    public static void MapearOrigemDoContexto(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/empresa/contexto").WithTags("Conhecimento da empresa");

        grupo.MapPost("/sugestoes", (SugestaoDeContextoV1? sugestao, ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, escopo => SugerirAsync(escopo, sugestao, conhecimento, ct)))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        grupo.MapPost("/{id:long}/promover", (long id, PromocaoDeContextoV1? promocao, ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo
                => EndpointsDaUs425.Resultado(await conhecimento.PromoverAsync(escopo, id, promocao?.VersaoEsperada, ct))));

        grupo.MapDelete("/{id:long}", (long id, ClaimsPrincipal quem, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs425.ComEmpresa(quem, async escopo => EndpointsDaUs425.Resultado(await conhecimento.DescartarAsync(escopo, id, ct))));

        var admin = app.MapGroup("/api/empresas/{empresa:int}/contexto").WithTags("Contexto da empresa (admin)").RequireAuthorization(Seguranca.PoliticaAdmin);

        admin.MapPost("/sugestoes", (int empresa, SugestaoDeContextoV1? sugestao, ClaimsPrincipal quem, IEmpresas empresas, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs550.ComoAdmin(empresa, quem, empresas, ct, escopo => SugerirAsync(escopo, sugestao, conhecimento, ct)))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        admin.MapPost("/{id:long}/promover", (int empresa, long id, PromocaoDeContextoV1? promocao, ClaimsPrincipal quem, IEmpresas empresas, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs550.ComoAdmin(empresa, quem, empresas, ct, async escopo
                => EndpointsDaUs425.Resultado(await conhecimento.PromoverAsync(escopo, id, promocao?.VersaoEsperada, ct))));

        admin.MapDelete("/{id:long}", (int empresa, long id, ClaimsPrincipal quem, IEmpresas empresas, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
            => EndpointsDaUs550.ComoAdmin(empresa, quem, empresas, ct, async escopo
                => EndpointsDaUs425.Resultado(await conhecimento.DescartarAsync(escopo, id, ct))));
    }

    /// <summary>
    /// O 422 do contexto com segredo (<see cref="RegrasDosRelatos.ContemSegredo"/>), ou nulo quando a gravação segue. Só
    /// o CONTEXTO é conferido: o modelo do branch (<c>feature/{equipe}/{id}</c>) não é texto livre.
    /// </summary>
    internal static IResult? SegredoNoContexto(string? chave, string? valor)
        => RegrasDoConhecimentoDaEmpresa.EhContexto(chave) && RegrasDosRelatos.ContemSegredo(valor)
            ? Results.Problem(MensagemDeSegredo, statusCode: StatusCodes.Status422UnprocessableEntity)
            : null;

    private static async Task<IResult> SugerirAsync(EscopoDaEmpresa escopo, SugestaoDeContextoV1? sugestao, IConhecimentoDaEmpresa conhecimento, CancellationToken ct)
    {
        var problema = RegrasDoConhecimentoDaEmpresa.ValidarSugestao(sugestao);
        if (problema.Length > 0)
            return Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest);
        return SegredoNoContexto(RegrasDoConhecimentoDaEmpresa.ChaveDoTema(sugestao!.Tema), sugestao.Texto)
            ?? EndpointsDaUs425.Resultado(await conhecimento.SugerirAsync(escopo, sugestao, ct));
    }
}
