using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A publicação e a distribuição dos catálogos nomeados (US #461). O manifesto sai do <see cref="GeradorDoManifesto"/> (a
/// regra do git.kit), o zip leva TODOS os itens ativos do catálogo — então a dependência resolvida no próprio catálogo
/// vai no mesmo pacote — e a release fica no banco (<see cref="IArmazemDePacotes"/>). O isolamento é UM ponto,
/// <see cref="VisivelAsync"/>: o catálogo que quem pede não pode ver é tratado como inexistente.
/// <para>
/// O BÁSICO sem release no site continua servido pelas releases <c>catalogo-vN</c> do GitHub (<see cref="IReleasesDoDevKit"/>),
/// e a primeira publicação continua a numeração delas — as máquinas não perdem a delta na troca de fonte.
/// </para>
/// </summary>
public sealed class PublicacaoDeCatalogos(DevKitPageDb db, IArmazemDePacotes armazem, IReleasesDoDevKit releases, TimeProvider relogio)
    : IPublicacaoDeCatalogos
{
    public async Task<ResultadoDaGestao<RelatorioDePublicacaoV1>> ConferirAsync(string slug, int? minDevKit, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return ResultadoDaGestao<RelatorioDePublicacaoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Catálogo não encontrado.");
        return new((await PrepararAsync(catalogo, minDevKit, ct)).Relatorio);
    }

    public async Task<(PublicacaoDoCatalogoV1? Feita, RelatorioDePublicacaoV1? Recusa, FalhaNaGestao Falha, string Mensagem)> PublicarAsync(
        string slug, int? minDevKit, string autor, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return (null, null, FalhaNaGestao.NaoEncontrado, "Catálogo não encontrado.");

        var preparo = await PrepararAsync(catalogo, minDevKit, ct);
        if (preparo.Relatorio.Problemas.Count > 0)
            return (null, preparo.Relatorio, FalhaNaGestao.Conflito, "A publicação foi recusada: corrija os problemas do relatório.");
        if (!preparo.Gerado.Mudou)
            return (null, null, FalhaNaGestao.Recusada, $"Nada a publicar: o catálogo continua na versão {preparo.Gerado.Manifesto.Versao}.");

        var manifesto = preparo.Gerado.Manifesto;
        var zip = Empacotar(preparo.Ativos, manifesto);
        var versao = new VersaoDoCatalogo
        {
            CatalogoId = catalogo.Id,
            Versao = manifesto.Versao,
            Tag = Catalogos.Tag(catalogo.Slug, manifesto.Versao),
            Manifesto = ManifestoJson.Serializar(manifesto),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(zip)),
            TamanhoBytes = zip.LongLength,
            Itens = manifesto.Itens.Count,
            Retirados = manifesto.Retirados.Count,
            PublicadaEmUtc = relogio.GetUtcNow().UtcDateTime,
            PublicadaPor = Autor(autor),
        };
        db.VersoesDoCatalogo.Add(versao);
        // A release e o zip juntos: uma release sem pacote seria anunciada às máquinas sem nada para baixar.
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duas publicações ao mesmo tempo: o índice único (catálogo, versão) decide, e a segunda refaz a conferência.
            return (null, null, FalhaNaGestao.Conflito, "Outra publicação deste catálogo acabou de ser gravada: confira e publique de novo.");
        }

        await armazem.GuardarAsync(versao.Id, zip, ct);
        await transacao.CommitAsync(ct);
        return (new PublicacaoDoCatalogoV1(Release(catalogo, versao), preparo.Relatorio), null, FalhaNaGestao.Nenhuma, string.Empty);
    }

    public async Task<IReadOnlyList<CatalogoDisponivelV1>> DisponiveisAsync(AcessoAoCatalogo acesso, CancellationToken ct)
    {
        var consulta = db.Catalogos.AsNoTracking();
        if (!acesso.Admin)
        {
            var empresa = acesso.Escopo?.EmpresaId ?? 0;
            consulta = consulta.Where(c => c.Basico || db.VinculosDeCatalogo.Any(v => v.CatalogoId == c.Id && v.EmpresaId == empresa));
        }

        var catalogos = await consulta.OrderByDescending(c => c.Basico).ThenBy(c => c.Nome).ToListAsync(ct);
        var ids = catalogos.Select(c => c.Id).ToArray();
        var ultimas = (await db.VersoesDoCatalogo.AsNoTracking().Where(v => ids.Contains(v.CatalogoId))
                .Select(v => new { v.CatalogoId, v.Versao, v.PublicadaEmUtc }).ToListAsync(ct))
            .GroupBy(v => v.CatalogoId).ToDictionary(g => g.Key, g => g.MaxBy(v => v.Versao)!);

        var lista = new List<CatalogoDisponivelV1>(catalogos.Count);
        foreach (var c in catalogos)
        {
            if (ultimas.TryGetValue(c.Id, out var ultima))
                lista.Add(new CatalogoDisponivelV1(c.Slug, c.Nome, c.Basico, ultima.Versao, DevKitPageDb.Utc(ultima.PublicadaEmUtc)));
            else if (c.Basico && await DoGitHubAsync(null, ct) is { } doGitHub)
                lista.Add(new CatalogoDisponivelV1(c.Slug, c.Nome, true, doGitHub.Versao, doGitHub.PublicadaEm));
            else
                lista.Add(new CatalogoDisponivelV1(c.Slug, c.Nome, c.Basico, 0, null));
        }

        return lista;
    }

    public async Task<ReleaseServida?> UltimaAsync(string slug, AcessoAoCatalogo acesso, CancellationToken ct)
    {
        if (await VisivelAsync(slug, acesso, ct) is not { } catalogo)
            return null;
        var versao = await db.VersoesDoCatalogo.AsNoTracking().Where(v => v.CatalogoId == catalogo.Id).OrderByDescending(v => v.Versao).FirstOrDefaultAsync(ct);
        if (versao is not null)
            return Servida(catalogo, versao);
        return catalogo.Basico ? await DoGitHubAsync(null, ct) : null;
    }

    public async Task<ReleaseServida?> ReleaseAsync(string slug, int versao, AcessoAoCatalogo acesso, CancellationToken ct)
    {
        if (await VisivelAsync(slug, acesso, ct) is not { } catalogo)
            return null;
        var gravada = await db.VersoesDoCatalogo.AsNoTracking().FirstOrDefaultAsync(v => v.CatalogoId == catalogo.Id && v.Versao == versao, ct);
        if (gravada is not null)
            return Servida(catalogo, gravada);
        return catalogo.Basico ? await DoGitHubAsync(versao, ct) : null;
    }

    public async Task<IReadOnlyList<ReleaseDoCatalogoV1>?> ReleasesAsync(string slug, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return null;
        var lista = (await db.VersoesDoCatalogo.AsNoTracking().Where(v => v.CatalogoId == catalogo.Id).ToListAsync(ct))
            .OrderByDescending(v => v.Versao).Select(v => Release(catalogo, v)).ToList();
        if (!catalogo.Basico)
            return lista;

        // O básico: as releases ANTERIORES à primeira do site continuam no GitHub — o histórico não começa na importação.
        var primeira = lista.Count == 0 ? int.MaxValue : lista.Min(r => r.Versao);
        if (await releases.CatalogosAsync(ct) is not { } doGitHub)
            return lista.Count == 0 ? null : lista;
        foreach (var r in doGitHub.Where(r => r.Versao < primeira))
        {
            var manifesto = await releases.ManifestoAsync(r, ct);
            lista.Add(new ReleaseDoCatalogoV1(r.Versao, r.Tag, r.Nome, r.PublicadaEm, manifesto?.Itens.Count ?? 0, manifesto?.Retirados.Count ?? 0, r.TamanhoBytes, r.Sha256));
        }

        return lista;
    }

    public async Task<PacoteAberto?> AbrirPacoteAsync(ReleaseServida release, CancellationToken ct)
    {
        if (release.VersaoId is { } id)
        {
            if (await armazem.AbrirAsync(id, ct) is not { } zip)
                return null;
            var conteudo = new MemoryStream(zip, writable: false);
            return new PacoteAberto(conteudo, zip.LongLength, conteudo);
        }

        return release.DoGitHub is { } doGitHub ? await releases.AbrirPacoteAsync(doGitHub, ct) : null;
    }

    /// <summary>
    /// O catálogo do slug, se quem pede pode vê-lo — o PONTO ÚNICO do isolamento: o básico é de todos; os demais, do
    /// admin e dos usuários das empresas relacionadas (o <see cref="EscopoDaEmpresa"/>). Fora disso, nulo (a rota
    /// responde 404, e não 403: não se descobre que o catálogo existe).
    /// </summary>
    private async Task<Catalogo?> VisivelAsync(string slug, AcessoAoCatalogo acesso, CancellationToken ct)
    {
        var catalogo = await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct);
        if (catalogo is null || catalogo.Basico || acesso.Admin)
            return catalogo;
        return acesso.Escopo is { } escopo && await db.VinculosDeCatalogo.AnyAsync(v => v.CatalogoId == catalogo.Id && v.EmpresaId == escopo.EmpresaId, ct)
            ? catalogo
            : null;
    }

    /// <summary>O que a publicação faria: os itens ativos, o manifesto gerado e o relatório (mudanças e problemas).</summary>
    private async Task<Preparo> PrepararAsync(Catalogo catalogo, int? minDevKit, CancellationToken ct)
    {
        var anterior = (await UltimaAsync(catalogo.Slug, new AcessoAoCatalogo(true, null), ct))?.Manifesto ?? GeradorDoManifesto.Vazio;
        var ativos = await db.ItensEditaveis.AsNoTracking()
            .Where(i => i.CatalogoId == catalogo.Id && i.RetiradoEmUtc == null)
            .OrderBy(i => i.Tipo).ThenBy(i => i.Nome)
            .ToListAsync(ct);
        var minimo = Math.Max(1, minDevKit ?? (anterior.MinDevKit > 0 ? anterior.MinDevKit : 1));

        var problemas = new List<string>();
        var lidos = new List<GeradorDoManifesto.ItemLido>();
        var paraPublicar = new List<ItemParaPublicar>();
        foreach (var item in ativos)
        {
            try
            {
                lidos.Add(GeradorDoManifesto.Ler(item.Tipo, item.Nome, item.Arquivo, item.Conteudo, item.Categoria));
                var (conferido, erro) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(item.Tipo, item.Conteudo, item.Categoria));
                if (conferido is null)
                    problemas.Add($"{item.Tipo}:{item.Nome}: {erro}");
                else
                    paraPublicar.Add(new ItemParaPublicar(item.Tipo, item.Nome, conferido.Dependencias, conferido.Embutidos));
            }
            catch (FormatException ex)
            {
                problemas.Add($"{item.Tipo}:{item.Nome}: {ex.Message}");
            }
        }

        var basico = catalogo.Basico ? new HashSet<string>(StringComparer.Ordinal) : await ChavesDoBasicoAsync(ct);
        if (!catalogo.Basico)
        {
            foreach (var item in paraPublicar.Where(i => basico.Contains(RegrasDoCatalogoDaEmpresa.Chave(i.Tipo, i.Nome))))
                problemas.Add($"{item.Tipo}:{item.Nome} já existe no catálogo básico do dev.kit: renomeie o item (a máquina não teria como separar os dois).");
        }

        var outros = (await db.ItensEditaveis.AsNoTracking()
                .Where(i => i.CatalogoId != catalogo.Id && i.RetiradoEmUtc == null && !i.Catalogo!.Basico)
                .Select(i => new { i.Tipo, i.Nome, Catalogo = i.Catalogo!.Nome })
                .ToListAsync(ct))
            .GroupBy(i => RegrasDoCatalogoDaEmpresa.Chave(i.Tipo, i.Nome), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Catalogo, StringComparer.Ordinal);
        problemas.AddRange(RegrasDosCatalogos.Dependencias(paraPublicar, basico, outros));

        var gerado = GeradorDoManifesto.Gerar(anterior, lidos, minimo);
        var relatorio = new RelatorioDePublicacaoV1(
            catalogo.Slug, anterior.Versao, anterior.Versao + 1, problemas.Count == 0 && gerado.Mudou, minimo,
            gerado.Mudou ? gerado.Relatorio.Skip(1).ToArray() : [], problemas);
        return new Preparo(ativos, gerado, relatorio);
    }

    /// <summary>As chaves (<c>tipo:nome</c>) do básico: os itens ativos do site ou, antes da importação, os do manifesto do GitHub.</summary>
    internal async Task<HashSet<string>> ChavesDoBasicoAsync(CancellationToken ct)
    {
        var doSite = await db.ItensEditaveis.AsNoTracking()
            .Where(i => i.Catalogo!.Basico && i.RetiradoEmUtc == null)
            .Select(i => new { i.Tipo, i.Nome })
            .ToListAsync(ct);
        if (doSite.Count > 0)
            return doSite.Select(i => RegrasDoCatalogoDaEmpresa.Chave(i.Tipo, i.Nome)).ToHashSet(StringComparer.Ordinal);
        return (await DoGitHubAsync(null, ct))?.Manifesto.Itens.Select(i => RegrasDoCatalogoDaEmpresa.Chave(i.Tipo, i.Nome)).ToHashSet(StringComparer.Ordinal)
               ?? new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>A release do GitHub (a última, ou a da versão) com o manifesto válido; nula quando indisponível.</summary>
    private async Task<ReleaseServida?> DoGitHubAsync(int? versao, CancellationToken ct)
    {
        if (await releases.CatalogosAsync(ct) is not { Count: > 0 } lista)
            return null;
        var release = versao is null ? lista[0] : lista.FirstOrDefault(r => r.Versao == versao);
        if (release is null || await releases.ManifestoAsync(release, ct) is not { } manifesto)
            return null;
        return new ReleaseServida(Catalogos.Basico, release.Versao, release.Tag, release.PublicadaEm, manifesto, release.Sha256, release.TamanhoBytes, null, release);
    }

    /// <summary>
    /// O zip da release: cada item ativo no caminho do manifesto (o mesmo layout do pacote do git.kit: <c>skills/</c>,
    /// <c>plans/</c>, <c>fluxos/</c>) e o <c>manifesto.json</c> na raiz.
    /// </summary>
    internal static byte[] Empacotar(IEnumerable<ItemDoCatalogoEditavel> itens, ManifestoDoCatalogoV1 manifesto)
    {
        var semBom = new UTF8Encoding(false);
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in itens)
                Escrever(zip, item.Arquivo, item.Conteudo, semBom);
            Escrever(zip, "manifesto.json", ManifestoJson.Serializar(manifesto), semBom);
        }

        return memoria.ToArray();
    }

    private static void Escrever(ZipArchive zip, string caminho, string texto, Encoding codificacao)
    {
        using var escrita = new StreamWriter(zip.CreateEntry(caminho, CompressionLevel.Optimal).Open(), codificacao);
        escrita.Write(texto);
    }

    private static ReleaseServida Servida(Catalogo catalogo, VersaoDoCatalogo v)
        => new(catalogo.Slug, v.Versao, v.Tag, DevKitPageDb.Utc(v.PublicadaEmUtc), ManifestoJson.Ler(v.Manifesto), v.Sha256, v.TamanhoBytes, v.Id, null);

    private static ReleaseDoCatalogoV1 Release(Catalogo catalogo, VersaoDoCatalogo v)
        => new(v.Versao, v.Tag, catalogo.Basico ? $"Catálogo {v.Versao}" : $"{catalogo.Nome} {v.Versao}", DevKitPageDb.Utc(v.PublicadaEmUtc),
            v.Itens, v.Retirados, v.TamanhoBytes, v.Sha256);

    private static string Autor(string autor) => autor.Length <= 100 ? autor : autor[..100];

    private sealed record Preparo(IReadOnlyList<ItemDoCatalogoEditavel> Ativos, GeradorDoManifesto.Resultado Gerado, RelatorioDePublicacaoV1 Relatorio);
}

/// <summary>O armazém dos zips no próprio banco (a tabela <c>PacotesDoCatalogo</c>).</summary>
public sealed class ArmazemDePacotesNoBanco(DevKitPageDb db) : IArmazemDePacotes
{
    public async Task GuardarAsync(long versaoId, byte[] zip, CancellationToken ct)
    {
        db.PacotesDoCatalogo.Add(new PacoteDoCatalogo { VersaoId = versaoId, Zip = zip });
        await db.SaveChangesAsync(ct);
    }

    public async Task<byte[]?> AbrirAsync(long versaoId, CancellationToken ct)
        => await db.PacotesDoCatalogo.AsNoTracking().Where(p => p.VersaoId == versaoId).Select(p => p.Zip).FirstOrDefaultAsync(ct);
}
