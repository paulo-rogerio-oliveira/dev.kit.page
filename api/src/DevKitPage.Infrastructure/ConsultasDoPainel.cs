using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// As consultas do dashboard. Os números saem dos TOTAIS DIÁRIOS, somados no banco (o
/// <c>GroupBy</c> vira SQL); só o log de eventos e as ocorrências de erro leem os brutos.
/// <para>
/// O <see cref="EscopoDoPainel"/> (US #381) é aplicado AQUI, e só aqui: o gestor vê só as máquinas da
/// empresa dele que CONSENTIRAM, e delas só o uso a partir do consentimento — os totais diários desde o
/// dia dele (<see cref="TodosOsTotais"/>), o log e as ocorrências de erro desde o instante
/// (<see cref="Eventos"/>, <see cref="Ocorrencias"/>). O admin vê tudo; a exportação dos colaboradores
/// corta no consentimento também para ele.
/// </para>
/// </summary>
public sealed class ConsultasDoPainel(DevKitPageDb db) : IConsultasDoPainel
{
    /// <summary>O teto de dias preenchidos na série (um período maior é cortado no início).</summary>
    public const int DiasMaximosDaSerie = 400;

    public async Task<IReadOnlyList<MaquinaResumo>> MaquinasAsync(EscopoDoPainel escopo, CancellationToken ct)
    {
        var visiveis = Visiveis(escopo);
        var eventos = await TodosOsTotais(escopo)
            .GroupBy(t => t.MaquinaId)
            .Select(g => new { MaquinaId = g.Key, Eventos = g.Sum(t => t.Eventos) })
            .ToDictionaryAsync(x => x.MaquinaId, x => x.Eventos, ct);

        var maquinas = await visiveis.AsNoTracking().OrderBy(m => m.Apelido).ToListAsync(ct);
        return maquinas
            .Select(m => new MaquinaResumo(
                m.Id, m.MaquinaId, m.Apelido, m.VersaoDevKit, DevKitPageDb.Utc(m.RegistradaEmUtc),
                m.UltimoEnvioEmUtc is { } ultimo ? DevKitPageDb.Utc(ultimo) : null,
                eventos.GetValueOrDefault(m.Id)))
            .ToArray();
    }

    public Task<bool> MaquinaVisivelAsync(EscopoDoPainel escopo, int maquina, CancellationToken ct)
        => Visiveis(escopo).AnyAsync(m => m.Id == maquina, ct);

