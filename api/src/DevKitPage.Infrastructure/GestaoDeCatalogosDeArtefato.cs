using System.IO.Compression;
using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A EDIÇÃO DE ARTEFATO do catálogo (US #687): a parte da <see cref="GestaoDeCatalogos"/> que a lista + editor usam — os
/// itens de todos os catálogos com a busca no conteúdo, as diferenças com a versão publicada, o histórico, o descarte do
/// rascunho e os anexos. O que as máquinas recebem não muda: só a release publicada chega a elas (o rascunho nunca), e o
/// manifesto é o do <see cref="GeradorDoManifesto"/>, sem duplicar nada dele.
/// </summary>
public sealed partial class GestaoDeCatalogos
{
    /// <summary>Por quanto tempo os salvamentos seguidos do mesmo autor viram UMA linha de rascunho (o salvar sozinho do editor).</summary>
    public static readonly TimeSpan JanelaDoRascunho = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<ItemEditavelV1>> TodosOsItensAsync(string? busca, string? tipo, string? escopo, CancellationToken ct)
    {
        var catalogos = await db.Catalogos.AsNoTracking().ToDictionaryAsync(c => c.Id, ct);
        var consulta = db.ItensEditaveis.AsNoTracking();
        if (tipo is { Length: > 0 } t)
            consulta = consulta.Where(i => i.Tipo == t);
        var itens = await consulta.OrderBy(i => i.Tipo).ThenBy(i => i.Nome).ToListAsync(ct);

        var termo = (busca ?? string.Empty).Trim();
        var lista = new List<ItemEditavelV1>(itens.Count);
        foreach (var porCatalogo in itens.GroupBy(i => i.CatalogoId))
        {
            var catalogo = catalogos[porCatalogo.Key];
            var publicados = await PublicadosAsync(catalogo, ct);
            foreach (var item in porCatalogo)
            {
                // A busca vale também no CONTEÚDO (o SKILL.md, a Rule, o fluxo): é o que o admin lembra de uma skill.
                if (termo.Length > 0 && !Contem(item.Nome, termo) && !Contem(item.Descricao, termo) && !Contem(item.Conteudo, termo))
                    continue;
                var editavel = Editavel(item, publicados, catalogo, comConteudo: false);
                if (escopo is { Length: > 0 } e && editavel.Escopo != e)
                    continue;
                lista.Add(editavel);
            }
        }

        return lista.OrderBy(i => i.Categoria, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.Nome, StringComparer.Ordinal).ToArray();
    }

    public async Task<ResultadoDaGestao<DiferencasDoItemV1>> DiferencasAsync(string slug, long id, CancellationToken ct)
    {
        if (await ItemDoCatalogoAsync(slug, id, ct) is not { } achado)
            return Falha<DiferencasDoItemV1>(slug, id, ct);

        var (catalogo, item) = achado;
        var publicado = (await PublicadosAsync(catalogo, ct)).GetValueOrDefault((item.Tipo, item.Nome));
        var anterior = publicado is null ? string.Empty
            : await ConteudoPublicadoAsync(catalogo, publicado, ct) is { } texto ? RegrasDosCatalogos.TextoEditavel(item.Tipo, texto) : string.Empty;
        var atual = RegrasDosCatalogos.TextoEditavel(item.Tipo, item.Conteudo);
        var linhas = RegrasDosCatalogos.Diferencas(anterior, atual);
        return new(new DiferencasDoItemV1(publicado?.Versao ?? 0, anterior, atual, linhas,
            linhas.Count(l => l.Tipo == TiposDeLinha.Adicionada), linhas.Count(l => l.Tipo == TiposDeLinha.Removida)));
    }

    public async Task<ResultadoDaGestao<IReadOnlyList<HistoricoDoItemV1>>> HistoricoAsync(string slug, long id, CancellationToken ct)
    {
        if (await ItemDoCatalogoAsync(slug, id, ct) is not { } achado)
            return Falha<IReadOnlyList<HistoricoDoItemV1>>(slug, id, ct);

        var (catalogo, item) = achado;
        var acoes = (await db.HistoricosDeItemDoCatalogo.AsNoTracking().Where(h => h.ItemId == item.Id).ToListAsync(ct))
            .Select(h => new HistoricoDoItemV1(DevKitPageDb.Utc(h.EmUtc), h.Acao, h.Versao, string.Empty, h.Autor));

        // Cada versão do item que uma release do SITE publicou, na primeira release que a trouxe.
        var publicacoes = new List<HistoricoDoItemV1>();
        var versoesVistas = new HashSet<int>();
        foreach (var release in (await db.VersoesDoCatalogo.AsNoTracking().Where(v => v.CatalogoId == catalogo.Id).ToListAsync(ct)).OrderBy(v => v.Versao))
        {
            ManifestoDoCatalogoV1 manifesto;
            try { manifesto = ManifestoJson.Ler(release.Manifesto); }
            catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException) { continue; }
            if (manifesto.Itens.FirstOrDefault(i => i.Tipo == item.Tipo && i.Nome == item.Nome) is { } doItem && versoesVistas.Add(doItem.Versao))
                publicacoes.Add(new HistoricoDoItemV1(DevKitPageDb.Utc(release.PublicadaEmUtc), AcoesNoItem.Publicado, doItem.Versao, release.Tag, release.PublicadaPor));
        }

        return new(acoes.Concat(publicacoes).OrderByDescending(h => h.Em).ToArray());
    }

    public async Task<ResultadoDaGestao<ItemEditavelV1>> DescartarRascunhoAsync(string slug, long id, string autor, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return NaoEncontrado<ItemEditavelV1>();
        if (await db.ItensEditaveis.FirstOrDefaultAsync(i => i.Id == id && i.CatalogoId == catalogo.Id, ct) is not { } item)
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Item não encontrado.");

        var publicados = await PublicadosAsync(catalogo, ct);
        if (publicados.GetValueOrDefault((item.Tipo, item.Nome)) is not { } publicado)
        {
            // Nunca chegou a uma release: descartar o rascunho é descartar o item.
            db.ItensEditaveis.Remove(item);
            await db.SaveChangesAsync(ct);
            return new(null);
        }

        if (await ConteudoPublicadoAsync(catalogo, publicado, ct) is not { } conteudo)
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.Conflito, "O pacote da última release não pôde ser lido agora: tente descartar de novo em instantes.");
        var (conferido, erro) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(item.Tipo, conteudo));
        if (conferido is null)
            return ResultadoDaGestao<ItemEditavelV1>.Falhou(FalhaNaGestao.Conflito, $"A versão publicada não abre ({erro}): edite o item em vez de descartar.");

        // O item volta EXATAMENTE ao que as máquinas têm: o conteúdo, os dados da vitrine e o arquivo do manifesto.
        item.Conteudo = conferido.Conteudo;
        item.Hash = conferido.Hash;
        item.Descricao = publicado.Descricao;
        item.Arquivo = publicado.Arquivo;
        item.Categoria = publicado.Tecnologia == GeradorDoManifesto.Tecnologia(item.Tipo, item.Nome) ? string.Empty : publicado.Tecnologia;
        item.Dependencias = string.Join('\n', conferido.Dependencias);
        item.RetiradoEmUtc = null;
        item.AlteradoEmUtc = relogio.GetUtcNow().UtcDateTime;
        item.AlteradoPor = Autor(autor);
        await db.SaveChangesAsync(ct);
        await RegistrarAsync(item.Id, AcoesNoItem.Descartado, publicado.Versao, autor, ct);
        return new(Editavel(item, publicados, catalogo, comConteudo: true));
    }

    public async Task<ResultadoDaGestao<IReadOnlyList<AnexoDoItemV1>>> AnexosAsync(string slug, long id, CancellationToken ct)
    {
        if (await ItemDoCatalogoAsync(slug, id, ct) is null)
            return Falha<IReadOnlyList<AnexoDoItemV1>>(slug, id, ct);
        // Sem os bytes: a lista só diz o que há; o download é por anexo.
        var anexos = await db.AnexosDeItemDoCatalogo.AsNoTracking().Where(a => a.ItemId == id).OrderBy(a => a.Nome)
            .Select(a => new { a.Id, a.Nome, a.TipoDeConteudo, a.TamanhoBytes, a.Autor, a.EmUtc })
            .ToListAsync(ct);
        return new(anexos.Select(a => new AnexoDoItemV1(a.Id, a.Nome, a.TipoDeConteudo, a.TamanhoBytes, a.Autor, DevKitPageDb.Utc(a.EmUtc))).ToArray());
    }

    public async Task<ResultadoDaGestao<AnexoDoItemV1>> AnexarAsync(
        string slug, long id, string nome, string tipoDeConteudo, byte[] conteudo, string autor, CancellationToken ct)
    {
        if (await ItemDoCatalogoAsync(slug, id, ct) is null)
            return Falha<AnexoDoItemV1>(slug, id, ct);
        var limpo = Path.GetFileName((nome ?? string.Empty).Replace('\\', '/')).Trim();
        if (limpo.Length == 0 || conteudo.Length == 0)
            return ResultadoDaGestao<AnexoDoItemV1>.Falhou(FalhaNaGestao.Recusada, "Envie o arquivo no campo 'arquivo'.");
        if (conteudo.Length > AnexosDoItem.TamanhoMaximo)
            return ResultadoDaGestao<AnexoDoItemV1>.Falhou(FalhaNaGestao.Recusada, $"O anexo passa de {AnexosDoItem.TamanhoMaximo / 1024 / 1024} MB.");
        if (await db.AnexosDeItemDoCatalogo.CountAsync(a => a.ItemId == id, ct) >= AnexosDoItem.MaximoPorItem)
            return ResultadoDaGestao<AnexoDoItemV1>.Falhou(FalhaNaGestao.Recusada, $"O item já tem {AnexosDoItem.MaximoPorItem} anexos: remova um antes.");

        var anexo = new AnexoDeItemDoCatalogo
        {
            ItemId = id, Nome = limpo.Length <= 200 ? limpo : limpo[..200],
            TipoDeConteudo = string.IsNullOrWhiteSpace(tipoDeConteudo) ? "application/octet-stream" : tipoDeConteudo.Trim()[..Math.Min(100, tipoDeConteudo.Trim().Length)],
            TamanhoBytes = conteudo.LongLength, Conteudo = conteudo, Autor = Autor(autor), EmUtc = relogio.GetUtcNow().UtcDateTime,
        };
        db.AnexosDeItemDoCatalogo.Add(anexo);
        await db.SaveChangesAsync(ct);
        return new(new AnexoDoItemV1(anexo.Id, anexo.Nome, anexo.TipoDeConteudo, anexo.TamanhoBytes, anexo.Autor, DevKitPageDb.Utc(anexo.EmUtc)));
    }

    public async Task<AnexoBaixado?> AnexoAsync(string slug, long id, long anexoId, CancellationToken ct)
        => await ItemDoCatalogoAsync(slug, id, ct) is null
            ? null
            : await db.AnexosDeItemDoCatalogo.AsNoTracking().Where(a => a.Id == anexoId && a.ItemId == id)
                .Select(a => new AnexoBaixado(a.Nome, a.TipoDeConteudo, a.Conteudo)).FirstOrDefaultAsync(ct);

    public async Task<ResultadoDaGestao<AnexoDoItemV1>> RemoverAnexoAsync(string slug, long id, long anexoId, CancellationToken ct)
    {
        if (await ItemDoCatalogoAsync(slug, id, ct) is null)
            return Falha<AnexoDoItemV1>(slug, id, ct);
        if (await db.AnexosDeItemDoCatalogo.FirstOrDefaultAsync(a => a.Id == anexoId && a.ItemId == id, ct) is not { } anexo)
            return ResultadoDaGestao<AnexoDoItemV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Anexo não encontrado.");
        db.AnexosDeItemDoCatalogo.Remove(anexo);
        await db.SaveChangesAsync(ct);
        return new(new AnexoDoItemV1(anexo.Id, anexo.Nome, anexo.TipoDeConteudo, anexo.TamanhoBytes, anexo.Autor, DevKitPageDb.Utc(anexo.EmUtc)));
    }

    /// <summary>
    /// Uma linha do histórico. O rascunho que o editor salva sozinho a cada pausa não vira uma linha por salvamento: o do
    /// mesmo autor dentro da <see cref="JanelaDoRascunho"/> só avança a hora da linha que já existe.
    /// </summary>
    private async Task RegistrarAsync(long itemId, string acao, int versao, string autor, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var quem = Autor(autor);
        if (acao == AcoesNoItem.Rascunho
            && await db.HistoricosDeItemDoCatalogo.Where(h => h.ItemId == itemId).OrderByDescending(h => h.EmUtc).ThenByDescending(h => h.Id).FirstOrDefaultAsync(ct)
                is { Acao: AcoesNoItem.Rascunho } ultima
            && ultima.Autor == quem && agora - ultima.EmUtc < JanelaDoRascunho)
        {
            ultima.EmUtc = agora;
        }
        else
        {
            db.HistoricosDeItemDoCatalogo.Add(new HistoricoDeItemDoCatalogo { ItemId = itemId, Acao = acao, Versao = versao, Autor = quem, EmUtc = agora });
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>O conteúdo do item na última release (lido do zip dela, no arquivo do manifesto); nulo quando o pacote não abre.</summary>
    private async Task<string?> ConteudoPublicadoAsync(Catalogo catalogo, ItemDoCatalogoV1 publicado, CancellationToken ct)
    {
        if (await publicacao.UltimaAsync(catalogo.Slug, DoAdmin, ct) is not { } ultima)
            return null;
        await using var pacote = await publicacao.AbrirPacoteAsync(ultima, ct);
        if (pacote is null)
            return null;
        using var memoria = new MemoryStream();
        await pacote.Conteudo.CopyToAsync(memoria, ct);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(memoria.ToArray()), ZipArchiveMode.Read);
            var entrada = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/'), publicado.Arquivo, StringComparison.OrdinalIgnoreCase));
            if (entrada is null)
                return null;
            using var leitor = new StreamReader(entrada.Open(), Encoding.UTF8);
            return await leitor.ReadToEndAsync(ct);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>O catálogo e o item dele, ou nulo quando um dos dois não existe.</summary>
    private async Task<(Catalogo Catalogo, ItemDoCatalogoEditavel Item)?> ItemDoCatalogoAsync(string slug, long id, CancellationToken ct)
    {
        if (await db.Catalogos.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct) is not { } catalogo)
            return null;
        return await db.ItensEditaveis.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id && i.CatalogoId == catalogo.Id, ct) is { } item
            ? (catalogo, item)
            : null;
    }

    /// <summary>A falha de quem não achou: o catálogo, ou o item nele.</summary>
    private ResultadoDaGestao<T> Falha<T>(string slug, long id, CancellationToken ct) where T : class
        => db.Catalogos.AsNoTracking().Any(c => c.Slug == slug)
            ? ResultadoDaGestao<T>.Falhou(FalhaNaGestao.NaoEncontrado, $"Item {id} não encontrado neste catálogo.")
            : NaoEncontrado<T>();

    private static bool Contem(string texto, string termo) => texto.Contains(termo, StringComparison.CurrentCultureIgnoreCase);
}
