using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A gestão dos catálogos nomeados pelo admin (US #461). O que decide a situação de cada item é o manifesto da última
/// release (a do site, ou a do GitHub no básico ainda não importado), pela <see cref="PublicacaoDeCatalogos"/> — uma
/// fonte só para "o que as máquinas têm".
/// </summary>
public sealed partial class GestaoDeCatalogos(DevKitPageDb db, PublicacaoDeCatalogos publicacao, IReleasesDoDevKit releases, TimeProvider relogio)
    : IGestaoDeCatalogos
{
    private static readonly AcessoAoCatalogo DoAdmin = new(true, null);

    public async Task<IReadOnlyList<CatalogoV1>> ListarAsync(CancellationToken ct)
    {
        var catalogos = await db.Catalogos.AsNoTracking().OrderByDescending(c => c.Basico).ThenBy(c => c.Nome).ToListAsync(ct);
        var lista = new List<CatalogoV1>(catalogos.Count);
        foreach (var catalogo in catalogos)
            lista.Add(await ResumoAsync(catalogo, ct));
        return lista;
    }

    public async Task<ResultadoDaGestao<CatalogoV1>> CriarAsync(CatalogoNovoV1 novo, CancellationToken ct)
    {
        var slug = novo.Slug.Trim();
        if (await db.Catalogos.AnyAsync(c => c.Slug == slug, ct))
            return ResultadoDaGestao<CatalogoV1>.Falhou(FalhaNaGestao.Conflito, $"Já existe um catálogo com o slug '{slug}'.");

        var catalogo = new Catalogo
        {
            Slug = slug, Nome = novo.Nome.Trim(), Descricao = (novo.Descricao ?? string.Empty).Trim(), CriadoEmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        db.Catalogos.Add(catalogo);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ResultadoDaGestao<CatalogoV1>.Falhou(FalhaNaGestao.Conflito, $"Já existe um catálogo com o slug '{slug}'.");
        }

        return new(await ResumoAsync(catalogo, ct));
    }

    public async Task<ResultadoDaGestao<CatalogoV1>> EditarAsync(string slug, CatalogoEditadoV1 edicao, CancellationToken ct)
    {
        if (await db.Catalogos.FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<CatalogoV1>();
        catalogo.Nome = edicao.Nome.Trim();
        catalogo.Descricao = (edicao.Descricao ?? string.Empty).Trim();
        await db.SaveChangesAsync(ct);
        return new(await ResumoAsync(catalogo, ct));
    }

    public async Task<ResultadoDaGestao<CatalogoV1>> ExcluirAsync(string slug, CancellationToken ct)
    {
        if (await db.Catalogos.FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<CatalogoV1>();
        if (catalogo.Basico)
            return ResultadoDaGestao<CatalogoV1>.Falhou(FalhaNaGestao.Recusada, "O catálogo básico não se exclui: ele vale para todas as máquinas.");

        var resumo = await ResumoAsync(catalogo, ct);
        db.Catalogos.Remove(catalogo);
        await db.SaveChangesAsync(ct);
        return new(resumo);
    }

    public async Task<ResultadoDaGestao<CatalogoV1>> RelacionarAsync(string slug, int empresaId, CancellationToken ct)
    {
        if (await db.Catalogos.FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<CatalogoV1>();
        if (catalogo.Basico)
            return ResultadoDaGestao<CatalogoV1>.Falhou(FalhaNaGestao.Recusada, "O catálogo básico vale para todas as empresas: não se relaciona.");
        if (!await db.Empresas.AnyAsync(e => e.Id == empresaId, ct))
            return ResultadoDaGestao<CatalogoV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Empresa não encontrada.");
        if (await db.VinculosDeCatalogo.AnyAsync(v => v.CatalogoId == catalogo.Id && v.EmpresaId == empresaId, ct))
            return new(await ResumoAsync(catalogo, ct));

        // Na máquina o item é tipo + nome: dois catálogos da mesma empresa com o mesmo item brigariam por ele.
        var colisoes = RegrasDosCatalogos.Colisoes(await ChavesAsync(catalogo.Id, ct), await OutrosDaEmpresaAsync(empresaId, catalogo.Id, ct));
        if (colisoes.Count > 0)
            return ResultadoDaGestao<CatalogoV1>.Falhou(FalhaNaGestao.Conflito,
                $"A empresa já recebe outro catálogo com o mesmo item: {string.Join("; ", colisoes)}. Renomeie ou retire o item antes de relacionar.");

        db.VinculosDeCatalogo.Add(new VinculoDeCatalogo { CatalogoId = catalogo.Id, EmpresaId = empresaId, RelacionadoEmUtc = relogio.GetUtcNow().UtcDateTime });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Relacionar duas vezes ao mesmo tempo: o índice único guarda um — e o resultado é o mesmo.
        }

        return new(await ResumoAsync(catalogo, ct));
    }

    public async Task<ResultadoDaGestao<CatalogoV1>> DesrelacionarAsync(string slug, int empresaId, CancellationToken ct)
    {
        if (await db.Catalogos.FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<CatalogoV1>();
        await db.VinculosDeCatalogo.Where(v => v.CatalogoId == catalogo.Id && v.EmpresaId == empresaId).ExecuteDeleteAsync(ct);
        return new(await ResumoAsync(catalogo, ct));
    }

    public async Task<ResultadoDaGestao<IReadOnlyList<ItemEditavelV1>>> ItensAsync(string slug, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<IReadOnlyList<ItemEditavelV1>>();
        var publicados = await PublicadosAsync(catalogo, ct);
        var itens = await db.ItensEditaveis.AsNoTracking().Where(i => i.CatalogoId == catalogo.Id)
            .OrderBy(i => i.Tipo).ThenBy(i => i.Nome)
            .ToListAsync(ct);
        // O conteúdo vem do banco (o escopo e a stack saem dele, US #687), mas não vai na lista.
        return new(itens.Select(i => Editavel(i, publicados, catalogo, comConteudo: false)).ToArray());
    }

    public async Task<ResultadoDaGestao<ItemEditavelV1>> ItemAsync(string slug, long id, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<ItemEditavelV1>();
        if (await db.ItensEditaveis.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.CatalogoId == catalogo.Id, ct) is not { } item)
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Item não encontrado.");
        return new(Editavel(item, await PublicadosAsync(catalogo, ct), catalogo, comConteudo: true));
    }

    public async Task<ResultadoDaGestao<ItemEditavelV1>> SalvarItemAsync(
        string slug, long? id, ItemConferido conferido, string? categoria, string autor, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<ItemEditavelV1>();
        // A Rule de impacto (US #687) é do negócio do cliente: o básico vai a toda máquina, e ela nunca entra nele.
        if (catalogo.Basico && RegrasDosCatalogos.RuleDeImpacto(conferido.Tipo, conferido.Conteudo))
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.Recusada, RegrasDosCatalogos.ImpactoNoBasico);
        var chave = RegrasDoCatalogoDaEmpresa.Chave(conferido.Tipo, conferido.Nome);
        if (!catalogo.Basico && (await publicacao.ChavesDoBasicoAsync(ct)).Contains(chave))
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.Conflito, $"Já existe '{conferido.Nome}' no catálogo básico do dev.kit: publique com outro nome.");
        if (!catalogo.Basico && await ColisaoNasEmpresasAsync(catalogo.Id, chave, ct) is { } colisao)
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.Conflito, colisao);

        ItemDoCatalogoEditavel? item;
        if (id is { } existente)
        {
            item = await db.ItensEditaveis.FirstOrDefaultAsync(i => i.Id == existente && i.CatalogoId == catalogo.Id, ct);
            if (item is null)
                return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Item não encontrado.");
            if (item.Tipo != conferido.Tipo || item.Nome != conferido.Nome)
                return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.Recusada,
                    $"O nome do item não muda na edição ('{item.Nome}' é a identidade dele nas máquinas): crie um item novo e retire este.");
        }
        else
        {
            item = await db.ItensEditaveis.FirstOrDefaultAsync(i => i.CatalogoId == catalogo.Id && i.Tipo == conferido.Tipo && i.Nome == conferido.Nome, ct);
            if (item is { RetiradoEmUtc: null })
                return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.Conflito, $"Já existe {conferido.Tipo} '{conferido.Nome}' neste catálogo: edite o item.");
            if (item is null)
            {
                // O retirado volta pelo mesmo registro (a numeração do manifesto continua); o novo nasce na pasta do tipo.
                item = new ItemDoCatalogoEditavel
                {
                    CatalogoId = catalogo.Id, Tipo = conferido.Tipo, Nome = conferido.Nome, Arquivo = RegrasDosCatalogos.Arquivo(conferido.Tipo, conferido.Nome),
                };
                db.ItensEditaveis.Add(item);
            }
        }

        item.Descricao = conferido.Descricao;
        item.Categoria = (categoria ?? string.Empty).Trim();
        item.Conteudo = conferido.Conteudo;
        item.Hash = conferido.Hash;
        item.Dependencias = string.Join('\n', conferido.Dependencias);
        item.AlteradoEmUtc = relogio.GetUtcNow().UtcDateTime;
        item.AlteradoPor = Autor(autor);
        item.RetiradoEmUtc = null;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.Conflito, $"'{conferido.Nome}' acabou de ser gravado por outra pessoa: recarregue e edite de novo.");
        }

        var publicados = await PublicadosAsync(catalogo, ct);
        await RegistrarAsync(item.Id, AcoesNoItem.Rascunho, publicados.GetValueOrDefault((item.Tipo, item.Nome))?.Versao ?? 0, autor, ct);
        return new(Editavel(item, publicados, catalogo, comConteudo: true));
    }

    public async Task<ResultadoDaGestao<ItemEditavelV1>> RetirarItemAsync(string slug, long id, string autor, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<ItemEditavelV1>();
        if (await db.ItensEditaveis.FirstOrDefaultAsync(i => i.Id == id && i.CatalogoId == catalogo.Id, ct) is not { } item)
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Item não encontrado.");

        var publicados = await PublicadosAsync(catalogo, ct);
        if (!publicados.ContainsKey((item.Tipo, item.Nome)))
        {
            // Nunca chegou a uma release: não há o que tirar das máquinas — o rascunho some.
            db.ItensEditaveis.Remove(item);
            await db.SaveChangesAsync(ct);
            return new(null);
        }

        item.RetiradoEmUtc ??= relogio.GetUtcNow().UtcDateTime;
        item.AlteradoEmUtc = relogio.GetUtcNow().UtcDateTime;
        item.AlteradoPor = Autor(autor);
        await db.SaveChangesAsync(ct);
        await RegistrarAsync(item.Id, AcoesNoItem.Retirado, publicados[(item.Tipo, item.Nome)].Versao, autor, ct);
        return new(Editavel(item, publicados, await db.Catalogos.AsNoTracking().FirstAsync(c => c.Id == item.CatalogoId, ct), comConteudo: false));
    }

    public async Task<ImportacaoDoBasicoV1> ImportarBasicoAsync(string autor, CancellationToken ct)
    {
        var basico = await db.Catalogos.FirstAsync(c => c.Basico, ct);
        if (await db.VersoesDoCatalogo.AnyAsync(v => v.CatalogoId == basico.Id, ct) || await db.ItensEditaveis.AnyAsync(i => i.CatalogoId == basico.Id, ct))
            return new ImportacaoDoBasicoV1(0, 0, 0, false, "O catálogo básico já está no site: a importação é feita uma vez só.");
        if (await releases.CatalogosAsync(ct) is not { } lista)
            return new ImportacaoDoBasicoV1(0, 0, 0, false, "As releases do catálogo no GitHub estão indisponíveis agora. Tente de novo em alguns minutos.");
        if (lista.Count == 0)
            return new ImportacaoDoBasicoV1(0, 0, 0, false, "Não há release catalogo-vN no GitHub para importar.");
        var release = lista[0];
        if (await releases.ManifestoAsync(release, ct) is not { } manifesto)
            return new ImportacaoDoBasicoV1(release.Versao, 0, 0, false, $"O manifesto da release {release.Tag} não pôde ser lido.");

        byte[] zip;
        await using (var pacote = await releases.AbrirPacoteAsync(release, ct))
        {
            if (pacote is null)
                return new ImportacaoDoBasicoV1(release.Versao, 0, 0, false, $"O zip da release {release.Tag} não pôde ser baixado.");
            using var memoria = new MemoryStream();
            await pacote.Conteudo.CopyToAsync(memoria, ct);
            zip = memoria.ToArray();
        }

        var (itens, problema) = ItensDoPacote(zip, manifesto);
        if (problema.Length > 0)
            return new ImportacaoDoBasicoV1(release.Versao, 0, 0, false, $"A release {release.Tag} não foi importada: {problema}");

        var agora = relogio.GetUtcNow().UtcDateTime;
        foreach (var item in itens)
        {
            item.CatalogoId = basico.Id;
            item.AlteradoEmUtc = agora;
            item.AlteradoPor = Autor(autor);
            db.ItensEditaveis.Add(item);
        }

        // A release importada É a do GitHub (o mesmo zip, o mesmo manifesto): as máquinas na versão N seguem em dia, e a
        // próxima publicação do site é a N+1.
        var versao = new VersaoDoCatalogo
        {
            CatalogoId = basico.Id, Versao = release.Versao, Tag = release.Tag, Manifesto = ManifestoJson.Serializar(manifesto),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(zip)), TamanhoBytes = zip.LongLength, Itens = manifesto.Itens.Count,
            Retirados = manifesto.Retirados.Count, PublicadaEmUtc = release.PublicadaEm.UtcDateTime, PublicadaPor = Autor($"{autor} (importada do GitHub)"),
        };
        db.VersoesDoCatalogo.Add(versao);
        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        db.PacotesDoCatalogo.Add(new PacoteDoCatalogo { VersaoId = versao.Id, Zip = zip });
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new ImportacaoDoBasicoV1(release.Versao, itens.Count, manifesto.Retirados.Count, true,
            $"Release {release.Tag} importada: {itens.Count} itens. A próxima publicação do básico será a catalogo-v{release.Versao + 1}.");
    }

    /// <summary>
    /// Os itens do zip da release, cada um conferido pela regra do site — e com o hash IGUAL ao do manifesto do git.kit: se a
    /// regra portada divergir, a importação para aqui, em vez de publicar depois uma "versão nova" de tudo.
    /// </summary>
    internal static (List<ItemDoCatalogoEditavel> Itens, string Problema) ItensDoPacote(byte[] zip, ManifestoDoCatalogoV1 manifesto)
    {
        Dictionary<string, string> arquivos;
        try
        {
            using var pacote = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
            arquivos = pacote.Entries.Where(e => e.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(e => e.FullName.Replace('\\', '/'), e =>
                {
                    using var leitor = new StreamReader(e.Open(), Encoding.UTF8);
                    return leitor.ReadToEnd();
                }, StringComparer.OrdinalIgnoreCase);
        }
        catch (InvalidDataException)
        {
            return ([], "o zip não abre.");
        }

        return ItensDosArquivos(arquivos, manifesto);
    }

    internal static (List<ItemDoCatalogoEditavel> Itens, string Problema) ItensDosArquivos(
        IReadOnlyDictionary<string, string> arquivos, ManifestoDoCatalogoV1 manifesto)
    {
        var itens = new List<ItemDoCatalogoEditavel>();
        foreach (var publicado in manifesto.Itens)
        {
            if (string.IsNullOrEmpty(publicado.Arquivo) || publicado.Arquivo.Length > RegrasDosCatalogos.TamanhoMaximoDoArquivo)
                return ([], $"O arquivo do item tem até {RegrasDosCatalogos.TamanhoMaximoDoArquivo} caracteres.");
            if (!arquivos.TryGetValue(publicado.Arquivo, out var texto))
                return ([], $"{publicado.Arquivo} não está no zip.");
            var categoria = (publicado.Tecnologia ?? string.Empty).Trim();
            if (categoria == GeradorDoManifesto.Tecnologia(publicado.Tipo, publicado.Nome))
                categoria = string.Empty;
            var (conferido, erro) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(publicado.Tipo, texto, categoria));
            if (conferido is null)
                return ([], $"{publicado.Tipo}:{publicado.Nome}: {erro}");
            if (conferido.Hash != publicado.Hash)
                return ([], $"o hash de {publicado.Tipo}:{publicado.Nome} calculado no site não é o do manifesto — a regra do site divergiu da do git.kit.");

            itens.Add(new ItemDoCatalogoEditavel
            {
                // O fluxo é pelo nome do arquivo (a identidade no git.kit); a skill e o plan, pelo próprio conteúdo.
                Tipo = publicado.Tipo, Nome = publicado.Nome, Descricao = conferido.Descricao, Arquivo = publicado.Arquivo,
                Categoria = categoria,
                Conteudo = conferido.Conteudo, Hash = conferido.Hash, Dependencias = string.Join('\n', conferido.Dependencias),
            });
        }

        return (itens, string.Empty);
    }

    private async Task<CatalogoV1> ResumoAsync(Catalogo catalogo, CancellationToken ct)
    {
        var ultima = await db.VersoesDoCatalogo.AsNoTracking().Where(v => v.CatalogoId == catalogo.Id).OrderByDescending(v => v.Versao)
            .Select(v => new { v.Versao, v.PublicadaEmUtc }).FirstOrDefaultAsync(ct);
        var publicados = await PublicadosAsync(catalogo, ct);
        var itens = await db.ItensEditaveis.AsNoTracking().Where(i => i.CatalogoId == catalogo.Id)
            .Select(i => new { i.Tipo, i.Nome, i.Hash, i.Descricao, i.Categoria, i.Arquivo, i.RetiradoEmUtc })
            .ToListAsync(ct);
        var pendentes = itens.Count(i => RegrasDosCatalogos.Situacao(i.Tipo, i.Nome, i.Hash, i.Descricao, i.Categoria, i.Arquivo, i.RetiradoEmUtc is not null,
            publicados.GetValueOrDefault((i.Tipo, i.Nome))).Situacao is SituacoesDoItem.Novo or SituacoesDoItem.Alterado or SituacoesDoItem.RetiradaPendente);
        var empresas = await db.VinculosDeCatalogo.AsNoTracking().Where(v => v.CatalogoId == catalogo.Id)
            .OrderBy(v => v.Empresa!.Nome)
            .Select(v => new { v.EmpresaId, v.Empresa!.Nome, v.RelacionadoEmUtc })
            .ToListAsync(ct);

        return new CatalogoV1(
            catalogo.Id, catalogo.Slug, catalogo.Nome, catalogo.Descricao, catalogo.Basico, ultima?.Versao ?? 0,
            ultima is null ? null : DevKitPageDb.Utc(ultima.PublicadaEmUtc), itens.Count(i => i.RetiradoEmUtc is null), pendentes,
            empresas.Select(e => new EmpresaVinculadaV1(e.EmpresaId, e.Nome, DevKitPageDb.Utc(e.RelacionadoEmUtc))).ToArray(),
            DevKitPageDb.Utc(catalogo.CriadoEmUtc), catalogo.Origem);
    }

    /// <summary>Os itens da última release do catálogo, por tipo e nome (vazio sem release).</summary>
    private async Task<Dictionary<(string Tipo, string Nome), ItemDoCatalogoV1>> PublicadosAsync(Catalogo catalogo, CancellationToken ct)
        => (await publicacao.UltimaAsync(catalogo.Slug, DoAdmin, ct))?.Manifesto.Itens.ToDictionary(i => (i.Tipo, i.Nome)) ?? [];

    /// <summary>As chaves dos itens ativos de um catálogo.</summary>
    private async Task<IReadOnlySet<string>> ChavesAsync(int catalogoId, CancellationToken ct)
        => (await db.ItensEditaveis.AsNoTracking().Where(i => i.CatalogoId == catalogoId && i.RetiradoEmUtc == null)
                .Select(i => new { i.Tipo, i.Nome }).ToListAsync(ct))
            .Select(i => RegrasDoCatalogoDaEmpresa.Chave(i.Tipo, i.Nome)).ToHashSet(StringComparer.Ordinal);

    /// <summary>Os OUTROS catálogos relacionados à empresa, com as chaves dos itens ativos de cada um.</summary>
    private async Task<IReadOnlyList<(string Catalogo, IReadOnlySet<string> Itens)>> OutrosDaEmpresaAsync(int empresaId, int exceto, CancellationToken ct)
    {
        var outros = await db.VinculosDeCatalogo.AsNoTracking().Where(v => v.EmpresaId == empresaId && v.CatalogoId != exceto)
            .Select(v => new { v.CatalogoId, v.Catalogo!.Nome }).ToListAsync(ct);
        var lista = new List<(string, IReadOnlySet<string>)>(outros.Count);
        foreach (var outro in outros)
            lista.Add((outro.Nome, await ChavesAsync(outro.CatalogoId, ct)));
        return lista;
    }

    /// <summary>A colisão do item novo com outro catálogo de alguma empresa já relacionada a este; nula quando não há.</summary>
    private async Task<string?> ColisaoNasEmpresasAsync(int catalogoId, string chave, CancellationToken ct)
    {
        var empresas = await db.VinculosDeCatalogo.AsNoTracking().Where(v => v.CatalogoId == catalogoId).Select(v => v.EmpresaId).ToListAsync(ct);
        foreach (var empresa in empresas)
        {
            var colisoes = RegrasDosCatalogos.Colisoes(new HashSet<string>(StringComparer.Ordinal) { chave }, await OutrosDaEmpresaAsync(empresa, catalogoId, ct));
            if (colisoes.Count > 0)
                return $"Uma empresa deste catálogo já recebe o mesmo item por outro catálogo: {string.Join("; ", colisoes)}.";
        }

        return null;
    }

    private static ItemEditavelV1 Editavel(
        ItemDoCatalogoEditavel i, IReadOnlyDictionary<(string, string), ItemDoCatalogoV1> publicados, Catalogo catalogo, bool comConteudo)
    {
        var (situacao, versao) = RegrasDosCatalogos.Situacao(i.Tipo, i.Nome, i.Hash, i.Descricao, i.Categoria, i.Arquivo, i.RetiradoEmUtc is not null,
            publicados.GetValueOrDefault((i.Tipo, i.Nome)));
        return new ItemEditavelV1(
            i.Id, i.Tipo, i.Nome, i.Descricao, RegrasDosCatalogos.Tecnologia(i.Tipo, i.Nome, i.Categoria), i.Hash, versao, situacao,
            DevKitPageDb.Utc(i.AlteradoEmUtc), i.AlteradoPor, i.Dependencias.Split('\n', StringSplitOptions.RemoveEmptyEntries), comConteudo ? i.Conteudo : null,
            catalogo.Slug, RegrasDosCatalogos.Escopo(catalogo.Basico, i.Tipo, i.Conteudo), RegrasDosCatalogos.Stack(i.Tipo, i.Conteudo),
            RegrasDosCatalogos.RuleDeImpacto(i.Tipo, i.Conteudo));
    }

    private static ResultadoDaGestao<T> NaoEncontrado<T>() where T : class
        => ResultadoDaGestao<T>.Falhou(FalhaNaGestao.NaoEncontrado, "Catálogo não encontrado.");

    private static string Autor(string autor) => autor.Length <= 100 ? autor : autor[..100];
}