    public async Task<QuantidadeResposta> QuantidadeAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct)
    {
        var somas = await SomasAsync(escopo, periodo, maquina, ct);
        long Quantidade(string tipo) => somas.Where(s => s.Tipo == tipo).Sum(s => s.Quantidade);
        long Eventos(string tipo) => somas.Where(s => s.Tipo == tipo).Sum(s => s.Eventos);

        var porDia = await Totais(escopo, periodo, maquina)
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
        var ativas = await Totais(escopo, periodo, maquina).Select(t => t.MaquinaId).Distinct().CountAsync(ct);
        var fimDoPeriodo = periodo.Ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var registradas = await DoEscopo(escopo).CountAsync(m => m.RegistradaEmUtc < fimDoPeriodo && (maquina == null || m.Id == maquina), ct);

        return new QuantidadeResposta(
            periodo.De, periodo.Ate, Eventos(TiposDeEvento.SessaoIniciada), Eventos(TiposDeEvento.TurnoExecutado),
            Quantidade(TiposDeEvento.FluxoExecutado), Quantidade(TiposDeEvento.FerramentaAcionada),
            Quantidade(TiposDeEvento.ComandoDelegado), Quantidade(TiposDeEvento.ArquivoAlterado), serie, ativas, registradas);
    }

    public async Task<QualidadeResposta> QualidadeAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct)
        => CalculoDeQualidade.Calcular(periodo, await SomasAsync(escopo, periodo, maquina, ct));

    public async Task<Pagina<EventoDoLog>> EventosAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, int pagina, int tamanho, CancellationToken ct)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 200);
        var de = periodo.De.ToDateTime(TimeOnly.MinValue);
        var ate = periodo.Ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var consulta = Eventos(escopo)
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

    public async Task<Pagina<GrupoDeErroResumo>> ErrosAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, int pagina, int tamanho, CancellationToken ct)
    {
        pagina = Math.Max(1, pagina);
        tamanho = Math.Clamp(tamanho, 1, 100);

        var contagens = await ContagensDeErroAsync(Totais(escopo, periodo, maquina), ct);
        var assinaturas = contagens.Keys.ToList();
        var grupos = await db.GruposDeErro.AsNoTracking().Where(g => assinaturas.Contains(g.Assinatura)).ToListAsync(ct);

        var itens = grupos
            .OrderByDescending(g => g.UltimoVistoEmUtc).ThenByDescending(g => g.Id)
            .Skip((pagina - 1) * tamanho).Take(tamanho)
            .Select(g => Resumo(g, contagens[g.Assinatura]))
            .ToArray();
        return new Pagina<GrupoDeErroResumo>(itens, grupos.Count, pagina, tamanho);
    }

    public async Task<bool> ErroVisivelAsync(EscopoDoPainel escopo, long id, CancellationToken ct)
    {
        var assinatura = await db.GruposDeErro.Where(g => g.Id == id).Select(g => g.Assinatura).FirstOrDefaultAsync(ct);
        if (assinatura is null)
            return false;
        if (escopo.EhAdmin)
            return true;

        return await TodosOsTotais(escopo).AnyAsync(t => t.Tipo == TiposDeEvento.ExcecaoNaoClassificada && t.Detalhe == assinatura, ct);
    }

    public async Task<GrupoDeErroDetalhe?> ErroAsync(EscopoDoPainel escopo, long id, Periodo periodo, CancellationToken ct)
    {
        var grupo = await db.GruposDeErro.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grupo is null)
            return null;

        var totais = Totais(escopo, periodo, null).Where(t => t.Tipo == TiposDeEvento.ExcecaoNaoClassificada && t.Detalhe == grupo.Assinatura);
        var contagem = (await ContagensDeErroAsync(totais, ct)).GetValueOrDefault(grupo.Assinatura);
        var porDia = await totais
            .GroupBy(t => t.Dia)
            .Select(g => new OcorrenciasNoDia(g.Key, g.Sum(t => t.Eventos)))
            .ToListAsync(ct);

        // Os NOMES das máquinas e as ocorrências (com o trace) são dado individual: só as visíveis.
        var visiveis = Visiveis(escopo);
        var maquinas = await visiveis.AsNoTracking()
            .Where(m => totais.Select(t => t.MaquinaId).Contains(m.Id))
            .OrderBy(m => m.Apelido).Select(m => m.Apelido)
            .ToListAsync(ct);
        var ocorrencias = (await Ocorrencias(escopo)
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

    public async Task<IReadOnlyList<ColaboradorResumo>> ColaboradoresAsync(EscopoDoPainel escopo, CancellationToken ct)
        => (await Colaboradores(escopo).AsNoTracking()
                .OrderBy(m => m.Colaborador).ThenBy(m => m.Apelido)
                .Select(m => new { m.Id, m.Colaborador, m.Apelido, Empresa = m.Empresa!.Nome, m.VersaoDevKit, m.ConsentiuEmUtc, m.UltimoEnvioEmUtc })
                .ToListAsync(ct))
            .Select(m => new ColaboradorResumo(
                m.Id, m.Colaborador, m.Apelido, m.Empresa, m.VersaoDevKit, DevKitPageDb.Utc(m.ConsentiuEmUtc!.Value),
                m.UltimoEnvioEmUtc is { } ultimo ? DevKitPageDb.Utc(ultimo) : null))
            .ToArray();

    public async Task<IReadOnlyList<LinhaExportada>> ExportarAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct)
    {
        var colaboradores = await Colaboradores(escopo).AsNoTracking()
            .Where(m => maquina == null || m.Id == maquina)
            .Select(m => new { m.Id, m.Colaborador, m.Apelido, Empresa = m.Empresa!.Nome })
            .ToDictionaryAsync(m => m.Id, ct);
        // Só o uso a partir do dia do consentimento — também quando é o admin quem exporta.
        var porDia = await TotaisDosColaboradores(escopo, periodo, maquina)
            .GroupBy(t => new { t.MaquinaId, t.Dia, t.Tipo })
            .Select(g => new { g.Key.MaquinaId, g.Key.Dia, g.Key.Tipo, Quantidade = g.Sum(t => t.Quantidade), Eventos = g.Sum(t => t.Eventos) })
            .ToListAsync(ct);

        return porDia
            .GroupBy(x => (x.MaquinaId, x.Dia))
            .Select(g =>
            {
                long Q(string tipo) => g.Where(x => x.Tipo == tipo).Sum(x => x.Quantidade);
                long E(string tipo) => g.Where(x => x.Tipo == tipo).Sum(x => x.Eventos);
                var c = colaboradores[g.Key.MaquinaId];
                return new LinhaExportada(
                    c.Colaborador, c.Apelido, c.Empresa, g.Key.Dia, E(TiposDeEvento.SessaoIniciada), E(TiposDeEvento.TurnoExecutado),
                    E(TiposDeEvento.TurnoFalhou), Q(TiposDeEvento.FerramentaAcionada), Q(TiposDeEvento.ComandoDelegado),
                    Q(TiposDeEvento.ArquivoAlterado), E(TiposDeEvento.ObjetivoCumprido), E(TiposDeEvento.ObjetivoRecusado));
            })
            .OrderBy(l => l.Colaborador, StringComparer.CurrentCulture).ThenBy(l => l.Apelido, StringComparer.Ordinal).ThenBy(l => l.Dia)
            .ToArray();
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

    /// <summary>
    /// As máquinas do escopo: todas para o admin; para o gestor, as da empresa que CONSENTIRAM — sem o
    /// consentimento, nada da máquina vai para a empresa, nem nos totais.
    /// </summary>
    private IQueryable<Maquina> DoEscopo(EscopoDoPainel escopo)
    {
        var empresa = escopo.EmpresaId;
        return escopo.EhAdmin ? db.Maquinas : db.Maquinas.Where(m => m.EmpresaId == empresa && m.ConsentiuEmUtc != null);
    }

    /// <summary>As máquinas que quem consulta pode ver uma a uma (a lista, o filtro, os nomes): as mesmas do escopo.</summary>
    private IQueryable<Maquina> Visiveis(EscopoDoPainel escopo) => DoEscopo(escopo);

    /// <summary>Os colaboradores: as máquinas vinculadas a uma empresa, com consentimento, no escopo.</summary>
    private IQueryable<Maquina> Colaboradores(EscopoDoPainel escopo)
        => DoEscopo(escopo).Where(m => m.EmpresaId != null && m.ConsentiuEmUtc != null);

    private IQueryable<TotalDiario> Totais(EscopoDoPainel escopo, Periodo periodo, int? maquina)
        => TodosOsTotais(escopo).Where(t => t.Dia >= periodo.De && t.Dia <= periodo.Ate && (maquina == null || t.MaquinaId == maquina));

    /// <summary>
    /// Os totais diários do escopo, de qualquer período: tudo para o admin; para o gestor, os das máquinas
    /// que consentiram e SÓ a partir do dia do consentimento (<see cref="Maquina.DadosDesde"/>) — o uso de
    /// antes da adesão é da pessoa, e não da empresa.
    /// </summary>
    private IQueryable<TotalDiario> TodosOsTotais(EscopoDoPainel escopo)
    {
        var totais = db.TotaisDiarios.AsNoTracking();
        if (escopo.EhAdmin)
            return totais;

        var empresa = escopo.EmpresaId;
        return totais.Where(t => db.Maquinas.Any(m =>
            m.Id == t.MaquinaId && m.EmpresaId == empresa && m.ConsentiuEmUtc != null && m.DadosDesde != null && t.Dia >= m.DadosDesde));
    }

    /// <summary>Os colaboradores com o primeiro dia visível de cada um — o recorte da exportação, para o admin também.</summary>
    private IQueryable<TotalDiario> TotaisDosColaboradores(EscopoDoPainel escopo, Periodo periodo, int? maquina)
    {
        var colaboradores = Colaboradores(escopo);
        return db.TotaisDiarios.AsNoTracking()
            .Where(t => t.Dia >= periodo.De && t.Dia <= periodo.Ate && (maquina == null || t.MaquinaId == maquina))
            .Where(t => colaboradores.Any(m => m.Id == t.MaquinaId && m.DadosDesde != null && t.Dia >= m.DadosDesde));
    }

    /// <summary>
    /// Os eventos BRUTOS do escopo (o log): tudo para o admin; para o gestor, os das máquinas que
    /// consentiram e só a partir do INSTANTE do consentimento.
    /// </summary>
    private IQueryable<EventoDeUso> Eventos(EscopoDoPainel escopo)
    {
        var eventos = db.Eventos.AsNoTracking();
        if (escopo.EhAdmin)
            return eventos;

        var empresa = escopo.EmpresaId;
        return eventos.Where(e => db.Maquinas.Any(m =>
            m.Id == e.MaquinaId && m.EmpresaId == empresa && m.ConsentiuEmUtc != null && e.EmUtc >= m.ConsentiuEmUtc));
    }

    /// <summary>As ocorrências de erro do escopo, com o mesmo recorte do log: depois do instante do consentimento.</summary>
    private IQueryable<OcorrenciaDeErro> Ocorrencias(EscopoDoPainel escopo)
    {
        var ocorrencias = db.OcorrenciasDeErro.AsNoTracking();
        if (escopo.EhAdmin)
            return ocorrencias;

        var empresa = escopo.EmpresaId;
        return ocorrencias.Where(o => db.Maquinas.Any(m =>
            m.Id == o.MaquinaId && m.EmpresaId == empresa && m.ConsentiuEmUtc != null && o.EmUtc >= m.ConsentiuEmUtc));
    }

    private async Task<IReadOnlyList<SomaPorTipo>> SomasAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct)
        => await Totais(escopo, periodo, maquina)
            .GroupBy(t => new { t.Tipo, t.Detalhe })
            .Select(g => new SomaPorTipo(g.Key.Tipo, g.Key.Detalhe, g.Sum(t => t.Quantidade), g.Sum(t => t.Eventos), g.Sum(t => t.Valor)))
            .ToListAsync(ct);
}
