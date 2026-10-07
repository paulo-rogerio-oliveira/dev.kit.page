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

        // A exceção não classificada (US #381) é recortada pela ASSINATURA: é ela que vira o Detalhe,
        // e o total diário por (dia, tipo, assinatura) dá as ocorrências e as máquinas de cada grupo
        // sem tabela de contagem nova — e sobrevive ao expurgo, como o resto do histórico.
        var novos = unicos
            .Where(e => !existentes.Contains(e.EventId.Trim()))
            .Select(e => (Fonte: e, Evento: new EventoDeUso
            {
                EventId = e.EventId.Trim(),
                MaquinaId = maquinaId,
                Tipo = e.Tipo,
                SessaoId = ValidadorDeLote.Cortar(e.SessaoId),
                Quantidade = e.Quantidade,
                Valor = e.Valor,
                Detalhe = e.Tipo == TiposDeEvento.ExcecaoNaoClassificada
                    ? RegrasDeErro.Assinatura(e.Assinatura, RegrasDeErro.Mascarar(e.Trace))
                    : ValidadorDeLote.Cortar(e.Detalhe),
                EmUtc = e.Em.UtcDateTime,
                Dia = DateOnly.FromDateTime(e.Em.UtcDateTime),
                RecebidoEmUtc = agora,
            }))
            .ToList();
        var eventos = novos.Select(n => n.Evento).ToList();
        var versao = Versao(lote.VersaoDevKit);

        await using var transacao = await db.Database.BeginTransactionAsync(ct);
        db.Eventos.AddRange(eventos);
        await ConsolidarAsync(maquinaId, eventos, ct);
        var grupos = await AgruparErrosAsync(maquinaId, versao, novos.Where(n => n.Evento.Tipo == TiposDeEvento.ExcecaoNaoClassificada).ToList(), ct);

        var maquina = await db.Maquinas.FirstAsync(m => m.Id == maquinaId, ct);
        maquina.UltimoEnvioEmUtc = agora;
        if (!string.IsNullOrWhiteSpace(lote.VersaoDevKit))
            maquina.VersaoDevKit = ValidadorDeLote.Cortar(lote.VersaoDevKit);

        await db.SaveChangesAsync(ct);
        await ManterSoAsUltimasOcorrenciasAsync(grupos, ct);
        await transacao.CommitAsync(ct);

        return new BatchResultV1(lote.Eventos.Count, novos.Count, conhecidos.Count - novos.Count, lote.Eventos.Count - conhecidos.Count);
    }

    /// <summary>A versão do dev.kit no tamanho da coluna dos grupos e das ocorrências.</summary>
    private static string Versao(string? versao)
    {
        var limpa = (versao ?? string.Empty).Trim();
        return limpa.Length <= 50 ? limpa : limpa[..50];
    }

    /// <summary>
    /// O upsert dos grupos de exceção (US #381), na MESMA transação do evento: o grupo novo nasce
    /// <c>Novo</c>; o que já existe avança o último visto e, se estava resolvido e voltou numa versão
    /// igual ou maior que a da correção, REGRIDE. Cada ocorrência guarda o trace mascarado de novo
    /// (<see cref="RegrasDeErro.Mascarar"/> — a defesa em profundidade).
    /// </summary>
    private async Task<IReadOnlyCollection<GrupoDeErro>> AgruparErrosAsync(
        int maquinaId, string versao, IReadOnlyCollection<(TelemetryEventV1 Fonte, EventoDeUso Evento)> excecoes, CancellationToken ct)
    {
        if (excecoes.Count == 0)
            return Array.Empty<GrupoDeErro>();

        var assinaturas = excecoes.Select(x => x.Evento.Detalhe).Distinct().ToList();
        var grupos = await db.GruposDeErro.Where(g => assinaturas.Contains(g.Assinatura)).ToDictionaryAsync(g => g.Assinatura, StringComparer.Ordinal, ct);

        foreach (var (fonte, evento) in excecoes.OrderBy(x => x.Evento.EmUtc))
        {
            var trace = RegrasDeErro.Mascarar(fonte.Trace);
            if (!grupos.TryGetValue(evento.Detalhe, out var grupo))
            {
                grupo = new GrupoDeErro
                {
                    Assinatura = evento.Detalhe,
                    Tipo = RegrasDeErro.Tipo(trace),
                    Estado = EstadosDoGrupo.Novo,
                    PrimeiraVersao = versao,
                    UltimaVersao = versao,
                    PrimeiroVistoEmUtc = evento.EmUtc,
                    UltimoVistoEmUtc = evento.EmUtc,
                };
                db.GruposDeErro.Add(grupo);
                grupos[grupo.Assinatura] = grupo;
            }
            else
            {
                grupo.Estado = RegrasDeErro.EstadoAoReceber(grupo.Estado, grupo.ResolvidoNaVersao, versao);
                if (evento.EmUtc >= grupo.UltimoVistoEmUtc)
                {
                    grupo.UltimoVistoEmUtc = evento.EmUtc;
                    grupo.UltimaVersao = versao;
                }

                if (evento.EmUtc < grupo.PrimeiroVistoEmUtc)
                    grupo.PrimeiroVistoEmUtc = evento.EmUtc;
            }

            db.OcorrenciasDeErro.Add(new OcorrenciaDeErro
            {
                Grupo = grupo,
                MaquinaId = maquinaId,
                EventId = evento.EventId,
                VersaoDevKit = versao,
                Trace = trace,
                EmUtc = evento.EmUtc,
            });
        }

        return grupos.Values;
    }

    /// <summary>Cada grupo guarda só as <see cref="RegrasDeErro.OcorrenciasGuardadasPorGrupo"/> ocorrências mais recentes.</summary>
    private async Task ManterSoAsUltimasOcorrenciasAsync(IReadOnlyCollection<GrupoDeErro> grupos, CancellationToken ct)
    {
        foreach (var grupo in grupos)
        {
            var sobrando = await db.OcorrenciasDeErro
                .Where(o => o.GrupoId == grupo.Id)
                .OrderByDescending(o => o.EmUtc).ThenByDescending(o => o.Id)
                .Skip(RegrasDeErro.OcorrenciasGuardadasPorGrupo)
                .Select(o => o.Id)
                .ToListAsync(ct);
            if (sobrando.Count > 0)
                await db.OcorrenciasDeErro.Where(o => sobrando.Contains(o.Id)).ExecuteDeleteAsync(ct);
        }
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

        // As máquinas (US #381): as ATIVAS são as que têm total no período — a mesma fonte dos números
        // acima, então o expurgo dos brutos não as apaga; as REGISTRADAS são as que já existiam no fim dele.
        var ativas = await Totais(periodo, maquina).Select(t => t.MaquinaId).Distinct().CountAsync(ct);
        var fimDoPeriodo = periodo.Ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var registradas = await db.Maquinas.CountAsync(m => m.RegistradaEmUtc < fimDoPeriodo && (maquina == null || m.Id == maquina), ct);

        return new QuantidadeResposta(
            periodo.De, periodo.Ate, Eventos(TiposDeEvento.SessaoIniciada), Eventos(TiposDeEvento.TurnoExecutado),
            Quantidade(TiposDeEvento.FluxoExecutado), Quantidade(TiposDeEvento.FerramentaAcionada),
            Quantidade(TiposDeEvento.ComandoDelegado), Quantidade(TiposDeEvento.ArquivoAlterado), serie, ativas, registradas);
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

    public async Task<Pagina<GrupoDeErroResumo>> ErrosAsync(Periodo periodo, int? maquina, int pagina, int tamanho, CancellationToken ct)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 100);

        var contagens = await ContagensDeErroAsync(Totais(periodo, maquina), ct);
        var assinaturas = contagens.Keys.ToList();
        var grupos = await db.GruposDeErro.AsNoTracking().Where(g => assinaturas.Contains(g.Assinatura)).ToListAsync(ct);

        var itens = grupos
            .OrderByDescending(g => g.UltimoVistoEmUtc).ThenByDescending(g => g.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(g => Resumo(g, contagens[g.Assinatura]))
            .ToArray();
        return new Pagina<GrupoDeErroResumo>(itens, grupos.Count, pagina, tamanho);
    }

    public async Task<GrupoDeErroDetalhe?> ErroAsync(long id, Periodo periodo, CancellationToken ct)
    {
        var grupo = await db.GruposDeErro.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grupo is null)
            return null;

        var totais = Totais(periodo, null).Where(t => t.Tipo == TiposDeEvento.ExcecaoNaoClassificada && t.Detalhe == grupo.Assinatura);
        var contagem = (await ContagensDeErroAsync(totais, ct)).GetValueOrDefault(grupo.Assinatura);
        var porDia = await totais
            .GroupBy(t => t.Dia)
            .Select(g => new OcorrenciasNoDia(g.Key, g.Sum(t => t.Eventos)))
            .ToListAsync(ct);
        var maquinas = await db.Maquinas.AsNoTracking()
            .Where(m => totais.Select(t => t.MaquinaId).Contains(m.Id))
            .OrderBy(m => m.Apelido).Select(m => m.Apelido)
            .ToListAsync(ct);
        var ocorrencias = (await db.OcorrenciasDeErro.AsNoTracking()
                .Where(o => o.GrupoId == id)
                .OrderByDescending(o => o.EmUtc).ThenByDescending(o => o.Id)
                .Take(RegrasDeErro.OcorrenciasGuardadasPorGrupo)
                .Select(o => new { o.EventId, Apelido = o.Maquina!.Apelido, o.VersaoDevKit, o.Trace, o.EmUtc })
                .ToListAsync(ct))
            .Select(o => new OcorrenciaDeErroResumo(o.EventId, o.Apelido, o.VersaoDevKit, o.Trace, DevKitPageDb.Utc(o.EmUtc)))
            .ToArray();
        var versoes = ocorrencias.Select(o => o.VersaoDevKit).Append(grupo.PrimeiraVersao).Append(grupo.UltimaVersao)
            .Where(v => v.Length > 0).Distinct(StringComparer.Ordinal)
            .OrderBy(v => v, Comparer<string>.Create(RegrasDeErro.CompararVersoes))
            .ToArray();

        return new GrupoDeErroDetalhe(
            Resumo(grupo, contagem), ocorrencias.FirstOrDefault()?.Trace ?? string.Empty, ocorrencias,
            porDia.OrderBy(d => d.Dia).ToArray(), versoes, maquinas);
    }

    /// <summary>Ocorrências e máquinas distintas por assinatura, somadas no banco a partir dos totais diários.</summary>
    private static async Task<Dictionary<string, (long Ocorrencias, int Maquinas)>> ContagensDeErroAsync(IQueryable<TotalDiario> totais, CancellationToken ct)
        => (await totais
                .Where(t => t.Tipo == TiposDeEvento.ExcecaoNaoClassificada)
                .GroupBy(t => t.Detalhe)
                .Select(g => new { Assinatura = g.Key, Ocorrencias = g.Sum(t => t.Eventos), Maquinas = g.Select(t => t.MaquinaId).Distinct().Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.Assinatura, x => (x.Ocorrencias, x.Maquinas), StringComparer.Ordinal);

    private static GrupoDeErroResumo Resumo(GrupoDeErro g, (long Ocorrencias, int Maquinas) contagem)
        => new(g.Id, g.Assinatura, g.Tipo, g.Estado, g.ResolvidoNaVersao, contagem.Ocorrencias, contagem.Maquinas,
            g.PrimeiraVersao, g.UltimaVersao, DevKitPageDb.Utc(g.PrimeiroVistoEmUtc), DevKitPageDb.Utc(g.UltimoVistoEmUtc));

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

/// <summary>
/// A reação a um grupo de exceção (US #381). Resolver guarda a versão da correção — a base da
/// regressão na ingestão —; qualquer outro estado a esquece.
/// </summary>
public sealed class ReacaoAErros(DevKitPageDb db) : IReacaoAErros
{
    public async Task<bool> AlterarEstadoAsync(long id, AlterarEstadoDoGrupo pedido, CancellationToken ct)
    {
        var grupo = await db.GruposDeErro.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grupo is null)
            return false;

        var versao = (pedido.Versao ?? string.Empty).Trim();
        grupo.Estado = pedido.Estado;
        grupo.ResolvidoNaVersao = pedido.Estado == EstadosDoGrupo.Resolvido && versao.Length > 0 ? versao : null;
        await db.SaveChangesAsync(ct);
        return true;
    }
}

/// <summary>
/// O expurgo das ocorrências de erro (o trace de cada uma) além de <see cref="OpcoesDeTelemetria.RetencaoDias"/>.
/// O grupo fica — com o estado da reação —, e as contagens vêm dos totais diários.
/// </summary>
public sealed class ExpurgoDeOcorrencias(DevKitPageDb db, IOptions<OpcoesDeTelemetria> opcoes, TimeProvider relogio) : IExpurgoDeOcorrencias
{
    public Task<int> ExpurgarAsync(CancellationToken ct)
    {
        var corte = relogio.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, opcoes.Value.RetencaoDias));
        return db.OcorrenciasDeErro.Where(o => o.EmUtc < corte).ExecuteDeleteAsync(ct);
    }
}
