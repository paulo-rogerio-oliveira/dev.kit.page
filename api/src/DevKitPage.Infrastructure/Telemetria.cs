using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// A ingestão dos lotes do dev.kit. IDEMPOTENTE por eventId: o dev.kit reenvia o que não teve
/// resposta (e dois processos dele podem mandar o mesmo lote), e é isto que mantém as contas certas.
/// O total diário é consolidado NA MESMA transação do evento bruto — por isso o expurgo dos brutos
/// nunca muda o histórico do dashboard.
/// </summary>
public sealed class IngestaoDeTelemetria(DevKitPageDb db, TimeProvider relogio) : IIngestaoDeTelemetria
{
    public async Task<BatchResultV1> RegistrarAsync(int maquinaId, TelemetryBatchV1 lote, CancellationToken ct)
    {
        // Dois lotes com o mesmo evento ao mesmo tempo: o índice único recusa o segundo, e a nova
        // tentativa já o encontra gravado (vira duplicado, como deve).
        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                return await RegistrarUmaVezAsync(maquinaId, lote, ct);
            }
            catch (DbUpdateException) when (tentativa == 0)
            {
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<BatchResultV1> RegistrarUmaVezAsync(int maquinaId, TelemetryBatchV1 lote, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        var conhecidos = lote.Eventos.Where(e => TiposDeEvento.Conhecidos.Contains(e.Tipo ?? string.Empty)).ToList();
        var unicos = conhecidos.GroupBy(e => e.EventId.Trim(), StringComparer.Ordinal).Select(g => g.First()).ToList();
        var ids = unicos.Select(e => e.EventId.Trim()).ToList();
        var existentes = (await db.Eventos.Where(e => ids.Contains(e.EventId)).Select(e => e.EventId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var novos = unicos
            .Where(e => !existentes.Contains(e.EventId.Trim()))
            .Select(e => new EventoDeUso
            {
                EventId = e.EventId.Trim(),
                MaquinaId = maquinaId,
                Tipo = e.Tipo,
                SessaoId = ValidadorDeLote.Cortar(e.SessaoId),
                Quantidade = e.Quantidade,
                Valor = e.Valor,
                Detalhe = ValidadorDeLote.Cortar(e.Detalhe),
                EmUtc = e.Em.UtcDateTime,
                Dia = DateOnly.FromDateTime(e.Em.UtcDateTime),
                RecebidoEmUtc = agora,
            })
            .ToList();

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        db.Eventos.AddRange(novos);
        await ConsolidarAsync(maquinaId, novos, ct);

        var maquina = await db.Maquinas.FirstAsync(m => m.Id == maquinaId, ct);
        maquina.UltimoEnvioEmUtc = agora;
        if (!string.IsNullOrWhiteSpace(lote.VersaoDevKit))
            maquina.VersaoDevKit = ValidadorDeLote.Cortar(lote.VersaoDevKit);

        await db.SaveChangesAsync(ct);
        await transacao.CommitAsync(ct);

        return new BatchResultV1(lote.Eventos.Count, novos.Count, conhecidos.Count - novos.Count, lote.Eventos.Count - conhecidos.Count);
    }

    /// <summary>Soma os eventos NOVOS nos totais diários da máquina (cria a linha do dia que falta).</summary>
    private async Task ConsolidarAsync(int maquinaId, IReadOnlyCollection<EventoDeUso> novos, CancellationToken ct)
    {
        if (novos.Count == 0)
            return;

        var dias = novos.Select(e => e.Dia).Distinct().ToList();
        var totais = await db.TotaisDiarios.Where(t => t.MaquinaId == maquinaId && dias.Contains(t.Dia)).ToListAsync(ct);
        var porChave = totais.ToDictionary(t => (t.Dia, t.Tipo, t.Detalhe));

        foreach (var grupo in novos.GroupBy(e => (e.Dia, e.Tipo, e.Detalhe)))
        {
            if (!porChave.TryGetValue(grupo.Key, out var total))
            {
                total = new TotalDiario { MaquinaId = maquinaId, Dia = grupo.Key.Dia, Tipo = grupo.Key.Tipo, Detalhe = grupo.Key.Detalhe };
                db.TotaisDiarios.Add(total);
                porChave[grupo.Key] = total;
            }

            total.Quantidade += grupo.Sum(e => (long)e.Quantidade);
            total.Eventos += grupo.Count();
            total.Valor += grupo.Sum(e => e.Valor ?? 0);
        }
    }
}

/// <summary>
/// As consultas do dashboard. Os números saem dos TOTAIS DIÁRIOS, somados no banco (o
/// <c>GroupBy</c> vira SQL); só o log de eventos lê os brutos.
/// </summary>
public sealed class ConsultasDoPainel(DevKitPageDb db) : IConsultasDoPainel
{
    /// <summary>O teto de dias preenchidos na série (um período maior é cortado no início).</summary>
    public const int DiasMaximosDaSerie = 400;

    public async Task<IReadOnlyList<MaquinaResumo>> MaquinasAsync(CancellationToken ct)
    {
        var eventos = await db.TotaisDiarios
            .GroupBy(t => t.MaquinaId)
            .Select(g => new { MaquinaId = g.Key, Eventos = g.Sum(t => t.Eventos) })
            .ToDictionaryAsync(x => x.MaquinaId, x => x.Eventos, ct);

        var maquinas = await db.Maquinas.AsNoTracking().OrderBy(m => m.Apelido).ToListAsync(ct);
        return maquinas
            .Select(m => new MaquinaResumo(
                m.Id, m.MaquinaId, m.Apelido, m.VersaoDevKit, DevKitPageDb.Utc(m.RegistradaEmUtc),
                m.UltimoEnvioEmUtc is { } ultimo ? DevKitPageDb.Utc(ultimo) : null,
                eventos.GetValueOrDefault(m.Id)))
            .ToArray();
    }

    public async Task<QuantidadeResposta> QuantidadeAsync(Periodo periodo, int? maquina, CancellationToken ct)
    {
        var somas = await SomasAsync(periodo, maquina, ct);
        long Quantidade(string tipo) => somas.Where(s => s.Tipo == tipo).Sum(s => s.Quantidade);
        long Eventos(string tipo) => somas.Where(s => s.Tipo == tipo).Sum(s => s.Eventos);

        var porDia = await Totais(periodo, maquina)
            .GroupBy(t => new { t.Dia, t.Tipo })
            .Select(g => new { g.Key.Dia, g.Key.Tipo, Quantidade = g.Sum(t => t.Quantidade), Eventos = g.Sum(t => t.Eventos) })
            .ToListAsync(ct);

        var inicio = periodo.Ate.AddDays(-(DiasMaximosDaSerie - 1)) > periodo.De ? periodo.Ate.AddDays(-(DiasMaximosDaSerie - 1)) : periodo.De;
        var serie = new List<DiaDeUso>();
        for (var dia = inicio; dia <= periodo.Ate; dia = dia.AddDays(1))
        {
            var doDia = porDia.Where(x => x.Dia == dia).ToList();
            long Q(string tipo) => doDia.Where(x => x.Tipo == tipo).Sum(x => x.Quantidade);
            long E(string tipo) => doDia.Where(x => x.Tipo == tipo).Sum(x => x.Eventos);
            serie.Add(new DiaDeUso(dia, E(TiposDeEvento.SessaoIniciada), E(TiposDeEvento.TurnoExecutado), Q(TiposDeEvento.FluxoExecutado),
                Q(TiposDeEvento.FerramentaAcionada), Q(TiposDeEvento.ComandoDelegado), Q(TiposDeEvento.ArquivoAlterado)));
        }

        return new QuantidadeResposta(
            periodo.De, periodo.Ate, Eventos(TiposDeEvento.SessaoIniciada), Eventos(TiposDeEvento.TurnoExecutado),
            Quantidade(TiposDeEvento.FluxoExecutado), Quantidade(TiposDeEvento.FerramentaAcionada),
            Quantidade(TiposDeEvento.ComandoDelegado), Quantidade(TiposDeEvento.ArquivoAlterado), serie);
    }

    public async Task<QualidadeResposta> QualidadeAsync(Periodo periodo, int? maquina, CancellationToken ct)
        => CalculoDeQualidade.Calcular(periodo, await SomasAsync(periodo, maquina, ct));

    public async Task<Pagina<EventoDoLog>> EventosAsync(Periodo periodo, int? maquina, int pagina, int tamanho, CancellationToken ct)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 200);
        var de = periodo.De.ToDateTime(TimeOnly.MinValue);
        var ate = periodo.Ate.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var consulta = db.Eventos.AsNoTracking()
            .Where(e => e.EmUtc >= de && e.EmUtc < ate && (maquina == null || e.MaquinaId == maquina));
        var total = await consulta.CountAsync(ct);
        var linhas = await consulta
            .OrderByDescending(e => e.EmUtc).ThenByDescending(e => e.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(e => new { e.EventId, e.MaquinaId, Apelido = e.Maquina!.Apelido, e.Tipo, e.SessaoId, e.Quantidade, e.Valor, e.Detalhe, e.EmUtc })
            .ToListAsync(ct);

        var itens = linhas
            .Select(e => new EventoDoLog(e.EventId, e.MaquinaId, e.Apelido, e.Tipo, e.SessaoId, e.Quantidade, e.Valor, e.Detalhe, DevKitPageDb.Utc(e.EmUtc)))
            .ToArray();
        return new Pagina<EventoDoLog>(itens, total, pagina, tamanho);
    }

    private IQueryable<TotalDiario> Totais(Periodo periodo, int? maquina)
        => db.TotaisDiarios.AsNoTracking()
            .Where(t => t.Dia >= periodo.De && t.Dia <= periodo.Ate && (maquina == null || t.MaquinaId == maquina));

    private async Task<IReadOnlyList<SomaPorTipo>> SomasAsync(Periodo periodo, int? maquina, CancellationToken ct)
        => await Totais(periodo, maquina)
            .GroupBy(t => new { t.Tipo, t.Detalhe })
            .Select(g => new SomaPorTipo(g.Key.Tipo, g.Key.Detalhe, g.Sum(t => t.Quantidade), g.Sum(t => t.Eventos), g.Sum(t => t.Valor)))
            .ToListAsync(ct);
}

/// <summary>
/// O expurgo dos eventos brutos além de <see cref="OpcoesDeTelemetria.RetencaoDias"/>. Os totais
/// diários não são tocados: já foram consolidados na ingestão.
/// </summary>
public sealed class ExpurgoDeEventos(DevKitPageDb db, IOptions<OpcoesDeTelemetria> opcoes, TimeProvider relogio) : IExpurgoDeEventos
{
    public Task<int> ExpurgarAsync(CancellationToken ct)
    {
        var corte = relogio.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, opcoes.Value.RetencaoDias));
        return db.Eventos.Where(e => e.EmUtc < corte).ExecuteDeleteAsync(ct);
    }
}
