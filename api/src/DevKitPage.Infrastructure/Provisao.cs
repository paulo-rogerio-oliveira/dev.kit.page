using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A provisão do lado da API: grava o pedido e a auditoria, e lê — sempre filtrando pelo usuário do token. Nada aqui fala
/// com o Azure: quem executa é o worker, pelo <see cref="MotorDeProvisao"/>.
/// </summary>
public sealed class Provisao(DevKitPageDb db, TimeProvider relogio, IOptions<OpcoesDaProvisao> opcoes) : IProvisao
{
    public async Task<PermissaoDeProvisaoV1> PermissaoAsync(int usuarioId, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var origem = await OrigemAsync(db, cfg, usuarioId, ct);
        return new PermissaoDeProvisaoV1(origem != ProvisaoV1.OrigemNenhuma, origem, RegrasDaProvisao.Regioes(cfg), RegrasDaProvisao.RegiaoPadrao(cfg),
            Math.Max(1, cfg.MaximoDeFilasPorUsuario));
    }

    /// <summary>De onde vem a liberação do usuário: a permissão explícita, o plano da empresa, ou nenhuma.</summary>
    internal static async Task<string> OrigemAsync(DevKitPageDb db, OpcoesDaProvisao cfg, int usuarioId, CancellationToken ct)
    {
        var permissao = await db.PermissoesDeUsuario.AsNoTracking().AnyAsync(p => p.UsuarioId == usuarioId && p.Recurso == ProvisaoV1.RecursoProvisao, ct);
        var plano = permissao || cfg.PlanosQueLiberam.Length == 0
            ? null
            : await db.Usuarios.AsNoTracking().Where(u => u.Id == usuarioId).Select(u => u.Empresa == null ? null : u.Empresa.Plano).FirstOrDefaultAsync(ct);
        return RegrasDaProvisao.Origem(permissao, plano, cfg.PlanosQueLiberam);
    }

    public async Task<IReadOnlyList<RecursoProvisionadoV1>> RecursosAsync(int usuarioId, CancellationToken ct)
        => (await db.RecursosProvisionados.AsNoTracking().Where(r => r.UsuarioId == usuarioId).ToListAsync(ct))
            .OrderByDescending(r => r.CriadoEmUtc).ThenByDescending(r => r.Id).Select(Leitura).ToArray();

