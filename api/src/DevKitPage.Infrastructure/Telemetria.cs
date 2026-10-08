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
public sealed class IngestaoDeTelemetria(DevKitPageDb db, TimeProvider relogio, IOptions<OpcoesDeTelemetria> opcoes) : IIngestaoDeTelemetria
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
        await AtualizarRoisAsync(maquinaId, novos.Where(n => n.Evento.Tipo == TiposDeEvento.RoiCalculado).ToList(), ct);

        var maquina = await db.Maquinas.FirstAsync(m => m.Id == maquinaId, ct);
        maquina.UltimoEnvioEmUtc = agora;
        if (!string.IsNullOrWhiteSpace(lote.VersaoDevKit))
            maquina.VersaoDevKit = ValidadorDeLote.Cortar(lote.VersaoDevKit);

        await db.SaveChangesAsync(ct);
        await ManterSoAsUltimasOcorrenciasAsync(maquinaId, grupos, ct);
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

    /// <summary>
    /// O upsert das fotos do ROI (US #387), na MESMA transação do evento: a foto é uma por (máquina, work
    /// item), e só um evento MAIS NOVO (pelo <c>em</c>) a substitui — o evento antigo que chega atrasado
    /// (a fila de outra instância do dev.kit, um reenvio fora de ordem) conta no total diário, mas não
    /// volta a foto para trás. Só os eventos NOVOS passam aqui: o reenvio (o mesmo eventId) nem chega.
    /// O evento sem <c>roi</c> (um cliente com defeito) ou com a foto inválida não cria foto nenhuma.
    /// </summary>
    private async Task AtualizarRoisAsync(
        int maquinaId, IReadOnlyCollection<(TelemetryEventV1 Fonte, EventoDeUso Evento)> rois, CancellationToken ct)
    {
        var fotos = rois
            .Select(x => (Foto: RegrasDeRoi.Foto(x.Fonte.Roi), x.Evento))
            .Where(x => x.Foto is not null)
            .ToList();
        if (fotos.Count == 0)
            return;

        var itens = fotos.Select(x => x.Foto!.WorkItemId).Distinct().ToList();
        var existentes = await db.RoisDeWorkItem
            .Where(r => r.MaquinaId == maquinaId && itens.Contains(r.WorkItemId))
            .ToDictionaryAsync(r => r.WorkItemId, ct);

        // Em ordem de instante: dentro do mesmo lote, o último também vence.
        foreach (var (foto, evento) in fotos.OrderBy(x => x.Evento.EmUtc))
        {
            if (existentes.TryGetValue(foto!.WorkItemId, out var atual))
            {
                if (evento.EmUtc < atual.EmUtc)
                    continue;
            }
            else
            {
                atual = new RoiDeWorkItem { MaquinaId = maquinaId, WorkItemId = foto.WorkItemId };
                db.RoisDeWorkItem.Add(atual);
                existentes[foto.WorkItemId] = atual;
            }

            atual.Tipo = foto.Tipo;
            atual.Estado = foto.Estado;
            atual.De = foto.De;
            atual.Ate = foto.Ate;
            atual.TurnosDoAgente = foto.TurnosDoAgente;
            atual.Sessoes = foto.Sessoes;
            atual.Horas = foto.Horas;
            atual.HorasNoBoard = foto.HorasNoBoard;
            atual.HorasNoTimesheet = foto.HorasNoTimesheet;
            atual.LeadTimeDias = foto.LeadTimeDias;
            atual.Aberto = foto.Aberto;
            atual.PullRequests = foto.PullRequests;
            atual.PullRequestsMergeadas = foto.PullRequestsMergeadas;
            atual.EmUtc = evento.EmUtc;
            atual.EventId = evento.EventId;
        }
    }

    /// <summary>
    /// Cada grupo guarda só as <see cref="RegrasDeErro.OcorrenciasGuardadasPorGrupo"/> ocorrências mais
    /// recentes, cada empresa no máximo <see cref="OpcoesDeTelemetria.MaxOcorrenciasPorEmpresa"/> e a base
    /// inteira no máximo <see cref="OpcoesDeTelemetria.MaxOcorrenciasGuardadas"/>.
    /// </summary>
    private async Task ManterSoAsUltimasOcorrenciasAsync(int maquinaId, IReadOnlyCollection<GrupoDeErro> grupos, CancellationToken ct)
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

        if (grupos.Count == 0)
            return;

        // O teto POR EMPRESA primeiro (as máquinas anônimas são um grupo só): quem gera exceção em laço
        // descarta as próprias ocorrências mais antigas, nunca as de outra empresa.
        var empresa = await db.Maquinas.Where(m => m.Id == maquinaId).Select(m => m.EmpresaId).FirstAsync(ct);
        var daEmpresa = db.OcorrenciasDeErro.Where(o => o.Maquina!.EmpresaId == empresa);
        var tetoDaEmpresa = Math.Max(RegrasDeErro.OcorrenciasGuardadasPorGrupo, opcoes.Value.MaxOcorrenciasPorEmpresa);
        var excedentesDaEmpresa = await daEmpresa.CountAsync(ct) - tetoDaEmpresa;
        if (excedentesDaEmpresa > 0)
        {
            var antigasDaEmpresa = await daEmpresa
                .OrderBy(o => o.EmUtc).ThenBy(o => o.Id)
                .Take(excedentesDaEmpresa)
                .Select(o => o.Id)
                .ToListAsync(ct);
            await db.OcorrenciasDeErro.Where(o => antigasDaEmpresa.Contains(o.Id)).ExecuteDeleteAsync(ct);
        }

        // O teto GLOBAL: muitas empresas juntas também não enchem a base. Saem as mais antigas de todas;
        // os grupos e as contagens nos totais diários ficam.
        var teto = Math.Max(RegrasDeErro.OcorrenciasGuardadasPorGrupo, opcoes.Value.MaxOcorrenciasGuardadas);
        var excedentes = await db.OcorrenciasDeErro.CountAsync(ct) - teto;
        if (excedentes <= 0)
            return;
        var maisAntigas = await db.OcorrenciasDeErro
            .OrderBy(o => o.EmUtc).ThenBy(o => o.Id)
            .Take(excedentes)
            .Select(o => o.Id)
            .ToListAsync(ct);
        await db.OcorrenciasDeErro.Where(o => maisAntigas.Contains(o.Id)).ExecuteDeleteAsync(ct);
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
