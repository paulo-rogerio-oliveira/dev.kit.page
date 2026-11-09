using System.Data;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

public sealed class DecisoesRemotas(DevKitPageDb db, IRemoto remoto, ISegundoFator fator, TimeProvider relogio) : IDecisoesRemotas
{
    public async Task<IReadOnlyList<DecisaoPendenteV1>?> PendentesAsync(int usuario, string maquina, CancellationToken ct, bool incluirDispensadas = false)
    {
        var foto = await remoto.RetratoAsync(usuario, maquina, ct);
        if (foto is null) return null;
        var respostas = await db.RespostasDasDecisoes.AsNoTracking().Where(r => r.UsuarioId == usuario && r.MaquinaId == maquina).OrderByDescending(r => r.Id).ToListAsync(ct);
        var dispensadas = (await db.PendenciasDispensadas.AsNoTracking().Where(d => d.UsuarioId == usuario && d.MaquinaId == maquina)
            .Select(d => d.PendenciaId).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        return (foto.Retrato.Pendencias ?? []).Where(p => incluirDispensadas || !dispensadas.Contains(p.Id)).Select(p =>
        {
            var hash = RegrasDaDecisao.SnapshotId(p);
            return new DecisaoPendenteV1(p, hash, !foto.Maquina.Conectada ? "Máquina desconectada. Atualize o retrato antes de decidir." : RegrasDaDecisao.Bloqueio(p),
                respostas.FirstOrDefault(r => r.PendenciaId == p.Id && r.SnapshotId == hash) is { } r ? Ler(r) : null, dispensadas.Contains(p.Id));
        }).ToArray();
    }

    public async Task<ResultadoDaDecisao<DispensaDaPendenciaV1>> DispensarAsync(int usuario, string maquina, string pendencia, bool dispensar, CancellationToken ct)
    {
        var foto = await remoto.RetratoAsync(usuario, maquina, ct);
        if (foto is null) return new(null, "Máquina não encontrada, ou não é sua.", 404);
        var atuais = (foto.Retrato.Pendencias ?? []).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        if (dispensar && !atuais.Contains(pendencia)) return new(null, "Pendência não encontrada, ou não é sua.", 404);
        var agora = relogio.GetUtcNow().UtcDateTime;
        // A dispensa vale enquanto a pendência está no retrato: a que a máquina já resolveu sai daqui também.
        var gravadas = await db.PendenciasDispensadas.Where(d => d.UsuarioId == usuario && d.MaquinaId == maquina).ToListAsync(ct);
        db.PendenciasDispensadas.RemoveRange(gravadas.Where(d => !atuais.Contains(d.PendenciaId) || (!dispensar && d.PendenciaId == pendencia)));
        if (dispensar && !gravadas.Any(d => d.PendenciaId == pendencia))
            db.PendenciasDispensadas.Add(new PendenciaDispensada { UsuarioId = usuario, MaquinaId = maquina, PendenciaId = pendencia, DispensadaEmUtc = agora });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Dois cliques ao mesmo tempo: o índice único deixa uma dispensa, e o resultado é o mesmo.
            db.ChangeTracker.Clear();
        }
        return new(new DispensaDaPendenciaV1(maquina, pendencia, dispensar, DevKitPageDb.Utc(agora)));
    }

