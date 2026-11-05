using System.Data;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

public sealed class DecisoesRemotas(DevKitPageDb db, IRemoto remoto, ISegundoFator fator, TimeProvider relogio) : IDecisoesRemotas
{
    public async Task<IReadOnlyList<DecisaoPendenteV1>?> PendentesAsync(int usuario, string maquina, CancellationToken ct)
    {
        var foto = await remoto.RetratoAsync(usuario, maquina, ct);
        if (foto is null) return null;
        var respostas = await db.RespostasDasDecisoes.AsNoTracking().Where(r => r.UsuarioId == usuario && r.MaquinaId == maquina).ToListAsync(ct);
        return (foto.Retrato.Pendencias ?? []).Select(p =>
        {
            var hash = RegrasDaDecisao.SnapshotId(p);
            return new DecisaoPendenteV1(p, hash, !foto.Maquina.Conectada ? "Máquina desconectada. Atualize o retrato antes de decidir." : RegrasDaDecisao.Bloqueio(p),
                respostas.FirstOrDefault(r => r.PendenciaId == p.Id && r.SnapshotId == hash) is { } r ? Ler(r) : null);
        }).ToArray();
    }

    public async Task<ResultadoDaDecisao<RespostaDaDecisaoV1>> ResponderAsync(int usuario, string maquina, string pendencia, ResponderDecisaoV1 pedido, CancellationToken ct)
    {
        // Serializa com a escrita do retrato: a autorização só nasce para o conteúdo relido neste instante.
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var foto = await remoto.RetratoAsync(usuario, maquina, ct);
        var p = foto?.Retrato.Pendencias?.SingleOrDefault(p => p.Id == pendencia);
        if (p is null) return new(null, "Pendência não encontrada, ou não é sua.", 404);
        var hash = RegrasDaDecisao.SnapshotId(p);
        if (pedido.SnapshotId != hash) return new(null, "O snapshot mudou. Volte ao Board e revise as novas evidências.");
        if (!foto!.Maquina.Conectada) return new(null, "Máquina desconectada. Atualize o retrato antes de decidir.");
        if (await db.RespostasDasDecisoes.AnyAsync(r => r.UsuarioId == usuario && r.MaquinaId == maquina && r.PendenciaId == pendencia && r.SnapshotId == hash, ct))
            return new(null, "Este snapshot já recebeu uma resposta. Consulte o registro no Board.");
        var aprovar = pedido.Acao == "aprovar";
        if (p.Tipo == RemotoV1.PendenciaPergunta)
        {
            if (pedido.Acao != "responder" || pedido.Opcao is null || !(p.Opcoes ?? []).Contains(pedido.Opcao, StringComparer.Ordinal))
                return new(null, "Escolha um dos botões da pergunta.", 400);
        }
        else if (p.Tipo == RemotoV1.PendenciaAprovacao)
        {
            if (pedido.Acao is not ("aprovar" or "ajuste")) return new(null, "Escolha aprovar ou pedir ajuste.", 400);
            if (aprovar && RegrasDaDecisao.Bloqueio(p) is { } bloqueio) return new(null, bloqueio);
        }
        else return new(null, "Esta pendência não aceita decisão remota.", 400);
        if (aprovar)
        {
            var erro = await fator.VerificarAsync(usuario, pedido.Codigo, ativar: false, ct);
            // O contador de tentativas inválidas precisa persistir mesmo sem autorização.
            if (erro is not null) { await transacao.CommitAsync(ct); return new(null, erro, 403); }
        }
        var resposta = new RespostaDaDecisao
        {
            UsuarioId = usuario, MaquinaId = maquina, PendenciaId = pendencia, SnapshotId = hash,
            Acao = pedido.Acao, Opcao = p.Tipo == RemotoV1.PendenciaPergunta ? pedido.Opcao : null,
            Origem = p.Snapshot?.Origem, Destino = p.Snapshot?.Destino, Sha = p.Snapshot?.Sha,
            EmUtc = relogio.GetUtcNow().UtcDateTime, SegundoFator = aprovar,
        };
        db.RespostasDasDecisoes.Add(resposta);
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new(Ler(resposta));
    }

    public async Task<IReadOnlyList<RespostaDaDecisaoV1>> RespostasAsync(int usuario, string maquina, CancellationToken ct)
        => (await db.RespostasDasDecisoes.AsNoTracking().Where(r => r.UsuarioId == usuario && r.MaquinaId == maquina && r.Estado == "registrada")
            .OrderBy(r => r.Id).Take(100).ToListAsync(ct)).Select(Ler).ToArray();

    public async Task<ResultadoDaDecisao<RespostaDaDecisaoV1>> ReceberAsync(int usuario, string maquina, long id, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var r = await db.RespostasDasDecisoes.SingleOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuario && r.MaquinaId == maquina, ct);
        if (r is null) return new(null, "Resposta não encontrada.", 404);
        if (r.Estado != "registrada") return new(null, "Resposta já consumida ou vencida.");
        var pendentes = await PendentesAsync(usuario, maquina, ct);
        var atual = pendentes?.SingleOrDefault(p => p.Pendencia.Id == r.PendenciaId);
        var valida = atual?.SnapshotId == r.SnapshotId && (r.Acao != "aprovar" || atual.Bloqueio is null)
            && r.EmUtc > relogio.GetUtcNow().UtcDateTime.AddMinutes(-5);
        r.Estado = valida ? "recebida" : "vencida";
        await db.SaveChangesAsync(ct); await transacao.CommitAsync(ct);
        return valida ? new(Ler(r)) : new(null, "A autorização venceu ou o snapshot mudou. Revise novamente no site.");
    }
    private static RespostaDaDecisaoV1 Ler(RespostaDaDecisao r)
        => new(r.Id, r.MaquinaId, r.PendenciaId, r.SnapshotId, r.Acao, r.Opcao, r.Origem, r.Destino, r.Sha, DevKitPageDb.Utc(r.EmUtc), r.SegundoFator, r.Estado);
}
