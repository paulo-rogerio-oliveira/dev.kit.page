using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api;

/// <summary>
/// Os artefatos do projeto (controle-v4, item 2): o dev.kit envia o zip do pacote .NET (multipart) ou registra a URL do preview
/// (JSON) no MESMO <c>POST /api/projetos/{id}/artefatos</c>, e o site devolve o link acessível — o download assinado com prazo,
/// ou o preview. O Board lê os últimos de cada projeto em <c>GET /api/projetos/artefatos</c>. Nunca publica em produção.
/// </summary>
public static class EndpointsDosArtefatos
{
    /// <summary>O teto do JSON do preview.</summary>
    private const int TetoDoPreview = 8 * 1024;

    /// <summary>A folga do multipart sobre o teto do zip (os campos e os separadores).</summary>
    private const long FolgaDoFormulario = 64 * 1024;

    public static void MapearArtefatos(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/projetos").WithTags("Projetos");
        grupo.MapGet("/artefatos", (HttpRequest pedido, ClaimsPrincipal quem, IArtefatosDosProjetos artefatos, CancellationToken ct)
            => ComEscopo(quem, async e => Results.Ok((await artefatos.RecentesAsync(e, ct))
                .Select(p => p with { Artefatos = p.Artefatos.Select(a => Absoluto(pedido, a)).ToArray() }))));
        grupo.MapGet("/{id:long}/artefatos", (long id, HttpRequest pedido, ClaimsPrincipal quem, IArtefatosDosProjetos artefatos, CancellationToken ct)
            => ComEscopo(quem, async e => await artefatos.ListarAsync(e, id, ct) is { Valor: { } lista }
                ? Results.Ok(lista.Select(a => Absoluto(pedido, a)))
                : Results.Problem("Projeto não encontrado.", statusCode: StatusCodes.Status404NotFound)));
        grupo.MapPost("/{id:long}/artefatos", (long id, HttpContext http, ClaimsPrincipal quem, IArtefatosDosProjetos artefatos,
                IOptions<OpcoesDosArtefatos> opcoes, CancellationToken ct)
            => ComEscopo(quem, e => RegistrarAsync(e, id, http, artefatos, opcoes.Value, ct)))
            .DisableAntiforgery().RequireRateLimiting(LimiteDeTaxa.PoliticaDoDesign);

        // O download do armazém em DISCO (desenvolvimento): o token cifrado e com prazo é a autorização, como a SAS no Blob.
        app.MapGet(ArmazemDeArtefatosEmDisco.RotaDoDownload + "{token}", (string token, IArmazemDeArtefatos armazem)
            => armazem is ArmazemDeArtefatosEmDisco disco && disco.Abrir(token) is { } aberto
                ? Results.File(aberto.Arquivo, "application/zip", aberto.Nome)
                : Results.Problem("O link do artefato venceu ou não existe: gere de novo.", statusCode: StatusCodes.Status404NotFound))
            .AllowAnonymous().RequireRateLimiting(LimiteDeTaxa.PoliticaDoDownload).WithTags("Projetos");
    }

    private static async Task<IResult> RegistrarAsync(EscopoDoDesign escopo, long id, HttpContext http, IArtefatosDosProjetos artefatos,
        OpcoesDosArtefatos opcoes, CancellationToken ct)
    {
        var pedido = http.Request;
        if (pedido.HasJsonContentType())
        {
            var (preview, recusa) = await CorpoJson.LerAsync<PreviewDoProjetoV1>(pedido, TetoDoPreview, ct);
            return recusa ?? Criado(pedido, id, await artefatos.RegistrarPreviewAsync(escopo, id, preview!, ct));
        }
        if (!pedido.HasFormContentType)
            return Results.Problem("Envie o zip como multipart/form-data (campo 'arquivo') ou o preview como application/json.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);

        var teto = opcoes.TetoEmBytes;
        if (pedido.ContentLength > teto + FolgaDoFormulario)
            return Grande(opcoes);
        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } corpo)
            corpo.MaxRequestBodySize = teto + FolgaDoFormulario; // o corpo sem Content-Length também para no teto

        IFormCollection formulario;
        try
        {
            formulario = await new FormFeature(pedido, new FormOptions { MultipartBodyLengthLimit = teto + FolgaDoFormulario }).ReadFormAsync(ct);
        }
        catch (InvalidDataException)
        {
            return Grande(opcoes);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return Grande(opcoes);
        }

        var arquivo = formulario.Files.GetFile("arquivo");
        if (arquivo is null || arquivo.Length == 0)
            return Results.Problem("Envie o zip do artefato no campo 'arquivo'.", statusCode: StatusCodes.Status400BadRequest);
        if (arquivo.Length > teto)
            return Grande(opcoes);

        var temporario = Path.Combine(Path.GetTempPath(), "devkitpage-artefato-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using (var destino = File.Create(temporario))
                await arquivo.CopyToAsync(destino, ct);
            var recebido = new PacoteRecebido(formulario["repositorio"].ToString(), formulario["sha"].ToString(),
                formulario["hash"].ToString(), arquivo.FileName, temporario, arquivo.Length);
            return Criado(pedido, id, await artefatos.RegistrarPacoteAsync(escopo, id, recebido, ct));
        }
        finally
        {
            File.Delete(temporario);
        }
    }

    private static IResult Criado(HttpRequest pedido, long id, ResultadoDaGestao<ArtefatoV1> resultado)
    {
        if (resultado.Valor is { } artefato)
            return Results.Created($"/api/projetos/{id}/artefatos/{artefato.Id}", Absoluto(pedido, artefato));
        return resultado.Codigo == ArtefatosDosProjetosNoBanco.CodigoDoTeto
            ? Results.Problem(resultado.Mensagem, statusCode: StatusCodes.Status413PayloadTooLarge)
            : EndpointsDaUs425.Resultado(resultado);
    }

    /// <summary>O link do armazém em disco é relativo à API: o dev.kit e o celular recebem o endereço inteiro.</summary>
    private static ArtefatoV1 Absoluto(HttpRequest pedido, ArtefatoV1 artefato)
        => artefato.Link.StartsWith('/') ? artefato with { Link = $"{pedido.Scheme}://{pedido.Host}{pedido.PathBase}{artefato.Link}" } : artefato;

    private static IResult Grande(OpcoesDosArtefatos opcoes)
        => Results.Problem($"O zip passa do teto de {opcoes.TamanhoMaximoMb} MB.", statusCode: StatusCodes.Status413PayloadTooLarge);

    private static Task<IResult> ComEscopo(ClaimsPrincipal quem, Func<EscopoDoDesign, Task<IResult>> acao)
        => Seguranca.EscopoDoDesign(quem) is { } e ? acao(e) : Task.FromResult(Results.Forbid());
}
