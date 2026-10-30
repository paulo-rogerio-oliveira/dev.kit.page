using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas dos catálogos nomeados (US #461), no padrão do <see cref="EndpointsDaUs425"/>: o grupo, a política e o
/// <see cref="ResultadoDaGestao{T}"/> traduzido em status. Duas metades:
/// <list type="bullet">
/// <item>a DISTRIBUIÇÃO (o que o dev.kit pede): <c>/meus</c>, a delta com 304, o zip e o manifesto de uma release — o
/// básico é anônimo; os demais, do admin e dos usuários das empresas relacionadas; o resto recebe 404 (e não 403), para
/// a rota não revelar que o catálogo existe;</item>
/// <item>a GESTÃO do admin: o cadastro, o vínculo com as empresas, os itens editáveis, a conferência e a publicação, as
/// releases e a importação do básico do GitHub.</item>
/// </list>
/// </summary>
public static class EndpointsDeCatalogos
{
    public static void MapearCatalogos(this IEndpointRouteBuilder app)
    {
        var distribuicao = app.MapGroup("/api/catalogos").WithTags("Catálogos");

        distribuicao.MapGet("/meus", async (ClaimsPrincipal quem, IPublicacaoDeCatalogos publicacao, CancellationToken ct)
            => Results.Ok(await publicacao.DisponiveisAsync(Acesso(quem), ct))).AllowAnonymous();

        distribuicao.MapGet("/{slug}", async (string slug, int? desde, HttpContext http, ClaimsPrincipal quem, IPublicacaoDeCatalogos publicacao, CancellationToken ct) =>
        {
            if (await publicacao.UltimaAsync(slug, Acesso(quem), ct) is not { } ultima)
                return SemCatalogo(slug);
            http.Response.Headers.ETag = $"\"{ultima.Tag}\"";
            var inicio = Math.Max(0, desde ?? 0);
            if (RegrasDoCatalogoBase.Delta(ultima.Manifesto, inicio) is not { } delta)
                return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Ok(new CatalogoBaseV1(inicio, delta, $"/api/catalogos/{slug}/download", ultima.Sha256));
        }).AllowAnonymous();

        distribuicao.MapGet("/{slug}/download", async (string slug, HttpContext http, ClaimsPrincipal quem, IPublicacaoDeCatalogos publicacao, CancellationToken ct)
            => await TransmitirAsync(http, publicacao, slug, await publicacao.UltimaAsync(slug, Acesso(quem), ct), ct))
            .AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDoDownload);

        distribuicao.MapGet("/{slug}/releases/{versao:int}/download", async (string slug, int versao, HttpContext http, ClaimsPrincipal quem, IPublicacaoDeCatalogos publicacao, CancellationToken ct)
            => await TransmitirAsync(http, publicacao, slug, await publicacao.ReleaseAsync(slug, versao, Acesso(quem), ct), ct))
            .AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDoDownload);

        distribuicao.MapGet("/{slug}/releases/{versao:int}/manifesto", async (string slug, int versao, ClaimsPrincipal quem, IPublicacaoDeCatalogos publicacao, CancellationToken ct)
            => await publicacao.ReleaseAsync(slug, versao, Acesso(quem), ct) is { } release ? Results.Ok(release.Manifesto) : SemCatalogo(slug))
            .AllowAnonymous();

        distribuicao.MapGet("/{slug}/vitrine", async (string slug, ClaimsPrincipal quem, IPublicacaoDeCatalogos publicacao, CancellationToken ct)
            => await publicacao.UltimaAsync(slug, Acesso(quem), ct) is { } ultima
                ? Results.Ok(RegrasDoCatalogoBase.Vitrine(ultima.Manifesto, ultima.PublicadaEm))
                : SemCatalogo(slug)).AllowAnonymous();

        var admin = app.MapGroup("/api/catalogos").WithTags("Catálogos (admin)").RequireAuthorization(Seguranca.PoliticaAdmin);

        admin.MapGet("/", (IGestaoDeCatalogos gestao, CancellationToken ct) => gestao.ListarAsync(ct));

        admin.MapPost("/importar", async (ImportarCatalogoV1 pedido, ClaimsPrincipal quem, IImportacaoDeCatalogos importacao, CancellationToken ct)
            => Resultado(await importacao.ImportarAsync(pedido, Autor(quem), ct)))
            .RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        admin.MapPost("/", async (CatalogoNovoV1? novo, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            var erros = RegrasDosCatalogos.Validar(novo);
            if (erros.Count > 0)
                return Results.ValidationProblem(erros, "Confira os dados do catálogo.");
            var criado = await gestao.CriarAsync(novo!, ct);
            return criado.Valor is { } valor ? Results.Created($"/api/catalogos/{valor.Slug}", valor) : Falha(criado);
        }).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        admin.MapPut("/{slug}", async (string slug, CatalogoEditadoV1? edicao, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            var erros = RegrasDosCatalogos.Validar(edicao);
            return erros.Count > 0 ? Results.ValidationProblem(erros, "Confira os dados do catálogo.") : Resultado(await gestao.EditarAsync(slug, edicao!, ct));
        });

        admin.MapDelete("/{slug}", async (string slug, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            var excluido = await gestao.ExcluirAsync(slug, ct);
            return excluido.Valor is not null ? Results.NoContent() : Falha(excluido);
        });

        admin.MapPost("/{slug}/empresas", async (string slug, VinculoV1? vinculo, IGestaoDeCatalogos gestao, CancellationToken ct)
            => vinculo is null
                ? Results.Problem("Informe a empresa (empresaId).", statusCode: StatusCodes.Status400BadRequest)
                : Resultado(await gestao.RelacionarAsync(slug, vinculo.EmpresaId, ct)));

        admin.MapDelete("/{slug}/empresas/{empresaId:int}", async (string slug, int empresaId, IGestaoDeCatalogos gestao, CancellationToken ct)
            => Resultado(await gestao.DesrelacionarAsync(slug, empresaId, ct)));

        admin.MapGet("/{slug}/itens", async (string slug, IGestaoDeCatalogos gestao, CancellationToken ct)
            => Resultado(await gestao.ItensAsync(slug, ct)));

        admin.MapGet("/{slug}/itens/{id:long}", async (string slug, long id, IGestaoDeCatalogos gestao, CancellationToken ct)
            => Resultado(await gestao.ItemAsync(slug, id, ct)));

        admin.MapPost("/{slug}/itens", async (string slug, ItemEditadoV1? item, ClaimsPrincipal quem, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            var (conferido, erro) = RegrasDosCatalogos.Conferir(item);
            if (conferido is null)
                return Results.Problem(erro, statusCode: StatusCodes.Status400BadRequest);
            var salvo = await gestao.SalvarItemAsync(slug, null, conferido, item!.Categoria, Autor(quem), ct);
            return salvo.Valor is { } valor ? Results.Created($"/api/catalogos/{slug}/itens/{valor.Id}", valor) : Falha(salvo);
        });

        admin.MapPut("/{slug}/itens/{id:long}", async (string slug, long id, ItemEditadoV1? item, ClaimsPrincipal quem, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            var (conferido, erro) = RegrasDosCatalogos.Conferir(item);
            return conferido is null
                ? Results.Problem(erro, statusCode: StatusCodes.Status400BadRequest)
                : Resultado(await gestao.SalvarItemAsync(slug, id, conferido, item!.Categoria, Autor(quem), ct));
        });

        admin.MapDelete("/{slug}/itens/{id:long}", async (string slug, long id, ClaimsPrincipal quem, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            var retirado = await gestao.RetirarItemAsync(slug, id, Autor(quem), ct);
            // Sem valor e sem falha: o item nunca publicado foi apagado.
            return retirado.Falha == FalhaNaGestao.Nenhuma && retirado.Valor is null ? Results.NoContent() : Resultado(retirado);
        });

        admin.MapGet("/{slug}/publicacao", async (string slug, int? minDevKit, IPublicacaoDeCatalogos publicacao, CancellationToken ct)
            => Resultado(await publicacao.ConferirAsync(slug, minDevKit, ct)));

        admin.MapPost("/{slug}/publicar", async (string slug, PublicarCatalogoV1? pedido, ClaimsPrincipal quem, IPublicacaoDeCatalogos publicacao, CancellationToken ct) =>
        {
            var (feita, recusa, falha, mensagem) = await publicacao.PublicarAsync(slug, pedido?.MinDevKit, Autor(quem), ct);
            if (feita is not null)
                return Results.Created($"/api/catalogos/{slug}/releases/{feita.Release.Versao}/manifesto", feita);
            // A recusa com problemas volta com o RELATÓRIO, para a tela listar o que corrigir.
            if (recusa is not null)
                return Results.Conflict(recusa);
            return Results.Problem(mensagem, statusCode: falha switch
            {
                FalhaNaGestao.NaoEncontrado => StatusCodes.Status404NotFound,
                FalhaNaGestao.Conflito => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest,
            });
        }).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);

        admin.MapGet("/{slug}/releases", async (string slug, IPublicacaoDeCatalogos publicacao, CancellationToken ct)
            => await publicacao.ReleasesAsync(slug, ct) is { } lista ? Results.Ok(lista) : SemCatalogo(slug));

        admin.MapPost("/basico/importar", async (ClaimsPrincipal quem, IGestaoDeCatalogos gestao, CancellationToken ct)
            => Results.Ok(await gestao.ImportarBasicoAsync(Autor(quem), ct))).RequireRateLimiting(LimiteDeTaxa.PoliticaDaExportacao);
    }

    /// <summary>
    /// Quem pede, das claims: o admin, o usuário de uma empresa (o <see cref="EscopoDaEmpresa"/>) ou o anônimo — que é
    /// também quem ainda tem a troca de senha pendente.
    /// </summary>
    public static AcessoAoCatalogo Acesso(ClaimsPrincipal quem)
    {
        if (quem.Identity?.IsAuthenticated != true || quem.HasClaim(Seguranca.ClaimTrocaPendente, "true"))
            return AcessoAoCatalogo.Anonimo;
        return quem.FindFirstValue("role") == Papeis.Admin ? new AcessoAoCatalogo(true, null) : new AcessoAoCatalogo(false, Seguranca.EscopoDaEmpresa(quem));
    }

    /// <summary>O zip da release em streaming, com os cabeçalhos da API (o nome do arquivo é a tag).</summary>
    private static async Task<IResult> TransmitirAsync(HttpContext http, IPublicacaoDeCatalogos publicacao, string slug, ReleaseServida? release, CancellationToken ct)
    {
        if (release is null)
            return SemCatalogo(slug);
        await using var pacote = await publicacao.AbrirPacoteAsync(release, ct);
        if (pacote is null)
            return CatalogoIndisponivel();
        await EndpointsDaUs425.Transmitir(http, pacote, $"{release.Tag}.zip", ct);
        return Results.Empty;
    }

    /// <summary>
    /// O catálogo que não veio: o básico sem release nenhuma (nem no GitHub) é "indisponível" (503), como antes; qualquer
    /// outro é 404 — inexistente, sem release ou sem acesso dão a mesma resposta.
    /// </summary>
    private static IResult SemCatalogo(string slug)
        => slug == Catalogos.Basico ? CatalogoIndisponivel() : Results.Problem("Catálogo não encontrado.", statusCode: StatusCodes.Status404NotFound);

    private static IResult CatalogoIndisponivel()
        => Results.Problem("O catálogo do dev.kit está indisponível no momento. Tente de novo em alguns minutos.", statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string Autor(ClaimsPrincipal quem) => quem.Identity?.Name ?? "admin";

    private static IResult Resultado<T>(ResultadoDaGestao<T> resultado) where T : class
        => resultado.Valor is { } valor ? Results.Ok(valor) : Falha(resultado);

    /// <summary>A falha no status HTTP: 404, 409 ou 400, sempre com a mensagem.</summary>
    private static IResult Falha<T>(ResultadoDaGestao<T> resultado) where T : class
        => Results.Problem(resultado.Mensagem, statusCode: resultado.Falha switch
        {
            FalhaNaGestao.NaoEncontrado => StatusCodes.Status404NotFound,
            FalhaNaGestao.Conflito => StatusCodes.Status409Conflict,
            FalhaNaGestao.Proibida => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        });
}
