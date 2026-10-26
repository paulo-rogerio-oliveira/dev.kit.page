using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O painel de UTILIDADE (US #687) — a parte das <see cref="ConsultasDoPainel"/> que responde "o que está travando agora,
/// e o que o uso entregou". Reaproveita o que já existe: o escopo (o mesmo recorte do gestor, num ponto só), os totais
/// diários somados no banco e os eventos brutos do log. O que é novo são as FOTOS (<see cref="FotoDeEstado"/>) da saúde.
/// </summary>
public sealed partial class ConsultasDoPainel
{
    /// <summary>Os tipos dos eventos brutos que o resultado por sessão lê.</summary>
    private static readonly string[] TiposDoResultado =
    [
        TiposDeEvento.SessaoIniciada, TiposDeEvento.TurnoExecutado, TiposDeEvento.RevisaoHumana, TiposDeEvento.ObjetivoAvaliado,
        TiposDeEvento.ObjetivoCumprido,
    ];

    /// <summary>Os tipos dos totais que o gráfico de turnos por desfecho lê (o desfecho, e a conta de antes dele).</summary>
    private static readonly string[] TiposDosTurnos = [TiposDeEvento.DesfechoDoTurno, TiposDeEvento.TurnoExecutado, TiposDeEvento.TurnoFalhou];

    public async Task<UtilidadeResposta> UtilidadeAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, DateTimeOffset agora, CancellationToken ct)
    {
        var saude = await SaudeAsync(escopo, maquina, agora, ct);
        var resultado = await ResultadoAsync(escopo, periodo, maquina, ct);
        var turnos = await TurnosPorDesfechoAsync(escopo, periodo, maquina, ct);

        var de = periodo.De.ToDateTime(TimeOnly.MinValue);
        var ate = periodo.Ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var sugestoes = (await Fotos(escopo)
                .Where(f => f.Tipo == TiposDeEvento.SugestaoDeImpacto && f.EmUtc >= de && f.EmUtc < ate && (maquina == null || f.MaquinaId == maquina))
                .Select(f => f.Detalhe)
                .ToListAsync(ct))
            .Select(RegrasDoPainelDeUtilidade.LerSugestao)
            .OfType<SugestaoLida>();

        return new UtilidadeResposta(periodo.De, periodo.Ate, saude, resultado, turnos, RegrasDoPainelDeUtilidade.OndeTravam(sugestoes));
    }

    /// <summary>
    /// A saúde de agora, das fotos: as tasks paradas sem processo, as perguntas abertas, os fechamentos cujo último desfecho
    /// foi a falha e as máquinas cuja amostra das últimas 24 h está no limite. Não depende do período.
    /// </summary>
    private async Task<SaudeAgoraV1> SaudeAsync(EscopoDoPainel escopo, int? maquina, DateTimeOffset agora, CancellationToken ct)
    {
        string[] tipos = [TiposDeEvento.SituacaoDaTask, TiposDeEvento.PerguntaAoDev, TiposDeEvento.FechamentoDaEntrega, TiposDeEvento.AmostraDaMaquina];
        var fotos = await Fotos(escopo)
            .Where(f => tipos.Contains(f.Tipo) && (maquina == null || f.MaquinaId == maquina))
            .Select(f => new { f.Tipo, f.Chave, f.Detalhe, f.EmUtc, f.Maquina!.Apelido })
            .ToListAsync(ct);

        var limite24h = agora.UtcDateTime - RegrasDoPainelDeUtilidade.AmostraValidaPor;
        CartaoDeSaudeV1 Cartao(IEnumerable<DateTime> desde, IReadOnlyList<string> itens)
        {
            var lista = desde.ToList();
            return new CartaoDeSaudeV1(lista.Count, lista.Count(d => d >= limite24h), lista.Count == 0 ? null : DevKitPageDb.Utc(lista.Min()), itens);
        }

        var paradas = fotos.Where(f => f.Tipo == TiposDeEvento.SituacaoDaTask && f.Detalhe == SituacoesDaTask.ParadaSemProcesso).ToList();
        var perguntas = fotos.Where(f => f.Tipo == TiposDeEvento.PerguntaAoDev && f.Detalhe == EstadosDaPergunta.Aberta).ToList();
        var fechamentos = fotos.Where(f => f.Tipo == TiposDeEvento.FechamentoDaEntrega && f.Detalhe.StartsWith("falhou", StringComparison.Ordinal))
            .OrderByDescending(f => f.EmUtc).ToList();
        var maquinas = fotos
            .Where(f => f.Tipo == TiposDeEvento.AmostraDaMaquina && f.EmUtc >= limite24h)
            .Select(f => (Foto: f, Amostra: RegrasDoPainelDeUtilidade.LerAmostra(f.Detalhe)))
            .Where(x => x.Amostra is { NoLimite: true })
            .ToList();

        return new SaudeAgoraV1(
            Cartao(paradas.Select(f => f.EmUtc), []),
            Cartao(perguntas.Select(f => f.EmUtc), []),
            Cartao(fechamentos.Select(f => f.EmUtc), fechamentos.Select(f => f.Chave).Where(c => c.StartsWith('#')).Distinct().Take(5).ToArray()),
            Cartao(maquinas.Select(x => x.Foto.EmUtc), maquinas
                .Select(x => $"{x.Foto.Apelido}: disco {Megas(x.Amostra!.DiscoMb)} · memória {Megas(x.Amostra.MemoriaMb)} livres")
                .Take(5).ToArray()));
    }

    /// <summary>O resultado do período e a variação contra o período anterior de mesmo tamanho.</summary>
    private async Task<ResultadoDoUsoV1> ResultadoAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct)
    {
        var somas = await SomasAsync(escopo, periodo, maquina, ct);
        long Eventos(string tipo, string? detalhe = null) => somas.Where(s => s.Tipo == tipo && (detalhe is null || s.Detalhe == detalhe)).Sum(s => s.Eventos);
        long Quantidade(string tipo) => somas.Where(s => s.Tipo == tipo).Sum(s => s.Quantidade);
        long Aprovadas(Func<string, string?, long> eventos) => eventos(TiposDeEvento.RevisaoHumana, "aprovada") + eventos(TiposDeEvento.RevisaoHumana, "commit");

        var aprovadas = Aprovadas(Eventos);
        var inicioDaSemana = periodo.Ate.AddDays(-6) > periodo.De ? periodo.Ate.AddDays(-6) : periodo.De;
        var semana = await SomasAsync(escopo, new Periodo(inicioDaSemana, periodo.Ate), maquina, ct);
        var aprovadasNaSemana = Aprovadas((tipo, detalhe) => semana.Where(s => s.Tipo == tipo && s.Detalhe == detalhe).Sum(s => s.Eventos));
        var avaliacoes = somas.Where(s => s.Tipo == TiposDeEvento.ObjetivoAvaliado).ToList();
        double? notaMedia = avaliacoes.Sum(s => s.Eventos) is > 0 and var n ? Math.Round(avaliacoes.Sum(s => s.Valor) / (double)n, 1) : null;
        var turnos = Eventos(TiposDeEvento.TurnoExecutado);

        var dias = periodo.Ate.DayNumber - periodo.De.DayNumber + 1;
        var anterior = new Periodo(periodo.De.AddDays(-dias), periodo.De.AddDays(-1));
        var agora = RegrasDoPainelDeUtilidade.Sessoes(await EventosDasSessoesAsync(escopo, periodo, maquina, ct));
        var antes = RegrasDoPainelDeUtilidade.Sessoes(await EventosDasSessoesAsync(escopo, anterior, maquina, ct));

        double? NaPrimeira(ResultadoDasSessoes r) => r.Aprovadas == 0 ? null : Math.Round(100d * r.NaPrimeira / r.Aprovadas, 1);
        var naPrimeira = NaPrimeira(agora);
        var naPrimeiraAntes = NaPrimeira(antes);
        var mediana = RegrasDoPainelDeUtilidade.Mediana(agora.MinutosAteAprovar);
        var medianaAntes = RegrasDoPainelDeUtilidade.Mediana(antes.MinutosAteAprovar);
        var total = agora.MinutosAteAprovar.Sum();

        return new ResultadoDoUsoV1(
            aprovadas, Eventos(TiposDeEvento.FechamentoDaEntrega, "ok"), aprovadasNaSemana,
            naPrimeira, naPrimeira is { } p && naPrimeiraAntes is { } pa ? Math.Round(p - pa, 1) : null,
            notaMedia, agora.Rodadas.Count == 0 ? null : Math.Round(agora.Rodadas.Average(), 1),
            mediana is { } m ? Math.Round(m, 0) : null, mediana is { } m1 && medianaAntes is { } m0 ? Math.Round(m1 - m0, 0) : null,
            total > 0 ? Math.Round(Math.Min(100, 100 * agora.MinutosEsperandoDev.Sum() / total), 0) : null,
            aprovadas == 0 ? null : Math.Round(turnos / (double)aprovadas, 1), turnos, Quantidade(TiposDeEvento.ComandoDelegado),
            Quantidade(TiposDeEvento.ArquivoAlterado));
    }

    /// <summary>Os eventos brutos que o resultado por sessão lê, com a sessão qualificada pela máquina.</summary>
    private async Task<IReadOnlyList<EventoDaSessao>> EventosDasSessoesAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct)
    {
        var de = periodo.De.ToDateTime(TimeOnly.MinValue);
        var ate = periodo.Ate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return (await Eventos(escopo)
                .Where(e => e.EmUtc >= de && e.EmUtc < ate && (maquina == null || e.MaquinaId == maquina) && e.SessaoId != "" && TiposDoResultado.Contains(e.Tipo))
                .Select(e => new { e.MaquinaId, e.SessaoId, e.Tipo, e.Detalhe, e.Valor, e.EmUtc })
                .ToListAsync(ct))
            .Select(e => new EventoDaSessao($"{e.MaquinaId}:{e.SessaoId}", e.Tipo, e.Detalhe, e.Valor, e.EmUtc))
            .ToArray();
    }

    /// <summary>
    /// Os turnos de cada dia do período pelo desfecho (os dias sem turno entram zerados, como na série de uso). A conta é
    /// por MÁQUINA e dia, e depois somada: numa frota com o dev.kit novo (que manda o desfecho) e o antigo (que não manda),
    /// cada máquina entra pela regra dela.
    /// </summary>
    private async Task<IReadOnlyList<TurnosDoDiaV1>> TurnosPorDesfechoAsync(EscopoDoPainel escopo, Periodo periodo, int? maquina, CancellationToken ct)
    {
        var totais = await Totais(escopo, periodo, maquina)
            .Where(t => TiposDosTurnos.Contains(t.Tipo))
            .GroupBy(t => new { t.MaquinaId, t.Dia, t.Tipo, t.Detalhe })
            .Select(g => new { g.Key.MaquinaId, g.Key.Dia, g.Key.Tipo, g.Key.Detalhe, Eventos = g.Sum(t => t.Eventos) })
            .ToListAsync(ct);

        var inicio = periodo.Ate.AddDays(-(DiasMaximosDaSerie - 1)) > periodo.De ? periodo.Ate.AddDays(-(DiasMaximosDaSerie - 1)) : periodo.De;
        var serie = new List<TurnosDoDiaV1>();
        for (var dia = inicio; dia <= periodo.Ate; dia = dia.AddDays(1))
        {
            var porMaquina = totais.Where(t => t.Dia == dia).GroupBy(t => t.MaquinaId)
                .Select(g => RegrasDoPainelDeUtilidade.TurnosDoDia(dia, g.Select(t => (t.Tipo, t.Detalhe, t.Eventos)).ToArray()))
                .ToList();
            serie.Add(new TurnosDoDiaV1(dia, porMaquina.Sum(t => t.Trabalhou), porMaquina.Sum(t => t.EsperouDev), porMaquina.Sum(t => t.LimiteDeUso),
                porMaquina.Sum(t => t.Falhou)));
        }

        return serie;
    }

    /// <summary>
    /// As fotos do escopo, com o recorte do log: tudo para o admin; para o gestor, as das máquinas que consentiram e tiradas
    /// a partir do INSTANTE do consentimento.
    /// </summary>
    private IQueryable<FotoDeEstado> Fotos(EscopoDoPainel escopo)
    {
        var fotos = db.FotosDeEstado.AsNoTracking();
        if (escopo.EhAdmin)
            return fotos;

        var empresa = escopo.EmpresaId;
        return fotos.Where(f => db.Maquinas.Any(m =>
            m.Id == f.MaquinaId && m.EmpresaId == empresa && m.ConsentiuEmUtc != null && f.EmUtc >= m.ConsentiuEmUtc));
    }

    /// <summary>"8 MB", "1,2 GB" — o tamanho como o cartão o escreve; o desconhecido é um traço.</summary>
    private static string Megas(long mb)
        => mb < 0 ? "—" : mb < 1024 ? $"{mb} MB" : (mb / 1024d).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " GB";
}
