using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O catálogo da empresa (US #425, fase 2). O isolamento é UM ponto: <see cref="DaEmpresaAsync"/> carrega o item e
/// devolve a recusa quando ele é de outra empresa — toda operação por id passa por ali, e a lista e os assinados já
/// filtram pela empresa do <see cref="EscopoDaEmpresa"/>.
/// </summary>
public sealed class CatalogoDaEmpresa(DevKitPageDb db, TimeProvider relogio) : ICatalogoDaEmpresa
{
    public async Task<IReadOnlyList<ItemDaEmpresaV1>> ListarAsync(EscopoDaEmpresa escopo, CancellationToken ct)
    {
        var itens = await db.ItensDeCatalogo.AsNoTracking().Include(i => i.Autor)
            .Where(i => i.EmpresaId == escopo.EmpresaId)
            .OrderBy(i => i.Tipo).ThenBy(i => i.Nome)
            .ToListAsync(ct);
        var ids = itens.Select(i => i.Id).ToArray();
        var assinantes = await db.AssinaturasDeCatalogo.Where(a => ids.Contains(a.ItemId))
            .GroupBy(a => a.ItemId).Select(g => new { g.Key, Quantos = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Quantos, ct);
        var meus = (await db.AssinaturasDeCatalogo.Where(a => a.UsuarioId == escopo.UsuarioId && ids.Contains(a.ItemId)).Select(a => a.ItemId).ToListAsync(ct)).ToHashSet();

        return itens.Select(i => Resumo(i, assinantes.GetValueOrDefault(i.Id), meus.Contains(i.Id))).ToArray();
    }

    public async Task<ResultadoDaGestao<PublicacaoFeitaV1>> PublicarAsync(EscopoDaEmpresa escopo, PublicacaoV1 publicacao, CancellationToken ct)
    {
        var nome = publicacao.Nome.Trim();
        var hash = RegrasDoCatalogoDaEmpresa.Hash(publicacao.Conteudo);
        var agora = relogio.GetUtcNow().UtcDateTime;
        var dependencias = string.Join('\n', (publicacao.Dependencias ?? []).Select(d => d.Trim()).Distinct(StringComparer.Ordinal));
        var item = await db.ItensDeCatalogo.FirstOrDefaultAsync(i => i.EmpresaId == escopo.EmpresaId && i.Tipo == publicacao.Tipo && i.Nome == nome, ct);

        if (item is null)
        {
            item = new ItemDeCatalogo
            {
                EmpresaId = escopo.EmpresaId, Tipo = publicacao.Tipo, Nome = nome, AutorId = escopo.UsuarioId, CriadoEmUtc = agora,
            };
            db.ItensDeCatalogo.Add(item);
        }
        else
        {
            if (item.AutorId != escopo.UsuarioId && !escopo.EhGestor)
                return ResultadoDaGestao<PublicacaoFeitaV1>.Falhou(FalhaNaGestao.Proibida, $"'{nome}' é de outro autor: só ele ou o gestor publicam uma versão nova.");

            // O mesmo conteúdo NÃO é versão nova: quem publica de novo sem mudar nada não acorda a sincronia de ninguém.
            if (item.HashAtual == hash && item.RetiradoEmUtc is null)
                return new(new PublicacaoFeitaV1(item.Id, item.Tipo, item.Nome, item.VersaoAtual, item.HashAtual, Nova: false));
        }

        var nova = item.HashAtual != hash;
        item.Descricao = (publicacao.Descricao ?? string.Empty).Trim();
        item.Dependencias = dependencias;
        item.RetiradoEmUtc = null;
        item.AtualizadoEmUtc = agora;
        if (nova)
        {
            item.VersaoAtual++;
            item.HashAtual = hash;
            db.VersoesDeItem.Add(new VersaoDeItem
            {
                Item = item, Versao = item.VersaoAtual, Hash = hash, Conteudo = publicacao.Conteudo, AutorId = escopo.UsuarioId, PublicadaEmUtc = agora,
            });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duas publicações do mesmo nome (ou da mesma versão) ao mesmo tempo: o índice único decide.
            return ResultadoDaGestao<PublicacaoFeitaV1>.Falhou(FalhaNaGestao.Conflito, $"'{nome}' acabou de ser publicado por outra pessoa. Publique de novo.");
        }

        return new(new PublicacaoFeitaV1(item.Id, item.Tipo, item.Nome, item.VersaoAtual, item.HashAtual, nova));
    }

    public async Task<ResultadoDaGestao<ItemDaEmpresaV1>> RetirarAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct)
    {
        var (item, recusa) = await DaEmpresaAsync<ItemDaEmpresaV1>(escopo, id, ct);
        if (item is null)
            return recusa!;
        if (item.AutorId != escopo.UsuarioId && !escopo.EhGestor)
            return ResultadoDaGestao<ItemDaEmpresaV1>.Falhou(FalhaNaGestao.Proibida, "Só o autor do item ou o gestor da empresa o retiram.");

        item.RetiradoEmUtc ??= relogio.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        var assinantes = await db.AssinaturasDeCatalogo.CountAsync(a => a.ItemId == id, ct);
        var assinado = await db.AssinaturasDeCatalogo.AnyAsync(a => a.ItemId == id && a.UsuarioId == escopo.UsuarioId, ct);
        await db.Entry(item).Reference(i => i.Autor).LoadAsync(ct);
        return new(Resumo(item, assinantes, assinado));
    }

    public async Task<ResultadoDaGestao<AssinaturaFeitaV1>> AssinarAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct)
    {
        var (item, recusa) = await DaEmpresaAsync<AssinaturaFeitaV1>(escopo, id, ct);
        if (item is null)
            return recusa!;
        if (item.RetiradoEmUtc is not null)
            return ResultadoDaGestao<AssinaturaFeitaV1>.Falhou(FalhaNaGestao.Recusada, $"'{item.Nome}' foi retirado do catálogo da empresa.");

        // As dependências que estão no catálogo DA EMPRESA vêm junto (as do básico já estão em toda máquina),
        // inclusive as dependências delas — o fluxo traz as skills e os plans que usa.
        var trouxe = new List<string>();
        var visitados = new HashSet<long> { item.Id };
        var fila = new Queue<ItemDeCatalogo>([item]);
        var agora = relogio.GetUtcNow().UtcDateTime;
        while (fila.TryDequeue(out var atual))
        {
            if (!await db.AssinaturasDeCatalogo.AnyAsync(a => a.ItemId == atual.Id && a.UsuarioId == escopo.UsuarioId, ct))
            {
                db.AssinaturasDeCatalogo.Add(new AssinaturaDeCatalogo { ItemId = atual.Id, UsuarioId = escopo.UsuarioId, AssinadaEmUtc = agora });
                if (atual.Id != item.Id)
                    trouxe.Add(RegrasDoCatalogoDaEmpresa.Chave(atual.Tipo, atual.Nome));
            }

            foreach (var (tipo, nome) in Dependencias(atual))
            {
                var dependencia = await db.ItensDeCatalogo.FirstOrDefaultAsync(
                    i => i.EmpresaId == escopo.EmpresaId && i.Tipo == tipo && i.Nome == nome && i.RetiradoEmUtc == null, ct);
                if (dependencia is not null && visitados.Add(dependencia.Id))
                    fila.Enqueue(dependencia);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Assinar duas vezes ao mesmo tempo: o índice único guarda uma — e o resultado é o mesmo.
        }

        return new(new AssinaturaFeitaV1(item.Id, trouxe));
    }

    public async Task<ResultadoDaGestao<AssinaturaFeitaV1>> DesistirAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct)
    {
        var (item, recusa) = await DaEmpresaAsync<AssinaturaFeitaV1>(escopo, id, ct);
        if (item is null)
            return recusa!;

        await db.AssinaturasDeCatalogo.Where(a => a.ItemId == id && a.UsuarioId == escopo.UsuarioId).ExecuteDeleteAsync(ct);
        return new(new AssinaturaFeitaV1(item.Id, []));
    }

    public async Task<IReadOnlyList<ItemAssinadoV1>> AssinadosAsync(EscopoDaEmpresa escopo, CancellationToken ct)
    {
        var itens = await db.AssinaturasDeCatalogo.AsNoTracking()
            .Where(a => a.UsuarioId == escopo.UsuarioId)
            .Join(db.ItensDeCatalogo.Where(i => i.EmpresaId == escopo.EmpresaId), a => a.ItemId, i => i.Id, (_, i) => i)
            .OrderBy(i => i.Tipo).ThenBy(i => i.Nome)
            .ToListAsync(ct);
        var ids = itens.Select(i => i.Id).ToArray();
        var versoes = await db.VersoesDeItem.AsNoTracking().Where(v => ids.Contains(v.ItemId))
            .Select(v => new { v.ItemId, v.Versao, v.Conteudo })
            .ToListAsync(ct);

        // O retirado vai sem conteúdo e marcado: a sincronia o tira da máquina.
        return itens.Select(i => new ItemAssinadoV1(
                i.Id, i.Tipo, i.Nome, i.VersaoAtual, i.HashAtual,
                i.RetiradoEmUtc is null ? versoes.FirstOrDefault(v => v.ItemId == i.Id && v.Versao == i.VersaoAtual)?.Conteudo ?? string.Empty : string.Empty,
                i.RetiradoEmUtc is not null))
            .ToArray();
    }

    /// <summary>
    /// O PONTO ÚNICO do isolamento: o item do id, se for da empresa do escopo. O de outra empresa é
    /// <see cref="FalhaNaGestao.Proibida"/> (403) — o mesmo que o filtro de máquina do painel.
    /// </summary>
    private async Task<(ItemDeCatalogo? Item, ResultadoDaGestao<T>? Recusa)> DaEmpresaAsync<T>(EscopoDaEmpresa escopo, long id, CancellationToken ct)
        where T : class
    {
        var item = await db.ItensDeCatalogo.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
            return (null, ResultadoDaGestao<T>.Falhou(FalhaNaGestao.NaoEncontrado, "Item não encontrado."));
        if (item.EmpresaId != escopo.EmpresaId)
            return (null, ResultadoDaGestao<T>.Falhou(FalhaNaGestao.Proibida, "O item é de outra empresa."));
        return (item, null);
    }

    private static IEnumerable<(string Tipo, string Nome)> Dependencias(ItemDeCatalogo item)
        => item.Dependencias.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(RegrasDoCatalogoDaEmpresa.Dependencia)
            .Where(d => d is not null)
            .Select(d => d!.Value);

    private static ItemDaEmpresaV1 Resumo(ItemDeCatalogo i, int assinantes, bool assinado)
        => new(i.Id, i.Tipo, i.Nome, i.Descricao, i.VersaoAtual, i.HashAtual,
            i.Autor is null ? string.Empty : i.Autor.Nome.Length > 0 ? i.Autor.Nome : i.Autor.Login,
            assinantes, assinado, DevKitPageDb.Utc(i.AtualizadoEmUtc), i.RetiradoEmUtc is { } retirado ? DevKitPageDb.Utc(retirado) : null,
            i.Dependencias.Split('\n', StringSplitOptions.RemoveEmptyEntries));
}