    public async Task<DetalheDoRecursoV1?> DetalheAsync(int usuarioId, long id, CancellationToken ct)
    {
        var recurso = await db.RecursosProvisionados.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuarioId, ct);
        if (recurso is null)
            return null;
        var eventos = (await db.EventosDaProvisao.AsNoTracking().Where(e => e.RecursoId == id).ToListAsync(ct))
            .OrderBy(e => e.EmUtc).ThenBy(e => e.Id)
            .Select(e => new EventoDaProvisaoV1(DevKitPageDb.Utc(e.EmUtc), e.Evento, e.Autor, e.Detalhe)).ToArray();
        return new DetalheDoRecursoV1(Leitura(recurso), eventos);
    }

    public async Task<ResultadoDaGestao<PedidoAceito>> PedirFilaAsync(int usuarioId, string autor, string regiao, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var agora = relogio.GetUtcNow().UtcDateTime;
        var ativos = (await db.RecursosProvisionados.AsNoTracking()
                .Where(r => r.UsuarioId == usuarioId && r.Tipo == ProvisaoV1.TipoFila && r.Estado != ProvisaoV1.EstadoRemovido).ToListAsync(ct))
            .OrderByDescending(r => r.CriadoEmUtc).ToList();
        if (ativos.Count >= Math.Max(1, cfg.MaximoDeFilasPorUsuario))
            return new(new PedidoAceito(Leitura(ativos[0]), Novo: false));

        // O nome é sorteado AQUI, uma vez: a repetição do pedido usa o mesmo, e o índice único barra o sorteio repetido.
        for (var tentativa = 1; ; tentativa++)
        {
            var recurso = new RecursoProvisionado
            {
                UsuarioId = usuarioId,
                Tipo = ProvisaoV1.TipoFila,
                Nome = RegrasDaProvisao.NomeDaConta(usuarioId),
                Fila = ProvisaoV1.NomeDaFila,
                Regiao = regiao,
                GrupoDeRecursos = RegrasDaProvisao.NomeDoGrupo(cfg.PrefixoDoGrupo, usuarioId),
                Operacao = ProvisaoV1.OperacaoCriar,
                Estado = ProvisaoV1.EstadoPedido,
                CriadoEmUtc = agora,
                AtualizadoEmUtc = agora,
            };
            db.RecursosProvisionados.Add(recurso);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (tentativa < 3)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            await AuditarAsync(recurso, ProvisaoV1.EventoPedido, autor, $"{recurso.Regiao} · conta {recurso.Nome} · grupo {recurso.GrupoDeRecursos}", agora, ct);
            return new(new PedidoAceito(Leitura(recurso), Novo: true));
        }
    }

    public async Task<ResultadoDaGestao<PedidoAceito>> RepetirAsync(int usuarioId, string autor, long id, CancellationToken ct)
    {
        var recurso = await db.RecursosProvisionados.FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuarioId, ct);
        if (recurso is null)
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.NaoEncontrado, "Recurso não encontrado.");
        if (!RegrasDaProvisao.PodeRepetir(recurso.Estado))
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito, "Só o pedido que falhou se repete.");

        var agora = relogio.GetUtcNow().UtcDateTime;
        Reabrir(recurso, recurso.Operacao, agora);
        if (!await GravarAsync(ct))
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito, "O pedido mudou enquanto você o repetia. Atualize e tente de novo.");
        await AuditarAsync(recurso, ProvisaoV1.EventoRepeticao, autor, RotuloDaOperacao(recurso.Operacao), agora, ct);
        return new(new PedidoAceito(Leitura(recurso), Novo: true));
    }

    public async Task<ResultadoDaGestao<PedidoAceito>> RemoverAsync(int usuarioId, string autor, long id, CancellationToken ct)
    {
        var recurso = await db.RecursosProvisionados.FirstOrDefaultAsync(r => r.Id == id && r.UsuarioId == usuarioId, ct);
        if (recurso is null)
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.NaoEncontrado, "Recurso não encontrado.");

        switch (RegrasDaProvisao.DecidirRemocao(recurso.Estado, recurso.Operacao))
        {
            case Remocao.JaFeita:
            case Remocao.JaPedida:
                return new(new PedidoAceito(Leitura(recurso), Novo: false));
            case Remocao.AguardeACriacao:
                return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito,
                    "A fila ainda está sendo criada. Remova depois que ela ficar pronta (ou falhar).");
        }

        var agora = relogio.GetUtcNow().UtcDateTime;
        Reabrir(recurso, ProvisaoV1.OperacaoRemover, agora);
        if (!await GravarAsync(ct))
            return ResultadoDaGestao<PedidoAceito>.Falhou(FalhaNaGestao.Conflito, "O pedido mudou enquanto você o removia. Atualize e tente de novo.");
        await AuditarAsync(recurso, ProvisaoV1.EventoRemocaoPedida, autor, $"conta {recurso.Nome}", agora, ct);
        return new(new PedidoAceito(Leitura(recurso), Novo: true));
    }

    /// <summary>Devolve o recurso ao worker com a operação, do começo das tentativas.</summary>
    private static void Reabrir(RecursoProvisionado recurso, string operacao, DateTime agora)
    {
        recurso.Operacao = operacao;
        recurso.Estado = ProvisaoV1.EstadoPedido;
        recurso.Tentativas = 0;
        recurso.Erro = string.Empty;
        recurso.ProximaTentativaEmUtc = null;
        recurso.ReservadoPor = string.Empty;
        recurso.ReservadoAteUtc = null;
        recurso.Versao++;
        recurso.AtualizadoEmUtc = agora;
    }

    /// <summary>Grava a mudança; falso quando o worker mudou o recurso no meio (o token de concorrência).</summary>
    private async Task<bool> GravarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }

    private Task AuditarAsync(RecursoProvisionado recurso, string evento, string autor, string detalhe, DateTime agora, CancellationToken ct)
        => MotorDeProvisao.AuditarAsync(db, recurso.Id, recurso.UsuarioId, evento, autor, detalhe, agora, ct);

    internal static string RotuloDaOperacao(string operacao) => operacao == ProvisaoV1.OperacaoRemover ? "remoção" : "criação";

    internal static RecursoProvisionadoV1 Leitura(RecursoProvisionado r)
        => new(r.Id, r.Tipo, r.Nome, r.Fila, r.Regiao, r.GrupoDeRecursos, r.Operacao, r.Estado, r.Tentativas, r.Erro, r.Endpoint,
            DevKitPageDb.Utc(r.CriadoEmUtc), DevKitPageDb.Utc(r.AtualizadoEmUtc),
            r.ProntoEmUtc is { } pronto ? DevKitPageDb.Utc(pronto) : null,
            r.ProximaTentativaEmUtc is { } proxima ? DevKitPageDb.Utc(proxima) : null);
}