    public async Task<ResultadoDaDecisao<RespostaDaDecisaoV1>> ResponderAsync(int usuario, string maquina, string pendencia, ResponderDecisaoV1 pedido, CancellationToken ct)
    {
        // Serializa com a escrita do retrato: a autorização só nasce para o conteúdo relido neste instante.
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var foto = await remoto.RetratoAsync(usuario, maquina, ct);
        var p = foto?.Retrato.Pendencias?.SingleOrDefault(p => p.Id == pendencia);
        if (p is null) return new(null, "Pendência não encontrada, ou não é sua.", 404);
        // (controle-v4, item 3) O dispositivo é opcional; o que veio fora da lista é recusado antes de qualquer gravação.
        var dispositivo = RegrasDaDecisao.Dispositivo(pedido.Dispositivo, out var dispositivoInvalido);
        if (dispositivoInvalido is not null) return new(null, dispositivoInvalido, 400);
        var hash = RegrasDaDecisao.SnapshotId(p);
        if (pedido.SnapshotId != hash) return new(null, "O snapshot mudou. Volte ao Board e revise as novas evidências.");
        if (!foto!.Maquina.Conectada) return new(null, "Máquina desconectada. Atualize o retrato antes de decidir.");
        var anterior = await db.RespostasDasDecisoes.SingleOrDefaultAsync(r => r.UsuarioId == usuario && r.MaquinaId == maquina && r.PendenciaId == pendencia && r.SnapshotId == hash && r.Estado != "vencida", ct);
        if (anterior is not null)
        {
            if (anterior.Estado != "registrada" || !Expirou(anterior))
                return new(null, "Este snapshot já recebeu uma resposta. Consulte o registro no Board.");
            // Mantém a tentativa antiga na auditoria e libera apenas uma nova resposta ativa.
            anterior.Estado = "vencida";
            await db.SaveChangesAsync(ct);
        }
        // (ajuste 9) O autorizar exige o segundo fator, como o aprovar; o escopo está no id do snapshot conferido acima.
        var aprovar = pedido.Acao is DecisoesV1.AcaoAprovar or DecisoesV1.AcaoAutorizar;
        if (p.Tipo == RemotoV1.PendenciaAutorizacao)
        {
            if (pedido.Acao is not (DecisoesV1.AcaoAutorizar or DecisoesV1.AcaoAjuste)) return new(null, "Escolha autorizar ou recusar.", 400);
            if (pedido.Acao == DecisoesV1.AcaoAutorizar && RegrasDaDecisao.Bloqueio(p) is { } bloqueio) return new(null, bloqueio);
        }
        else if (p.Tipo == RemotoV1.PendenciaPergunta)
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
            Origem = p.Snapshot?.Origem, Destino = p.Snapshot?.Destino, Sha = p.Snapshot?.Sha ?? p.Escopo?.Sha,
            EmUtc = relogio.GetUtcNow().UtcDateTime, SegundoFator = aprovar, Dispositivo = dispositivo,
        };
        db.RespostasDasDecisoes.Add(resposta);
        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);
        return new(Ler(resposta));
    }

    public async Task<IReadOnlyList<RespostaDaDecisaoV1>> RespostasAsync(int usuario, string maquina, CancellationToken ct)
    {
        var limite = relogio.GetUtcNow().UtcDateTime.AddMinutes(-5);
        return (await db.RespostasDasDecisoes.AsNoTracking().Where(r => r.UsuarioId == usuario && r.MaquinaId == maquina && r.Estado == "registrada" && r.EmUtc > limite)
            .OrderBy(r => r.Id).Take(100).ToListAsync(ct)).Select(Ler).ToArray();
    }

    public async Task<ResultadoDaDecisao<RespostaDaDecisaoV1>> ReceberAsync(int usuario, string maquina, long id, CancellationToken ct)
    {
        await using var transacao = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var r = await db.RespostasDasDecisoes.SingleOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuario && r.MaquinaId == maquina, ct);
        if (r is null) return new(null, "Resposta não encontrada.", 404);
        if (r.Estado != "registrada") return new(null, "Resposta já consumida ou vencida.");
        // A dispensa é só do painel: a autorização já dada continua valendo para a máquina.
        var pendentes = await PendentesAsync(usuario, maquina, ct, incluirDispensadas: true);
        var atual = pendentes?.SingleOrDefault(p => p.Pendencia.Id == r.PendenciaId);
        var valida = atual?.SnapshotId == r.SnapshotId && (r.Acao is not (DecisoesV1.AcaoAprovar or DecisoesV1.AcaoAutorizar) || atual.Bloqueio is null)
            && r.EmUtc > relogio.GetUtcNow().UtcDateTime.AddMinutes(-5);
        r.Estado = valida ? "recebida" : "vencida";
        await db.SaveChangesAsync(ct); await transacao.CommitAsync(ct);
        return valida ? new(Ler(r)) : new(null, "A autorização venceu ou o snapshot mudou. Revise novamente no site.");
    }
    private bool Expirou(RespostaDaDecisao r) => r.EmUtc <= relogio.GetUtcNow().UtcDateTime.AddMinutes(-5);
    private RespostaDaDecisaoV1 Ler(RespostaDaDecisao r)
        => new(r.Id, r.MaquinaId, r.PendenciaId, r.SnapshotId, r.Acao, r.Opcao, r.Origem, r.Destino, r.Sha, DevKitPageDb.Utc(r.EmUtc), r.SegundoFator,
            r.Estado == "registrada" && Expirou(r) ? "vencida" : r.Estado, r.Dispositivo);
}
