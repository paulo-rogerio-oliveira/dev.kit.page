using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da US #687 — o admin do dev.kit.page orientado a utilidade:
/// <list type="bullet">
/// <item>a LICENÇA de cada usuário (<c>/api/usuarios/{id}/licenca</c>): a de agora com o histórico, a prévia, a definição
/// (somar dias, até uma data, sem prazo, com o tipo e o motivo) e a revogação — o que a renovação do dev.kit respeita;</item>
/// <item>a EDIÇÃO da empresa (<c>PUT /api/empresas/{id}</c>): os dados básicos, o plano, o contrato e os assentos (nunca
/// abaixo dos em uso);</item>
/// <item>o painel de UTILIDADE (<c>/api/dashboard/utilidade</c>): a saúde de agora, o resultado do período, os turnos por
/// desfecho e onde as tasks travam;</item>
/// <item>a EDIÇÃO DE ARTEFATO do catálogo: a lista de todos os catálogos com a busca no conteúdo, as diferenças com a
/// versão publicada, o histórico, os anexos e o descarte do rascunho.</item>
/// </list>
/// Tudo do admin, menos o painel — que segue a <see cref="Seguranca.PoliticaPainel"/> e o recorte do gestor.
/// </summary>
public static class EndpointsDaUs687
{
    public static void MapearLicencaDosUsuarios(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/usuarios/{id:int}/licenca").WithTags("Licença dos usuários").RequireAuthorization(Seguranca.PoliticaAdmin);

        grupo.MapGet("/", async (int id, ILicencasDosUsuarios licencas, CancellationToken ct)
            => EndpointsDaUs425.Resultado(await licencas.DetalheAsync(id, ct)));

        grupo.MapPost("/previa", async (int id, DefinicaoDeLicencaV1? definicao, ILicencasDosUsuarios licencas, TimeProvider relogio, CancellationToken ct) =>
        {
            var problema = RegrasDeLicenca.ValidarDefinicao(definicao, exigirMotivo: false, relogio.GetUtcNow());
            return problema.Length > 0
                ? Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest)
                : EndpointsDaUs425.Resultado(await licencas.PreviaAsync(id, definicao!, ct));
        });

