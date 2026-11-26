using System.Text.Json;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

public sealed partial class ArtefatosDosProjetosNoBanco
{
    public async Task<ResultadoDaGestao<PedidoDeArtefatosV1[]>> ListarPedidosAsync(EscopoDoDesign escopo, long projeto,
        int limite, CancellationToken ct)
    {
        if (!await db.ProjetosCompartilhados.AsNoTracking().Visiveis(escopo).AnyAsync(p => p.Id == projeto, ct))
            return ResultadoDaGestao<PedidoDeArtefatosV1[]>.Falhou(FalhaNaGestao.NaoEncontrado, "Projeto não encontrado.");
        // CriadoEm está no snapshot versionado, não na linha. Ordenar os pedidos, nunca suas origens.
        var snapshots = await db.PedidosDeArtefatos.AsNoTracking().Where(p => p.ProjetoId == projeto).Select(p => p.Conteudo).ToListAsync(ct);
        var pedidos = snapshots.Select(p => JsonSerializer.Deserialize<PedidoDeArtefatosV1>(p)!).ToList();
        var publicados = pedidos.SelectMany(p => p.Resultados).Where(a => a.IdRemoto > 0).Select(a => a.IdRemoto).ToHashSet();
        var antigos = await db.ArtefatosDosProjetos.AsNoTracking().Where(a => a.ProjetoId == projeto).ToListAsync(ct);
        pedidos.AddRange(antigos.Where(a => !publicados.Contains(a.Id)).Select(PedidoAntigo));
        var recentes = pedidos.OrderByDescending(p => p.CriadoEm).ThenBy(p => p.Id, StringComparer.Ordinal)
            .Take(Math.Clamp(limite, 1, 50)).ToArray();
        return new(await ResolverLinksAsync(projeto, recentes, ct));
    }