/// <summary>
/// O motor da provisão, usado pelo WORKER (<c>DevKitPage.Provisao</c>) sobre a mesma base. A reserva é otimista: o worker
/// marca o pedido com o nome dele e uma validade, e o token de concorrência faz o segundo worker perder a corrida.
/// </summary>
public sealed class MotorDeProvisao(DevKitPageDb db, TimeProvider relogio, IOptions<OpcoesDaProvisao> opcoes) : IMotorDeProvisao
{
    public async Task<RecursoProvisionado?> ReservarAsync(string trabalhador, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var agora = relogio.GetUtcNow().UtcDateTime;
        // O pedido vencido e a execução cuja reserva venceu (o worker caiu no meio): os mais antigos primeiro.
        var candidatos = await db.RecursosProvisionados
            .Where(r => (r.Estado == ProvisaoV1.EstadoPedido && (r.ProximaTentativaEmUtc == null || r.ProximaTentativaEmUtc <= agora))
                || (r.Estado == ProvisaoV1.EstadoEmProvisao && r.ReservadoAteUtc != null && r.ReservadoAteUtc < agora))
            .OrderBy(r => r.AtualizadoEmUtc).ThenBy(r => r.Id)
            .Take(10)
            .ToListAsync(ct);

        foreach (var recurso in candidatos)
        {
            var retomada = recurso.Estado == ProvisaoV1.EstadoEmProvisao;
            if (retomada && recurso.Tentativas >= Math.Max(1, cfg.MaximoDeTentativas))
            {
                // O worker caiu no meio em TODAS as tentativas: o pedido não volta a derrubá-lo — fica falhou, e o usuário repete.
                await DesistirAsync(recurso, agora, ct);
                continue;
            }

            recurso.Estado = ProvisaoV1.EstadoEmProvisao;
            recurso.Tentativas++;
            recurso.ReservadoPor = trabalhador;
            recurso.ReservadoAteUtc = agora.AddMinutes(Math.Max(1, cfg.ReservaMinutos));
            recurso.ProximaTentativaEmUtc = null;
            recurso.Versao++;
            recurso.AtualizadoEmUtc = agora;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Outro worker (ou o usuário) mudou o pedido entre a leitura e a gravação: o próximo.
                db.Entry(recurso).State = EntityState.Detached;
                continue;
            }

            await AuditarAsync(db, recurso.Id, recurso.UsuarioId, ProvisaoV1.EventoInicio, ProvisaoV1.AutorWorker,
                $"{Provisao.RotuloDaOperacao(recurso.Operacao)} · tentativa {recurso.Tentativas}{(retomada ? " (retomada: a reserva anterior venceu)" : string.Empty)}",
                agora, ct);
            return recurso;
        }