        grupo.MapPut("/", async (int id, DefinicaoDeLicencaV1? definicao, ClaimsPrincipal quem, ILicencasDosUsuarios licencas, TimeProvider relogio, CancellationToken ct) =>
        {
            var problema = RegrasDeLicenca.ValidarDefinicao(definicao, exigirMotivo: true, relogio.GetUtcNow());
            return problema.Length > 0
                ? Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest)
                : EndpointsDaUs425.Resultado(await licencas.DefinirAsync(id, definicao!, Autor(quem), ct));
        });

        grupo.MapPost("/revogar", async (int id, RevogacaoDeLicencaV1? revogacao, ClaimsPrincipal quem, ILicencasDosUsuarios licencas, CancellationToken ct) =>
        {
            var problema = RegrasDeLicenca.ValidarMotivo(revogacao?.Motivo, exigir: true);
            return problema.Length > 0
                ? Results.Problem(problema, statusCode: StatusCodes.Status400BadRequest)
                : EndpointsDaUs425.Resultado(await licencas.RevogarAsync(id, revogacao!.Motivo!.Trim(), Autor(quem), ct));
        });
    }

    public static void MapearEdicaoDeEmpresas(this IEndpointRouteBuilder app)
    {
        app.MapPut("/api/empresas/{id:int}", async (int id, EmpresaEditadaV1? edicao, ClaimsPrincipal quem, IEmpresas empresas, CancellationToken ct) =>
        {
            var erros = RegrasDaEmpresa.Validar(edicao);
            if (erros.Count > 0)
                return Results.ValidationProblem(erros, "Confira os dados da empresa.");
            return EndpointsDaUs425.Resultado(await empresas.EditarAsync(id, edicao!, Autor(quem), ct));
        }).RequireAuthorization(Seguranca.PoliticaAdmin).WithTags("Empresas");
    }

    /// <summary>
    /// O painel de utilidade, no grupo do painel: o escopo do token (o gestor só a empresa dele, e o filtro de máquina
    /// fora do escopo é 403, como nas outras rotas do dashboard). O <c>harness</c> recorta a adoção (o desconhecido é 400).
    /// </summary>
    public static void MapearPainelDeUtilidade(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard/utilidade", async (DateOnly? de, DateOnly? ate, int? maquina, string? harness, ClaimsPrincipal quem,
                IConsultasDoPainel consultas, TimeProvider relogio, CancellationToken ct) =>
            {
                if (!RegrasDoHarness.TryNormalizar(harness, out var cli))
                    return Results.Problem(RegrasDoHarness.Invalido, statusCode: StatusCodes.Status400BadRequest);
                if (Seguranca.Escopo(quem) is not { } escopo)
                    return Results.Forbid();
                if (maquina is { } id && !await consultas.MaquinaVisivelAsync(escopo, id, ct))
                    return Results.Forbid();
                var agora = relogio.GetUtcNow();
                return Results.Ok(await consultas.UtilidadeAsync(escopo, Periodo.Pedido(de, ate, DateOnly.FromDateTime(agora.UtcDateTime)), maquina, cli,
                    agora, ct));
            })
            .RequireAuthorization(Seguranca.PoliticaPainel).WithTags("Dashboard");
    }

    /// <summary>
    /// A edição de artefato do catálogo (só o admin): a lista de todos os catálogos (o <c>itens</c> é slug reservado), e,
    /// por item, as diferenças com a versão publicada, o histórico, o descarte do rascunho e os anexos. Salvar e publicar
    /// continuam nas rotas da US #461 — o rascunho só chega às máquinas pela release.
    /// </summary>
    public static void MapearEdicaoDeArtefato(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/catalogos").WithTags("Catálogos (admin)").RequireAuthorization(Seguranca.PoliticaAdmin);

        admin.MapGet("/itens", async (string? busca, string? tipo, string? escopo, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            if (tipo is { Length: > 0 } t && !TiposDeItem.Todos.Contains(t))
                return Results.Problem($"Tipo inválido: use {string.Join(", ", TiposDeItem.Todos)}.", statusCode: StatusCodes.Status400BadRequest);
            if (escopo is { Length: > 0 } e && !EscoposDoItem.Todos.Contains(e))
                return Results.Problem($"Escopo inválido: use {string.Join(", ", EscoposDoItem.Todos)}.", statusCode: StatusCodes.Status400BadRequest);
            return Results.Ok(await gestao.TodosOsItensAsync(busca, tipo, escopo, ct));
        });

        var item = admin.MapGroup("/{slug}/itens/{id:long}");

        item.MapGet("/diferencas", async (string slug, long id, IGestaoDeCatalogos gestao, CancellationToken ct)
            => EndpointsDaUs425.Resultado(await gestao.DiferencasAsync(slug, id, ct)));

        item.MapGet("/historico", async (string slug, long id, IGestaoDeCatalogos gestao, CancellationToken ct)
            => EndpointsDaUs425.Resultado(await gestao.HistoricoAsync(slug, id, ct)));

        item.MapPost("/descartar", async (string slug, long id, ClaimsPrincipal quem, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            var descartado = await gestao.DescartarRascunhoAsync(slug, id, Autor(quem), ct);
            // Sem valor e sem falha: o item nunca publicado foi apagado junto com o rascunho.
            return descartado.Falha == FalhaNaGestao.Nenhuma && descartado.Valor is null ? Results.NoContent() : EndpointsDaUs425.Resultado(descartado);
        });

        item.MapGet("/anexos", async (string slug, long id, IGestaoDeCatalogos gestao, CancellationToken ct)
            => EndpointsDaUs425.Resultado(await gestao.AnexosAsync(slug, id, ct)));

        item.MapPost("/anexos", async (string slug, long id, HttpRequest pedido, ClaimsPrincipal quem, IGestaoDeCatalogos gestao, CancellationToken ct) =>
        {
            if (!pedido.HasFormContentType)
                return Results.Problem("Envie o anexo como multipart/form-data, no campo 'arquivo'.", statusCode: StatusCodes.Status400BadRequest);
            var arquivo = (await pedido.ReadFormAsync(ct)).Files.GetFile("arquivo");
            if (arquivo is null || arquivo.Length == 0)
                return Results.Problem("Envie o arquivo no campo 'arquivo'.", statusCode: StatusCodes.Status400BadRequest);
            if (arquivo.Length > AnexosDoItem.TamanhoMaximo)
                return Results.Problem($"O anexo passa de {AnexosDoItem.TamanhoMaximo / 1024 / 1024} MB.", statusCode: StatusCodes.Status413PayloadTooLarge);
            using var memoria = new MemoryStream((int)arquivo.Length);
            await arquivo.CopyToAsync(memoria, ct);
            var anexado = await gestao.AnexarAsync(slug, id, arquivo.FileName, arquivo.ContentType, memoria.ToArray(), Autor(quem), ct);
            return anexado.Valor is { } valor
                ? Results.Created($"/api/catalogos/{slug}/itens/{id}/anexos/{valor.Id}", valor)
                : EndpointsDaUs425.Resultado(anexado);
        }).DisableAntiforgery();

        item.MapGet("/anexos/{anexoId:long}", async (string slug, long id, long anexoId, IGestaoDeCatalogos gestao, CancellationToken ct)
            => await gestao.AnexoAsync(slug, id, anexoId, ct) is { } anexo
                ? Results.File(anexo.Conteudo, anexo.TipoDeConteudo, anexo.Nome)
                : Results.Problem("Anexo não encontrado.", statusCode: StatusCodes.Status404NotFound));

        item.MapDelete("/anexos/{anexoId:long}", async (string slug, long id, long anexoId, IGestaoDeCatalogos gestao, CancellationToken ct)
            => EndpointsDaUs425.Resultado(await gestao.RemoverAnexoAsync(slug, id, anexoId, ct)));
    }

    /// <summary>Quem fez a alteração, como a trilha a grava: o login do token.</summary>
    internal static string Autor(ClaimsPrincipal quem) => quem.Identity?.Name ?? "admin";
}
