using System.Security.Cryptography;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// Os artefatos dos projetos (controle-v4, item 2) no banco e no armazém. Gravar exige editor ou dono do projeto; ler, só vê-lo.
/// A cada registro a RETENÇÃO roda no projeto: o zip vencido e o que passa de <see cref="OpcoesDosArtefatos.MaximoPorProjeto"/>
/// saem do armazém e do banco. O link de download é gerado na leitura, com o prazo de <see cref="OpcoesDosArtefatos.LinkHoras"/>
/// e nunca além da retenção do zip.
/// </summary>
public sealed class ArtefatosDosProjetosNoBanco(DevKitPageDb db, IArmazemDeArtefatos armazem, IOptions<OpcoesDosArtefatos> opcoes,
    TimeProvider relogio) : IArtefatosDosProjetos
{
    public const string CodigoDoTeto = "teto";

    public async Task<ResultadoDaGestao<ArtefatoV1>> RegistrarPacoteAsync(EscopoDoDesign escopo, long projeto, PacoteRecebido pacote, CancellationToken ct)
    {
        var (alvo, recusa) = await ParaGravarAsync(escopo, projeto, ct);
        if (alvo is null)
            return recusa!;
        var problema = RegrasDosArtefatos.ValidarPacote(pacote.Repositorio, pacote.Sha, pacote.HashInformado);
        if (problema.Length > 0)
            return ResultadoDaGestao<ArtefatoV1>.Falhou(FalhaNaGestao.Recusada, problema);
        var cfg = opcoes.Value;
        if (pacote.Tamanho <= 0)
            return ResultadoDaGestao<ArtefatoV1>.Falhou(FalhaNaGestao.Recusada, "Envie o zip do artefato no campo 'arquivo'.");
        if (pacote.Tamanho > cfg.TetoEmBytes)
            return ResultadoDaGestao<ArtefatoV1>.Falhou(FalhaNaGestao.Recusada, $"O zip passa do teto de {cfg.TamanhoMaximoMb} MB.", CodigoDoTeto);

        string hash;
        await using (var leitura = File.OpenRead(pacote.Arquivo))
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(leitura, ct));
        if (pacote.HashInformado is { Length: > 0 } informado && !string.Equals(informado, hash, StringComparison.OrdinalIgnoreCase))
            return ResultadoDaGestao<ArtefatoV1>.Falhou(FalhaNaGestao.Recusada, "O SHA-256 do zip recebido não confere com o informado: o envio se corrompeu, gere de novo.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        var nome = RegrasDosArtefatos.NomeDoArquivo(pacote.NomeDoArquivo);
        var chave = $"projetos/{alvo.Id}/{Guid.NewGuid():N}.zip";
        await using (var conteudo = File.OpenRead(pacote.Arquivo))
            await armazem.GravarAsync(chave, conteudo, ct);
        var artefato = new ArtefatoDoProjeto
        {
            ProjetoId = alvo.Id, Tipo = ArtefatosV1.TipoPacote, Repositorio = pacote.Repositorio!.Trim(), Sha = pacote.Sha!.Trim().ToLowerInvariant(),
            Chave = chave, NomeDoArquivo = nome, Hash = hash, Tamanho = pacote.Tamanho, AutorId = escopo.UsuarioId, CriadoEmUtc = agora,
            RetidoAteUtc = agora.AddDays(Math.Max(1, cfg.RetencaoDias)),
        };
        return await GravarAsync(artefato, ct);
    }

    public async Task<ResultadoDaGestao<ArtefatoV1>> RegistrarPreviewAsync(EscopoDoDesign escopo, long projeto, PreviewDoProjetoV1 preview, CancellationToken ct)
    {
        var (alvo, recusa) = await ParaGravarAsync(escopo, projeto, ct);
        if (alvo is null)
            return recusa!;
        var problema = RegrasDosArtefatos.ValidarPreview(preview);
        if (problema.Length > 0)
            return ResultadoDaGestao<ArtefatoV1>.Falhou(FalhaNaGestao.Recusada, problema);
        var artefato = new ArtefatoDoProjeto
        {
            ProjetoId = alvo.Id, Tipo = ArtefatosV1.TipoPreview, Repositorio = preview.Repositorio!.Trim(), Sha = preview.Sha!.Trim().ToLowerInvariant(),
            Url = preview.Url!.Trim(), Ambiente = preview.Ambiente!.Trim(), AutorId = escopo.UsuarioId, CriadoEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        return await GravarAsync(artefato, ct);
    }

    public async Task<ResultadoDaGestao<ArtefatoV1[]>> ListarAsync(EscopoDoDesign escopo, long projeto, CancellationToken ct)
    {
        if (!await db.ProjetosCompartilhados.AsNoTracking().Visiveis(escopo).AnyAsync(p => p.Id == projeto, ct))
            return ResultadoDaGestao<ArtefatoV1[]>.Falhou(FalhaNaGestao.NaoEncontrado, "Projeto não encontrado.");
        var agora = relogio.GetUtcNow().UtcDateTime;
        var linhas = await Vigentes(agora).Where(a => a.ProjetoId == projeto).OrderByDescending(a => a.CriadoEmUtc).ThenByDescending(a => a.Id)
            .Take(Math.Max(1, opcoes.Value.MaximoPorProjeto)).ToListAsync(ct);
        return new(await LeituraAsync(linhas, ct));
    }

    public async Task<IReadOnlyList<ArtefatosDoProjetoV1>> RecentesAsync(EscopoDoDesign escopo, CancellationToken ct)
    {
        var projetos = await db.ProjetosCompartilhados.AsNoTracking().Visiveis(escopo).Select(p => new { p.Id, p.Slug, p.Nome }).ToListAsync(ct);
        if (projetos.Count == 0)
            return [];
        var ids = projetos.Select(p => p.Id).ToArray();
        var agora = relogio.GetUtcNow().UtcDateTime;
        var linhas = await Vigentes(agora).Where(a => ids.Contains(a.ProjetoId)).ToListAsync(ct);
        var ultimos = linhas.GroupBy(a => a.ProjetoId)
            .SelectMany(g => g.OrderByDescending(a => a.CriadoEmUtc).ThenByDescending(a => a.Id).Take(ArtefatosV1.UltimosNoCard)).ToList();
        var lidos = (await LeituraAsync(ultimos, ct)).ToLookup(a => a.ProjetoId);
        return projetos.Where(p => lidos.Contains(p.Id)).OrderBy(p => p.Slug)
            .Select(p => new ArtefatosDoProjetoV1(p.Id, p.Slug, p.Nome, lidos[p.Id].ToArray())).ToArray();
    }

    /// <summary>O projeto que o escopo pode gravar, ou a recusa: 404 sem revelar o que não vê, 403 para o leitor.</summary>
    private async Task<(ProjetoCompartilhado? Projeto, ResultadoDaGestao<ArtefatoV1>? Recusa)> ParaGravarAsync(EscopoDoDesign escopo, long id, CancellationToken ct)
    {
        var projeto = await db.ProjetosCompartilhados.AsNoTracking().Include(p => p.Acessos).Visiveis(escopo).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (projeto is null)
            return (null, ResultadoDaGestao<ArtefatoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Projeto não encontrado."));
        var papel = RegrasDeAcesso.Papel(escopo, projeto);
        return RegrasDeAcesso.PodeEditar(papel)
            ? (projeto, null)
            : (null, ResultadoDaGestao<ArtefatoV1>.Falhou(FalhaNaGestao.Proibida,
                $"Gerar artefato do projeto {projeto.Nome} exige editor ou dono: papel atual {(papel.Length == 0 ? "sem acesso" : papel)}."));
    }

    private async Task<ResultadoDaGestao<ArtefatoV1>> GravarAsync(ArtefatoDoProjeto artefato, CancellationToken ct)
    {
        db.ArtefatosDosProjetos.Add(artefato);
        await db.SaveChangesAsync(ct);
        await ReterAsync(artefato.ProjetoId, ct);
        return new((await LeituraAsync([artefato], ct))[0]);
    }

    /// <summary>A retenção do projeto: o zip vencido e o excedente do teto por projeto saem do armazém e do banco.</summary>
    private async Task ReterAsync(long projeto, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var todos = await db.ArtefatosDosProjetos.Where(a => a.ProjetoId == projeto).OrderByDescending(a => a.CriadoEmUtc).ThenByDescending(a => a.Id).ToListAsync(ct);
        var saem = todos.Where(a => a.RetidoAteUtc is { } ate && ate <= agora).Concat(todos.Skip(Math.Max(1, opcoes.Value.MaximoPorProjeto))).Distinct().ToList();
        if (saem.Count == 0)
            return;
        foreach (var velho in saem.Where(a => a.Chave.Length > 0))
            await armazem.ApagarAsync(velho.Chave, ct);
        db.ArtefatosDosProjetos.RemoveRange(saem);
        await db.SaveChangesAsync(ct);
    }

    private IQueryable<ArtefatoDoProjeto> Vigentes(DateTime agora)
        => db.ArtefatosDosProjetos.AsNoTracking().Where(a => a.RetidoAteUtc == null || a.RetidoAteUtc > agora);

    private async Task<ArtefatoV1[]> LeituraAsync(IReadOnlyList<ArtefatoDoProjeto> linhas, CancellationToken ct)
    {
        var autores = linhas.Select(a => a.AutorId).Distinct().ToArray();
        var nomes = await db.Usuarios.AsNoTracking().Where(u => autores.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Nome.Length > 0 ? u.Nome : u.Login, ct);
        var agora = relogio.GetUtcNow();
        return linhas.Select(a =>
        {
            DateTimeOffset? retido = a.RetidoAteUtc is { } r ? DevKitPageDb.Utc(r) : null;
            DateTimeOffset? expira = null;
            var link = a.Url;
            if (a.Tipo == ArtefatosV1.TipoPacote)
            {
                var prazo = agora.AddHours(Math.Max(1, opcoes.Value.LinkHoras));
                expira = retido is { } fim && fim < prazo ? fim : prazo;
                link = armazem.Link(a.Chave, a.NomeDoArquivo, expira.Value);
            }
            return new ArtefatoV1(a.Id, a.ProjetoId, a.Tipo, a.Repositorio, a.Sha, link, expira, a.Hash, a.Tamanho, a.Ambiente,
                nomes.GetValueOrDefault(a.AutorId, string.Empty), DevKitPageDb.Utc(a.CriadoEmUtc), retido);
        }).ToArray();
    }
}
