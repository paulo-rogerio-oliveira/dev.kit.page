using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O CONTEXTO do dev.kit (US #556), sobre a tabela das convenções da US #491 migrada. O isolamento é o
/// <see cref="EscopoDoContexto"/> — toda consulta filtra pela empresa dele, e o item de outra empresa é "não encontrado" —,
/// e a permissão é a <see cref="RegrasDoContexto"/>, a mesma regra pura para o dev, o gestor e o admin do board.
/// </summary>
public sealed class ContextoDaEmpresa(DevKitPageDb db, TimeProvider relogio) : IContextoDaEmpresa
{
    public async Task<ContextoCompostoV1> ComporAsync(EscopoDoContexto escopo, string? equipe, CancellationToken ct)
    {
        var daEquipe = RegrasDoContexto.NormalizarEquipe(equipe);
        if (daEquipe.Length > LimitesDoContexto.TamanhoMaximoDaEquipe)
            daEquipe = string.Empty;

        // O gestor vê todas as equipes da empresa; o dev, só a aprovada — a declarada sem participação vira um PEDIDO.
        var participacao = daEquipe.Length == 0 ? string.Empty
            : escopo.AdministraAEmpresa ? SituacoesDeParticipacao.Aprovado
            : await PedirParticipacaoAsync(escopo, daEquipe, ct);
        var comEquipe = participacao == SituacoesDeParticipacao.Aprovado;

        var itens = await Da(escopo.EmpresaId)
            .Where(c => c.Nivel == NiveisDeContexto.Empresa
                || (comEquipe && c.Nivel == NiveisDeContexto.Equipe && c.Equipe == daEquipe)
                || (c.Nivel == NiveisDeContexto.Usuario && c.UsuarioId == escopo.UsuarioId))
            .ToListAsync(ct);
        return new ContextoCompostoV1(await ParaLeituraAsync(itens, ct), daEquipe, participacao);
    }

    public async Task<VisaoDoContextoV1?> VisaoAsync(EscopoDoContexto escopo, CancellationToken ct)
    {
        if (escopo.EhAdmin && !await db.Empresas.AnyAsync(e => e.Id == escopo.EmpresaId, ct))
            return null;

        var aprovadas = await EquipesAprovadasAsync(escopo, ct);
        var todos = await Da(escopo.EmpresaId).ToListAsync(ct);
        var visiveis = todos.Where(i => RegrasDoContexto.PodeVer(escopo, i.Nivel, i.Equipe, i.UsuarioId, aprovadas)).ToList();

        var participacoes = await db.ParticipantesDeEquipe.AsNoTracking().Include(p => p.Usuario)
            .Where(p => p.EmpresaId == escopo.EmpresaId)
            .OrderBy(p => p.Equipe).ThenBy(p => p.UsuarioId)
            .ToListAsync(ct);
        var pendentes = participacoes
            .Where(p => p.UsuarioId == escopo.UsuarioId && p.Situacao == SituacoesDeParticipacao.Pendente)
            .Select(p => p.Equipe).ToArray();

        // O gestor e o admin decidem: veem os pedidos e as participações, e todas as equipes que a empresa já usa.
        IReadOnlyList<ParticipanteDeEquipeV1> participantes = escopo.AdministraAEmpresa ? participacoes.Select(Participante).ToArray() : [];
        var equipes = escopo.AdministraAEmpresa
            ? todos.Where(i => i.Nivel == NiveisDeContexto.Equipe).Select(i => i.Equipe).Concat(participacoes.Select(p => p.Equipe))
            : aprovadas;

        return new VisaoDoContextoV1(
            Papel(escopo), await ParaLeituraAsync(visiveis, ct),
            equipes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            pendentes, participantes, escopo.AdministraAEmpresa);
    }

    public async Task<ResultadoDaGestao<IReadOnlyList<ContextoV1>>> DaEquipeAsync(EscopoDoContexto escopo, string equipe, CancellationToken ct)
    {
        var daEquipe = RegrasDoContexto.NormalizarEquipe(equipe);
        if (!RegrasDoContexto.PodeVer(escopo, NiveisDeContexto.Equipe, daEquipe, 0, await EquipesAprovadasAsync(escopo, ct)))
            return ResultadoDaGestao<IReadOnlyList<ContextoV1>>.Falhou(FalhaNaGestao.Proibida,
                $"Você não participa da equipe '{daEquipe}' (ou o pedido ainda aguarda a aprovação do gestor).");

        var itens = await Da(escopo.EmpresaId).Where(c => c.Nivel == NiveisDeContexto.Equipe && c.Equipe == daEquipe).ToListAsync(ct);
        return new(await ParaLeituraAsync(itens, ct));
    }

    public async Task<ResultadoDoContexto> GravarAsync(EscopoDoContexto escopo, GravacaoDeContextoV1 gravacao, CancellationToken ct)
    {
        // A rota já recusou (422); a regra repete aqui porque o serviço é o último ponto antes do banco.
        if (RegrasDoContexto.ContemSegredo(gravacao.Valor))
            return ResultadoDoContexto.Falhou(FalhaNaGestao.Recusada, RegrasDoContexto.MensagemDeSegredo);

        var nivel = gravacao.Nivel!.Trim().ToLowerInvariant();
        var chave = gravacao.Chave!.Trim().ToLowerInvariant();
        var origem = RegrasDoContexto.NormalizarOrigem(gravacao.Origem);
        var equipe = nivel == NiveisDeContexto.Equipe ? RegrasDoContexto.NormalizarEquipe(gravacao.Equipe) : string.Empty;
        var dono = nivel == NiveisDeContexto.Usuario ? escopo.UsuarioId : 0;

        if (escopo.EhAdmin)
        {
            // O admin do board grava em QUALQUER empresa — que tem de existir —, e no nível usuário diz de quem é o item.
            if (!await db.Empresas.AnyAsync(e => e.Id == escopo.EmpresaId, ct))
                return ResultadoDoContexto.Falhou(FalhaNaGestao.NaoEncontrado, "A empresa não existe.");
            if (nivel == NiveisDeContexto.Usuario)
            {
                if (gravacao.UsuarioId is not > 0)
                    return ResultadoDoContexto.Falhou(FalhaNaGestao.Recusada, "Informe de qual usuário é o contexto (usuarioId).");
                dono = gravacao.UsuarioId.Value;
                if (!await db.Usuarios.AnyAsync(u => u.Id == dono && u.EmpresaId == escopo.EmpresaId, ct))
                    return ResultadoDoContexto.Falhou(FalhaNaGestao.NaoEncontrado, "O usuário não é da empresa.");
            }
        }

        var recusa = RegrasDoContexto.PodeGravar(escopo, nivel, equipe, dono, await EquipesAprovadasAsync(escopo, ct));
        if (recusa.Length > 0)
            return ResultadoDoContexto.Falhou(FalhaNaGestao.Proibida, recusa);

        var existente = await db.Contextos.Include(c => c.Autor).FirstOrDefaultAsync(c => c.EmpresaId == escopo.EmpresaId && c.Nivel == nivel
            && c.Equipe == equipe && c.UsuarioId == dono && c.Chave == chave && c.Origem == origem, ct);

        if (gravacao.VersaoEsperada is { } esperada)
        {
            if (existente is null)
                return ResultadoDoContexto.Falhou(FalhaNaGestao.Conflito, $"'{chave}' foi excluído por outra pessoa depois da sua leitura.");
            if (existente.Versao != esperada)
                return ResultadoDoContexto.Falhou(FalhaNaGestao.Conflito,
                    $"'{chave}' foi alterado por outra pessoa depois da sua leitura (versão {existente.Versao}, a sua era {esperada}).",
                    (await ParaLeituraAsync([existente], ct))[0]);
        }

        if (existente is null)
        {
            existente = new ItemDeContexto { EmpresaId = escopo.EmpresaId, Nivel = nivel, Equipe = equipe, UsuarioId = dono, Chave = chave, Origem = origem, Versao = 0 };
            db.Contextos.Add(existente);
        }

        existente.Valor = gravacao.Valor!.Trim();
        existente.Versao++;
        existente.AutorId = escopo.UsuarioId;
        existente.AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duas gravações ao mesmo tempo: a versão (token de concorrência) ou o índice único decide, e quem perdeu relê.
            return ResultadoDoContexto.Falhou(FalhaNaGestao.Conflito, $"'{chave}' acabou de ser gravado por outra pessoa. Releia e grave de novo.");
        }

        await db.Entry(existente).Reference(c => c.Autor).LoadAsync(ct);
        return new((await ParaLeituraAsync([existente], ct))[0]);
    }

    public async Task<ResultadoDoContexto> ExcluirAsync(EscopoDoContexto escopo, long id, CancellationToken ct)
    {
        if (await ItemAsync(escopo, id, ct) is not { } item)
            return NaoEncontrado();
        var recusa = RegrasDoContexto.PodeGravar(escopo, item.Nivel, item.Equipe, item.UsuarioId, await EquipesAprovadasAsync(escopo, ct));
        if (recusa.Length > 0)
            return ResultadoDoContexto.Falhou(FalhaNaGestao.Proibida, recusa);

        var lido = (await ParaLeituraAsync([item], ct))[0];
        db.Contextos.Remove(item);
        await db.SaveChangesAsync(ct);
        return new(lido);
    }

    public async Task<ResultadoDoContexto> PromoverAsync(EscopoDoContexto escopo, long id, CancellationToken ct)
    {
        if (await ItemAsync(escopo, id, ct) is not { } sugestao)
            return NaoEncontrado();
        if (sugestao.Origem != OrigensDeContexto.Agente)
            return ResultadoDoContexto.Falhou(FalhaNaGestao.Recusada, "Só a sugestão de um agente se promove: este item já é instrução.");
        var recusa = RegrasDoContexto.PodeGravar(escopo, sugestao.Nivel, sugestao.Equipe, sugestao.UsuarioId, await EquipesAprovadasAsync(escopo, ct));
        if (recusa.Length > 0)
            return ResultadoDoContexto.Falhou(FalhaNaGestao.Proibida, recusa);

        var instrucao = await db.Contextos.FirstOrDefaultAsync(c => c.EmpresaId == sugestao.EmpresaId && c.Nivel == sugestao.Nivel
            && c.Equipe == sugestao.Equipe && c.UsuarioId == sugestao.UsuarioId && c.Chave == sugestao.Chave && c.Origem == OrigensDeContexto.Usuario, ct);
        if (instrucao is null)
        {
            instrucao = new ItemDeContexto
            {
                EmpresaId = sugestao.EmpresaId, Nivel = sugestao.Nivel, Equipe = sugestao.Equipe, UsuarioId = sugestao.UsuarioId,
                Chave = sugestao.Chave, Origem = OrigensDeContexto.Usuario, Versao = 0,
            };
            db.Contextos.Add(instrucao);
        }

        instrucao.Valor = sugestao.Valor;
        instrucao.Versao++;
        instrucao.AutorId = escopo.UsuarioId;
        instrucao.AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime;
        db.Contextos.Remove(sugestao);
        await db.SaveChangesAsync(ct);

        await db.Entry(instrucao).Reference(c => c.Autor).LoadAsync(ct);
        return new((await ParaLeituraAsync([instrucao], ct))[0]);
    }

    public async Task<ResultadoDaGestao<ParticipanteDeEquipeV1>> DecidirParticipacaoAsync(
        EscopoDoContexto escopo, string equipe, DecisaoDeParticipacaoV1 decisao, CancellationToken ct)
    {
        if (!escopo.AdministraAEmpresa)
            return ResultadoDaGestao<ParticipanteDeEquipeV1>.Falhou(FalhaNaGestao.Proibida,
                "Só o admin da empresa (o gestor) ou o admin do board decide quem participa de uma equipe.");

        var daEquipe = RegrasDoContexto.NormalizarEquipe(equipe);
        var situacao = decisao.Situacao?.Trim().ToLowerInvariant() ?? string.Empty;
        if (daEquipe.Length == 0 || daEquipe.Length > LimitesDoContexto.TamanhoMaximoDaEquipe)
            return ResultadoDaGestao<ParticipanteDeEquipeV1>.Falhou(FalhaNaGestao.Recusada, "Informe a equipe (até 80 caracteres).");
        if (situacao is not (SituacoesDeParticipacao.Aprovado or SituacoesDeParticipacao.Recusado))
            return ResultadoDaGestao<ParticipanteDeEquipeV1>.Falhou(FalhaNaGestao.Recusada,
                $"A decisão é '{SituacoesDeParticipacao.Aprovado}' ou '{SituacoesDeParticipacao.Recusado}'.");

        // O usuário tem de ser DA empresa do escopo: o gestor de outra empresa não o encontra (404, não revela).
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == decisao.UsuarioId && u.EmpresaId == escopo.EmpresaId, ct);
        if (usuario is null)
            return ResultadoDaGestao<ParticipanteDeEquipeV1>.Falhou(FalhaNaGestao.NaoEncontrado, "O usuário não é da empresa.");

        var participante = await db.ParticipantesDeEquipe.FirstOrDefaultAsync(
            p => p.EmpresaId == escopo.EmpresaId && p.UsuarioId == usuario.Id && p.Equipe == daEquipe, ct);
        var agora = relogio.GetUtcNow().UtcDateTime;
        if (situacao == SituacoesDeParticipacao.Recusado)
        {
            if (participante is not null)
            {
                db.ParticipantesDeEquipe.Remove(participante);
                await db.SaveChangesAsync(ct);
            }

            return new(new ParticipanteDeEquipeV1(usuario.Id, ConhecimentoDaEmpresa.Nome(usuario), daEquipe, SituacoesDeParticipacao.Recusado, DevKitPageDb.Utc(agora)));
        }

        if (participante is null)
        {
            participante = new ParticipanteDeEquipe { EmpresaId = escopo.EmpresaId, UsuarioId = usuario.Id, Equipe = daEquipe };
            db.ParticipantesDeEquipe.Add(participante);
        }

        participante.Situacao = SituacoesDeParticipacao.Aprovado;
        participante.AtualizadoEmUtc = agora;
        participante.Usuario = usuario;
        await db.SaveChangesAsync(ct);
        return new(Participante(participante));
    }

    /// <summary>
    /// O pedido do dev para a equipe que ele declarou: o que já existe fica como está (pendente ou aprovado); o novo nasce
    /// pendente. Devolve a situação.
    /// </summary>
    private async Task<string> PedirParticipacaoAsync(EscopoDoContexto escopo, string equipe, CancellationToken ct)
    {
        var situacao = await db.ParticipantesDeEquipe.AsNoTracking()
            .Where(p => p.EmpresaId == escopo.EmpresaId && p.UsuarioId == escopo.UsuarioId && p.Equipe == equipe)
            .Select(p => p.Situacao).FirstOrDefaultAsync(ct);
        if (situacao is not null)
            return situacao;

        db.ParticipantesDeEquipe.Add(new ParticipanteDeEquipe
        {
            EmpresaId = escopo.EmpresaId, UsuarioId = escopo.UsuarioId, Equipe = equipe,
            Situacao = SituacoesDeParticipacao.Pendente, AtualizadoEmUtc = relogio.GetUtcNow().UtcDateTime,
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Duas sincronizações ao mesmo tempo: o índice único deixou uma — o pedido existe do mesmo jeito.
            db.ChangeTracker.Clear();
        }

        return SituacoesDeParticipacao.Pendente;
    }

    /// <summary>As equipes em que o usuário do escopo participa APROVADO (o pedido pendente não conta).</summary>
    private async Task<IReadOnlySet<string>> EquipesAprovadasAsync(EscopoDoContexto escopo, CancellationToken ct)
        => (await db.ParticipantesDeEquipe.AsNoTracking()
            .Where(p => p.EmpresaId == escopo.EmpresaId && p.UsuarioId == escopo.UsuarioId && p.Situacao == SituacoesDeParticipacao.Aprovado)
            .Select(p => p.Equipe).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

    /// <summary>O item DA empresa do escopo; o de outra empresa é nulo — "não encontrado", sem revelar que existe.</summary>
    private Task<ItemDeContexto?> ItemAsync(EscopoDoContexto escopo, long id, CancellationToken ct)
        => db.Contextos.Include(c => c.Autor).FirstOrDefaultAsync(c => c.Id == id && c.EmpresaId == escopo.EmpresaId, ct);

    private static ResultadoDoContexto NaoEncontrado()
        => ResultadoDoContexto.Falhou(FalhaNaGestao.NaoEncontrado, "O item de contexto não existe.");

    /// <summary>O contexto da empresa, com o autor, na ordem da composição: empresa → equipe → usuário.</summary>
    private IQueryable<ItemDeContexto> Da(int empresaId) => db.Contextos.AsNoTracking().Include(c => c.Autor)
        .Where(c => c.EmpresaId == empresaId)
        .OrderBy(c => c.Nivel).ThenBy(c => c.Equipe).ThenBy(c => c.UsuarioId).ThenBy(c => c.Chave).ThenBy(c => c.Origem);

    /// <summary>A leitura: o dono do item de nível usuário e o autor pelo nome (ou login), e a data na borda.</summary>
    private async Task<IReadOnlyList<ContextoV1>> ParaLeituraAsync(IReadOnlyList<ItemDeContexto> itens, CancellationToken ct)
    {
        var donos = itens.Where(c => c.UsuarioId > 0).Select(c => c.UsuarioId).Distinct().ToArray();
        var nomes = donos.Length == 0
            ? new Dictionary<int, string>()
            : await db.Usuarios.AsNoTracking().Where(u => donos.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => ConhecimentoDaEmpresa.Nome(u), ct);

        return itens.Select(c => new ContextoV1(
            c.Id, c.Nivel, c.Chave, c.Valor, c.Equipe,
            c.UsuarioId > 0 ? nomes.GetValueOrDefault(c.UsuarioId, string.Empty) : string.Empty,
            c.Origem, ConhecimentoDaEmpresa.Nome(c.Autor), DevKitPageDb.Utc(c.AtualizadoEmUtc), c.Versao)).ToArray();
    }

    private static ParticipanteDeEquipeV1 Participante(ParticipanteDeEquipe p)
        => new(p.UsuarioId, ConhecimentoDaEmpresa.Nome(p.Usuario), p.Equipe, p.Situacao, DevKitPageDb.Utc(p.AtualizadoEmUtc));

    private static string Papel(EscopoDoContexto escopo) => escopo.Papel switch
    {
        PapelNoContexto.Admin => Papeis.Admin,
        PapelNoContexto.Gestor => Papeis.Gestor,
        _ => Papeis.Dev,
    };
}
