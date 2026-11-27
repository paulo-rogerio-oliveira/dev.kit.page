using System.Security.Claims;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Api;

public static class EndpointsDosProjetos
{
    public static void MapearProjetos(this IEndpointRouteBuilder app)
    {
        app.MapearMatrix();
        var grupo = app.MapGroup("/api/projetos").WithTags("Projetos");
        grupo.MapearAcessosDosProjetos();
        grupo.MapGet("/resumos", (string? time, DateTimeOffset? desde, ClaimsPrincipal quem, IConsultaDeProjetos projetos, CancellationToken ct)
            => ComEscopo(quem, async e => Results.Ok(await projetos.ListarResumosAsync(e, time, desde, ct))));
        grupo.MapGet("", (string? time, DateTimeOffset? desde, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e => Results.Ok(await projetos.ListarAsync(e, time, desde, ct))));
        grupo.MapGet("/{id:long}", (long id, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e => await projetos.ObterAsync(e, id, ct) is { } p
                ? Results.Ok(p) : Results.Problem("Projeto não encontrado.", statusCode: 404)));
        grupo.MapGet("/{id:long}/prontidao-disparo", (long id, ClaimsPrincipal quem, IProjetosCompartilhados projetos,
            IConsultaDeProjetos consulta, CancellationToken ct)
            => ComEscopo(quem, async e => await projetos.ObterAsync(e, id, ct) is { } p
                ? Results.Ok(new { pendencia = await consulta.PendenciaDoDisparoAsync(e, p.Slug, ct) })
                : Results.Problem("Projeto não encontrado.", statusCode: 404)));
        grupo.MapPut("", (EnvioDeProjetoV1 envio, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => Gravar(quem, projetos, null, envio, ct));
        grupo.MapPut("/{id:long}", (long id, EnvioDeProjetoV1 envio, ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => Gravar(quem, projetos, id, envio, ct));
        grupo.MapPut("/{id:long}/configuracao-do-pacote", (long id, EdicaoDaConfiguracaoDoPacoteV1 edicao,
            ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e =>
            {
                var atual = await projetos.ObterAsync(e, id, ct);
                if (atual is null) return Results.Problem("Projeto não encontrado.", statusCode: 404);
                if (!RegrasDeAcesso.PodeEditar(atual.Papel))
                    return Results.Problem("Edição exige editor ou dono do projeto.", statusCode: 403);
                if (edicao.VersaoEsperada != atual.Versao)
                    return Results.Problem("O projeto mudou. Releia antes de salvar.", statusCode: 409);
                if (edicao.ConfiguracaoDoPacote is not ("Debug" or "Release"))
                    return Results.Problem("A configuração do pacote deve ser Debug ou Release.", statusCode: 400);
                var envio = new EnvioDeProjetoV1(atual.Slug, atual.Nome, atual.Nivel, atual.Time, atual.Repositorios, atual.Branch,
                    atual.SemanaConvertida, atual.Definicao, atual.Anexos, atual.Autonomia, atual.Paralelismo, atual.DestinoDaPr,
                    atual.Situacao, edicao.VersaoEsperada, atual.Itens, atual.FeatureId, atual.EpicId, atual.Iteracao, atual.Board,
                    atual.BranchesDasTasks, DateTimeOffset.UtcNow, atual.BaseDoBranch, atual.FluxoPadrao, atual.Agente, atual.Modelo,
                    atual.Modelos, atual.PoliticaDeFinalizacao, atual.SkillDoPacote, atual.SkillsDoPacote, edicao.ConfiguracaoDoPacote)
                    { EdicaoPeloBoard = true };
                return await Gravar(quem, projetos, id, envio, ct);
            }));
        grupo.MapPut("/{id:long}/skill-do-pacote", (long id, EdicaoDaSkillDoPacoteV1 edicao,
            ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e =>
            {
                var atual = await projetos.ObterAsync(e, id, ct);
                if (atual is null) return Results.Problem("Projeto não encontrado.", statusCode: 404);
                if (!RegrasDeAcesso.PodeEditar(atual.Papel))
                    return Results.Problem("Edição exige editor ou dono do projeto.", statusCode: 403);
                if (edicao.VersaoEsperada != atual.Versao)
                    return Results.Problem("O projeto mudou. Releia antes de salvar.", statusCode: 409);
                var nome = edicao.SkillDoPacote?.Trim();
                if (nome is null || nome.Length > ProjetosV1.TamanhoMaximoDoNome
                    || nome.Length > 0 && !(atual.SkillsDoPacote?.Any(s => s.Nome == nome) ?? false))
                    return Results.Problem("Skill fora do cadastro sincronizado do host.", statusCode: 400);
                var envio = new EnvioDeProjetoV1(atual.Slug, atual.Nome, atual.Nivel, atual.Time, atual.Repositorios, atual.Branch,
                    atual.SemanaConvertida, atual.Definicao, atual.Anexos, atual.Autonomia, atual.Paralelismo, atual.DestinoDaPr,
                    atual.Situacao, edicao.VersaoEsperada, atual.Itens, atual.FeatureId, atual.EpicId, atual.Iteracao, atual.Board,
                    atual.BranchesDasTasks, DateTimeOffset.UtcNow, atual.BaseDoBranch, atual.FluxoPadrao, atual.Agente, atual.Modelo,
                    atual.Modelos, atual.PoliticaDeFinalizacao, nome, atual.SkillsDoPacote, atual.ConfiguracaoDoPacote) { EdicaoPeloBoard = true };
                return await Gravar(quem, projetos, id, envio, ct);
            }));
        grupo.MapPut("/{id:long}/itens/{numero:int}", (long id, int numero, EdicaoRemotaDoItemV1 edicao,
            ClaimsPrincipal quem, IProjetosCompartilhados projetos, CancellationToken ct)
            => ComEscopo(quem, async e =>
            {
                var atual = await projetos.ObterAsync(e, id, ct);
                if (atual is null) return Results.Problem("Projeto não encontrado.", statusCode: 404);
                if (!RegrasDeAcesso.PodeEditar(atual.Papel))
                    return Results.Problem("Edição exige editor ou dono do projeto.", statusCode: 403);
                if (edicao.VersaoEsperada != atual.Versao)
                    return Results.Problem("O projeto mudou. Rascunho preservado: releia antes de salvar.", statusCode: 409);
                try
                {
                    var envio = EdicaoRemotaDoItem.Preparar(atual, numero, edicao, DateTimeOffset.UtcNow);
                    return await Gravar(quem, projetos, id, envio, ct);
                }
                catch (ArgumentException ex) { return Results.Problem(ex.Message, statusCode: 400); }
            }));
    }

    private static Task<IResult> Gravar(ClaimsPrincipal quem, IProjetosCompartilhados projetos, long? id, EnvioDeProjetoV1 envio, CancellationToken ct)
        => ComEscopo(quem, async e =>
        {
            var r = await projetos.GravarAsync(e, id, envio, ct);
            return r.Status switch
            {
                201 => Results.Created($"/api/projetos/{r.Projeto!.Id}", r.Projeto),
                200 => Results.Ok(r.Projeto),
                409 => Results.Problem(r.Mensagem, statusCode: 409, extensions: new Dictionary<string, object?> { ["atual"] = r.Projeto }),
                _ => Results.Problem(r.Mensagem, statusCode: r.Status),
            };
        });

    private static Task<IResult> ComEscopo(ClaimsPrincipal quem, Func<EscopoDoDesign, Task<IResult>> acao)
        => Seguranca.EscopoDoDesign(quem) is { } e ? acao(e) : Task.FromResult(Results.Forbid());
}