    private static PedidoDeArtefatosV1 PedidoAntigo(ArtefatoDoProjeto a)
        => new(a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), a.ProjetoId, new DateTimeOffset(a.CriadoEmUtc, TimeSpan.Zero),
            [new(a.Id, a.ProjetoId, a.Tipo, a.Repositorio, a.Sha, "pronto", "", null, a.Hash, a.Tamanho, a.Ambiente, "",
                new DateTimeOffset(a.CriadoEmUtc, TimeSpan.Zero), IdRemoto: a.Id, Ciclo: new("publicado", "concluido"))]);

    public async Task<ResultadoDaGestao<PedidoDeArtefatosV1>> RegistrarPedidoAsync(EscopoDoDesign escopo, long projeto,
        PedidoDeArtefatosV1 pedido, CancellationToken ct)
    {
        var (alvo, recusa) = await ParaGravarAsync(escopo, projeto, ct);
        if (alvo is null) return ResultadoDaGestao<PedidoDeArtefatosV1>.Falhou(recusa!.Falha, recusa.Mensagem);
        var problema = ValidarPedido(pedido, projeto);
        if (problema.Length > 0) return ResultadoDaGestao<PedidoDeArtefatosV1>.Falhou(FalhaNaGestao.Recusada, problema);
        var linha = await db.PedidosDeArtefatos.SingleOrDefaultAsync(p => p.ProjetoId == projeto && p.Id == pedido.Id, ct);
        if (linha is null)
        {
            linha = new() { Id = pedido.Id, ProjetoId = projeto };
            db.PedidosDeArtefatos.Add(linha);
        }
        else
        {
            var anterior = JsonSerializer.Deserialize<PedidoDeArtefatosV1>(linha.Conteudo)!;
            if (anterior.Resultados.Count != pedido.Resultados.Count || anterior.Resultados.Any(a =>
                !pedido.Resultados.Any(b => (a.Id, a.Repositorio, a.Ciclo?.Branch) == (b.Id, b.Repositorio, b.Ciclo?.Branch)
                    && ((a.Sha, a.Tipo, a.Ambiente) == (b.Sha, b.Tipo, b.Ambiente)
                        || a.Estado == "falhou" && a.Ciclo?.Etapa == "preparacao" && a.IdRemoto == 0
                        && (a.Sha == "" || a.Sha == b.Sha) && b.Sha.Length == 40
                        && (b.Ciclo?.ComandoId ?? 0) >= (a.Ciclo?.ComandoId ?? 0)))))
                return ResultadoDaGestao<PedidoDeArtefatosV1>.Falhou(FalhaNaGestao.Recusada, "A origem e os IDs do pedido são imutáveis.");
            // Publicado é terminal no mesmo comando: outro executor pode enviar um retrato ainda pendente.
            // Um ComandoId posterior continua autorizando a evolução de uma nova tentativa.
            pedido = pedido with { CriadoEm = anterior.CriadoEm, Resultados = pedido.Resultados.Select(b =>
            {
                var a = anterior.Resultados.Single(a => a.Id == b.Id);
                return a.Ciclo?.ComandoId > (b.Ciclo?.ComandoId ?? 0)
                    || (a.Ciclo?.ComandoId == b.Ciclo?.ComandoId && (a.Estado == "publicado"
                        || a.Estado == "local-pronto" && b.Estado is ("enfileirando" or "enfileirado" or "gerando")))
                    ? a : b;
            }).ToArray() };
        }
        // Links são resolvidos pelo armazém; snapshots não podem oferecer caminhos locais ou URLs arbitrárias.
        linha.Conteudo = JsonSerializer.Serialize(pedido with { Resultados = pedido.Resultados.Select(a => a with { Link = "" }).ToArray() });
        linha.Revisao++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(linha).State = EntityState.Detached;
            return ResultadoDaGestao<PedidoDeArtefatosV1>.Falhou(FalhaNaGestao.Recusada, "Pedido atualizado em paralelo: consulte e repita o mesmo ID.");
        }
        return await ConsultarPedidoAsync(escopo, projeto, pedido.Id, ct);
    }

    public async Task<ResultadoDaGestao<PedidoDeArtefatosV1>> ConsultarPedidoAsync(EscopoDoDesign escopo, long projeto, string pedido, CancellationToken ct)
    {
        if (!await db.ProjetosCompartilhados.AsNoTracking().Visiveis(escopo).AnyAsync(p => p.Id == projeto, ct))
            return ResultadoDaGestao<PedidoDeArtefatosV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Projeto não encontrado.");
        var linha = await db.PedidosDeArtefatos.AsNoTracking().SingleOrDefaultAsync(p => p.ProjetoId == projeto && p.Id == pedido, ct);
        PedidoDeArtefatosV1? valor = linha is null ? null : JsonSerializer.Deserialize<PedidoDeArtefatosV1>(linha.Conteudo);
        if (valor is null && long.TryParse(pedido, out var antigo))
        {
            var a = await db.ArtefatosDosProjetos.AsNoTracking().SingleOrDefaultAsync(a => a.ProjetoId == projeto && a.Id == antigo, ct);
            if (a is not null) valor = PedidoAntigo(a);
        }
        if (valor is null) return ResultadoDaGestao<PedidoDeArtefatosV1>.Falhou(FalhaNaGestao.NaoEncontrado, "Pedido não encontrado.");
        return new((await ResolverLinksAsync(projeto, [valor], ct))[0]);
    }

    private async Task<PedidoDeArtefatosV1[]> ResolverLinksAsync(long projeto, PedidoDeArtefatosV1[] pedidos, CancellationToken ct)
    {
        var ids = pedidos.SelectMany(p => p.Resultados).Where(a => a.IdRemoto > 0).Select(a => a.IdRemoto).Distinct().ToArray();
        var publicacoes = await db.ArtefatosDosProjetos.AsNoTracking().Where(a => a.ProjetoId == projeto && ids.Contains(a.Id)).ToListAsync(ct);
        var vigentes = publicacoes.Where(a => a.RetidoAteUtc is null || a.RetidoAteUtc > relogio.GetUtcNow().UtcDateTime).ToList();
        var links = (await LeituraAsync(vigentes, ct)).ToDictionary(a => a.Id);
        return pedidos.Select(valor => valor with { Resultados = valor.Resultados.Select(a =>
        {
            if (a.IdRemoto <= 0) return a with { Link = "" };
            if (!links.TryGetValue(a.IdRemoto, out var link)) return a with { Link = "", Ciclo = (a.Ciclo ?? new("expirado", "retencao")) with { Estado = "expirado" } };
            if ((a.Repositorio, a.Sha, a.Tipo) != (link.Repositorio, link.Sha, link.Tipo))
                return a with { Link = "", Ciclo = (a.Ciclo ?? new("falhou", "origem")) with { Estado = "falhou" }, Detalhe = "Publicação diverge da origem do resultado." };
            return a with { Link = link.Link, ValidoAte = link.ExpiraEm };
        }).ToArray() }).ToArray();
    }

    private static string ValidarPedido(PedidoDeArtefatosV1 pedido, long projeto)
    {
        if (pedido is null || pedido.ProjetoId != projeto || string.IsNullOrWhiteSpace(pedido.Id) || pedido.Id.Length > 64
            || !pedido.Id.All(char.IsAsciiLetterOrDigit) || pedido.Resultados is not { Count: > 0 and <= 100 }
            || pedido.Resultados.Any(a => a is null))
            return "Informe o pedido e seus resultados (até 100), com identidade válida.";
        if (pedido.Resultados.Select(a => a.Id).Distinct().Count() != pedido.Resultados.Count
            || pedido.Resultados.Select(a => a.Repositorio).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pedido.Resultados.Count)
            return "Os resultados e as origens não se repetem.";
        foreach (var a in pedido.Resultados)
        {
            if (a is null || a.Id <= 0 || a.ProjetoId != projeto || string.IsNullOrWhiteSpace(a.Repositorio) || a.Repositorio.Length > 300
                || !(a.Sha is { Length: 40 } && a.Sha.All(char.IsAsciiHexDigit)
                    || a.Sha == "" && a.Estado == "falhou" && a.Ciclo?.Etapa == "preparacao" && a.IdRemoto == 0)
                || a.Tipo is not ("" or "pacote" or "preview") || a.Ciclo is null || !RegrasDoRemoto.ProdutorValido(a.Produtor)
                || a.Ciclo.ConfiguracaoDoPacote is not (null or "Debug" or "Release")
                || a.Estado is not ("enfileirando" or "enfileirado" or "gerando" or "agente-pendente" or "local-pronto" or "envio-pendente" or "registro-pendente" or "publicado" or "falhou" or "nao-aplicavel" or "expirado"))
                return "Cada resultado exige origem, SHA integral, tipo e estado conhecidos.";
            if (a.Estado is "nao-aplicavel" or "falhou" or "agente-pendente" && string.IsNullOrWhiteSpace(a.Detalhe))
                return "Informe o motivo do resultado não gerado, falho ou pendente.";
            if (a.Estado == "publicado" && a.IdRemoto <= 0) return "Publicado exige o registro individual no site.";
        }
        return "";
    }
}
