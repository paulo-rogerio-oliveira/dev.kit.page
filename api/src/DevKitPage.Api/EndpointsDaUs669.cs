using System.Globalization;
using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

/// <summary>
/// As rotas da US #669: o DESIGN compartilhado — os projetos por repositório, as telas e as versões (captura e
/// proposta), com as imagens no armazém (o Blob Storage). Ficam sob <c>/api/design</c>, e não sob <c>/api/empresa</c>:
/// quem não tem empresa também guarda o seu design, no nível usuário (o <see cref="EscopoDoDesign"/> existe sem
/// empresa; o de <c>/api/empresa</c> não).
/// <list type="bullet">
/// <item><c>GET /projetos?time=&amp;desde=</c>: os visíveis, com telas e versões — o <c>desde</c> é a sincronização
/// incremental da abertura do dev.kit;</item>
/// <item><c>POST /versoes</c> (multipart: os metadados e o PNG em <c>imagem</c>): 201 nova, 200 o reenvio do mesmo PNG,
/// 409 o mesmo número com outro PNG, 413 o teto do plano;</item>
/// <item><c>GET /{projeto}/{tela}/v{n}</c>: o PNG pelo LINK ESTÁVEL (o da PR da semana); <c>DELETE</c> no mesmo caminho,
/// só o autor ou o gestor;</item>
/// <item><c>GET /uso</c>: o uso do teto do plano.</item>
/// </list>
/// </summary>
public static class EndpointsDaUs669
{
    public static void MapearDesign(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/design").WithTags("Design");

        grupo.MapGet("/projetos", (string? time, DateTimeOffset? desde, ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => ComEscopo(quem, async escopo => Results.Ok(await design.ProjetosAsync(escopo, time, desde, ct))));

        grupo.MapGet("/projetos/{id:long}", (long id, ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => ComEscopo(quem, async escopo => await design.ProjetoAsync(escopo, id, ct) is { } projeto
                ? Results.Ok(projeto)
                : Results.Problem("Projeto de design não encontrado.", statusCode: StatusCodes.Status404NotFound)));

        grupo.MapGet("/uso", (ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => ComEscopo(quem, async escopo => Results.Ok(await design.UsoAsync(escopo, ct))));

        // O multipart é lido à mão: os metadados são campos do formulário e o PNG vai em 'imagem'.
        grupo.MapPost("/versoes", (HttpRequest pedido, ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => ComEscopo(quem, async escopo =>
            {
                if (!pedido.HasFormContentType)
                    return Results.Problem("Envie a versão como multipart/form-data: os metadados e o PNG no campo 'imagem'.", statusCode: StatusCodes.Status400BadRequest);
                var formulario = await pedido.ReadFormAsync(ct);
                var (envio, erro) = Envio(formulario);
                var png = await PngAsync(formulario.Files.GetFile("imagem"), ct);
                var problema = erro.Length > 0 ? erro : RegrasDoDesign.Validar(envio, png);
                if (problema.Length > 0)
                    return Results.Problem(problema, statusCode: png.Length > DesignV1.TamanhoMaximoDaImagem
                        ? StatusCodes.Status413PayloadTooLarge
                        : StatusCodes.Status400BadRequest);

                var feito = await design.EnviarAsync(escopo, envio!, png, ct);
                return feito.Situacao switch
                {
                    SituacaoDoEnvio.Nova => Results.Created(feito.Versao!.Versao.Link, feito.Versao),
                    SituacaoDoEnvio.Repetida => Results.Ok(feito.Versao),
                    SituacaoDoEnvio.Conflito => Results.Problem(feito.Mensagem, statusCode: StatusCodes.Status409Conflict),
                    SituacaoDoEnvio.Teto => Results.Problem(feito.Mensagem, statusCode: StatusCodes.Status413PayloadTooLarge),
                    _ => Results.Problem(feito.Mensagem, statusCode: StatusCodes.Status400BadRequest),
                };
            })).RequireRateLimiting(LimiteDeTaxa.PoliticaDoDesign);

        // O LINK ESTÁVEL da versão: a imagem é imutável (o número não muda de PNG), então o ETag é o hash.
        grupo.MapGet("/{projeto:long}/{tela}/v{numero:int}", (long projeto, string tela, int numero, HttpContext http,
                ClaimsPrincipal quem, IDesignDaEmpresa design, CancellationToken ct)
            => ComEscopo(quem, async escopo =>
            {
                if (await design.ImagemAsync(escopo, projeto, tela, numero, ct) is not { } imagem)
                    return Results.Problem("Versão de design não encontrada.", statusCode: StatusCodes.Status404NotFound);
                http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
                return Results.File(imagem.Conteudo, "image/png", $"{tela}-v{numero:D3}.png",
                    lastModified: imagem.CriadaEm, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{imagem.Sha256}\""));
            }));

        grupo.MapDelete("/{projeto:long}/{tela}/v{numero:int}", (long projeto, string tela, int numero, ClaimsPrincipal quem,
                IDesignDaEmpresa design, CancellationToken ct)
            => ComEscopo(quem, async escopo => EndpointsDaUs425.Resultado(await design.ApagarAsync(escopo, projeto, tela, numero, ct))));
    }

    /// <summary>A ação com o escopo do design (qualquer usuário autenticado, com ou sem empresa); token torto, 403.</summary>
    private static Task<IResult> ComEscopo(ClaimsPrincipal quem, Func<EscopoDoDesign, Task<IResult>> acao)
        => Seguranca.EscopoDoDesign(quem) is { } escopo ? acao(escopo) : Task.FromResult(Results.Forbid());

    /// <summary>Os metadados do formulário; o erro de um número ou data ilegível vem à parte.</summary>
    private static (EnvioDeVersaoDeDesign? Envio, string Erro) Envio(IFormCollection f)
    {
        string? Campo(string nome) => f.TryGetValue(nome, out var valor) && !string.IsNullOrWhiteSpace(valor) ? valor.ToString() : null;

        int? numero = null, basis = null;
        DateTimeOffset? criadaEm = null;
        if (Campo("numero") is { } n)
        {
            if (!int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var lido))
                return (null, "O número da versão é um inteiro positivo.");
            numero = lido;
        }

        if (Campo("base") is { } b)
        {
            if (!int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var lido))
                return (null, "A base é o número de uma versão anterior.");
            basis = lido;
        }

        if (Campo("criadaEm") is { } c)
        {
            if (!DateTimeOffset.TryParse(c, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var lida))
                return (null, "A data da versão (criadaEm) vai em ISO 8601.");
            criadaEm = lida;
        }

        return (new EnvioDeVersaoDeDesign(
            Campo("repositorio"), Campo("nivel"), Campo("time"), Campo("tela"), Campo("nome"),
            f.TryGetValue("estado", out var estado) ? estado.ToString() : null, numero, Campo("tipo"), basis, Campo("tema"),
            Campo("origem"), Campo("alvo"), Campo("branch"), Campo("commit"), Campo("descricao"), Campo("autorTipo"),
            Campo("runner"), criadaEm), string.Empty);
    }

    /// <summary>Os bytes do PNG, com o teto do contrato + 1 (o que passa é recusado sem ler o resto).</summary>
    private static async Task<byte[]> PngAsync(IFormFile? arquivo, CancellationToken ct)
    {
        if (arquivo is null || arquivo.Length == 0)
            return [];
        if (arquivo.Length > DesignV1.TamanhoMaximoDaImagem)
            return new byte[DesignV1.TamanhoMaximoDaImagem + 1];
        using var memoria = new MemoryStream((int)arquivo.Length);
        await arquivo.CopyToAsync(memoria, ct);
        return memoria.ToArray();
    }
}