        return null;
    }

    public async Task<bool> ConcluirAsync(long id, string trabalhador, RecursoNaNuvem? recursoNaNuvem, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var recurso = await DoTrabalhadorAsync(id, trabalhador, ct);
        if (recurso is null)
            return false;

        var remocao = recurso.Operacao == ProvisaoV1.OperacaoRemover;
        recurso.Estado = remocao ? ProvisaoV1.EstadoRemovido : ProvisaoV1.EstadoPronto;
        recurso.Erro = string.Empty;
        if (remocao)
        {
            recurso.RemovidoEmUtc = agora;
            recurso.Endpoint = string.Empty;
        }
        else
        {
            recurso.ProntoEmUtc = agora;
            recurso.IdNoAzure = recursoNaNuvem?.IdNoAzure ?? recurso.IdNoAzure;
            recurso.Endpoint = recursoNaNuvem?.Endpoint ?? recurso.Endpoint;
        }

        if (!await LiberarAsync(recurso, agora, ct))
            return false;
        await AuditarAsync(db, recurso.Id, recurso.UsuarioId, remocao ? ProvisaoV1.EventoRemovido : ProvisaoV1.EventoPronto, ProvisaoV1.AutorWorker,
            remocao ? $"conta {recurso.Nome} removida" : $"grupo, conta e fila {recurso.Fila} prontos", agora, ct);
        return true;
    }

    public async Task<bool> FalharAsync(long id, string trabalhador, string mensagem, bool transitoria, CancellationToken ct)
    {
        var cfg = opcoes.Value;
        var agora = relogio.GetUtcNow().UtcDateTime;
        var recurso = await DoTrabalhadorAsync(id, trabalhador, ct);
        if (recurso is null)
            return false;

        var erro = RegrasDaProvisao.Erro(mensagem);
        recurso.Erro = erro;
        string evento;
        string detalhe;
        if (RegrasDaProvisao.TentaDeNovo(transitoria, recurso.Tentativas, cfg))
        {
            var espera = RegrasDaProvisao.Espera(recurso.Tentativas, cfg);
            recurso.Estado = ProvisaoV1.EstadoPedido;
            recurso.ProximaTentativaEmUtc = agora.Add(espera);
            evento = ProvisaoV1.EventoFalhaTransitoria;
            detalhe = $"{erro} — nova tentativa em {(int)espera.TotalSeconds} s";
        }
        else
        {
            recurso.Estado = ProvisaoV1.EstadoFalhou;
            evento = ProvisaoV1.EventoFalha;
            detalhe = transitoria ? $"{erro} — {recurso.Tentativas} tentativas esgotadas" : erro;
        }

        if (!await LiberarAsync(recurso, agora, ct))
            return false;
        await AuditarAsync(db, recurso.Id, recurso.UsuarioId, evento, ProvisaoV1.AutorWorker, RegrasDaProvisao.Erro(detalhe), agora, ct);
        return true;
    }

    private async Task DesistirAsync(RecursoProvisionado recurso, DateTime agora, CancellationToken ct)
    {
        recurso.Estado = ProvisaoV1.EstadoFalhou;
        recurso.Erro = $"A {Provisao.RotuloDaOperacao(recurso.Operacao)} não terminou em {recurso.Tentativas} tentativas: o worker parou no meio de cada uma.";
        if (await LiberarAsync(recurso, agora, ct))
            await AuditarAsync(db, recurso.Id, recurso.UsuarioId, ProvisaoV1.EventoFalha, ProvisaoV1.AutorWorker, recurso.Erro, agora, ct);
    }

    /// <summary>O recurso ainda reservado a este worker (em provisão); nulo quando a reserva passou a outro.</summary>
    private Task<RecursoProvisionado?> DoTrabalhadorAsync(long id, string trabalhador, CancellationToken ct)
        => db.RecursosProvisionados.FirstOrDefaultAsync(r => r.Id == id && r.Estado == ProvisaoV1.EstadoEmProvisao && r.ReservadoPor == trabalhador, ct);

    private async Task<bool> LiberarAsync(RecursoProvisionado recurso, DateTime agora, CancellationToken ct)
    {
        recurso.ReservadoPor = string.Empty;
        recurso.ReservadoAteUtc = null;
        recurso.Versao++;
        recurso.AtualizadoEmUtc = agora;
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Só este: os outros candidatos da reserva continuam rastreados.
            db.Entry(recurso).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>Uma linha na auditoria da provisão (só acrescenta).</summary>
    internal static async Task AuditarAsync(DevKitPageDb db, long? recursoId, int usuarioId, string evento, string autor, string detalhe, DateTime agora, CancellationToken ct)
    {
        db.EventosDaProvisao.Add(new EventoDaProvisao
        {
            RecursoId = recursoId,
            UsuarioId = usuarioId,
            Evento = evento,
            Autor = autor.Length <= ValidadorDeEmpresa.TamanhoMaximoDoLogin ? autor : autor[..ValidadorDeEmpresa.TamanhoMaximoDoLogin],
            Detalhe = RegrasDaProvisao.Erro(detalhe),
            EmUtc = agora,
        });
        await db.SaveChangesAsync(ct);
    }
}
