using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// A licença do dev.kit de cada usuário na gestão do ADMIN (US #687): a de agora com o histórico, a prévia da validade
/// nova, a definição (somar dias, até uma data, sem prazo — com o tipo e o motivo) e a revogação. A renovação do dev.kit
/// (<see cref="ILicencas"/>) respeita o que fica gravado aqui.
/// </summary>
public interface ILicencasDosUsuarios
{
    /// <summary>A licença e o histórico (as ações do admin e as renovações do dev.kit, do mais novo ao mais antigo).</summary>
    Task<ResultadoDaGestao<DetalheDaLicencaV1>> DetalheAsync(int usuarioId, CancellationToken ct);

    /// <summary>A validade que a definição JÁ validada produziria — sem gravar nada.</summary>
    Task<ResultadoDaGestao<PreviaDeLicencaV1>> PreviaAsync(int usuarioId, DefinicaoDeLicencaV1 definicao, CancellationToken ct);

    /// <summary>Aplica a definição JÁ validada (com o motivo): grava a validade, o tipo e a linha do histórico.</summary>
    Task<ResultadoDaGestao<DetalheDaLicencaV1>> DefinirAsync(int usuarioId, DefinicaoDeLicencaV1 definicao, string autor, CancellationToken ct);

    /// <summary>Revoga a licença: a renovação para até o admin definir uma validade nova.</summary>
    Task<ResultadoDaGestao<DetalheDaLicencaV1>> RevogarAsync(int usuarioId, string motivo, string autor, CancellationToken ct);
}

/// <summary>A amostra de recursos lida do recorte do <see cref="TiposDeEvento.AmostraDaMaquina"/>.</summary>
public sealed record AmostraLida(long DiscoMb, long MemoriaMb, string Limite)
{
    /// <summary>O disco ou a memória está no limite.</summary>
    public bool NoLimite => Limite is { Length: > 0 } && Limite != RegrasDoPainelDeUtilidade.SemLimite;
}

/// <summary>A sugestão de impacto lida do recorte do <see cref="TiposDeEvento.SugestaoDeImpacto"/>.</summary>
public sealed record SugestaoLida(string Causa, long Id, string Estado, string Us);

/// <summary>Um evento bruto de uma sessão, o que o cálculo do resultado lê.</summary>
public sealed record EventoDaSessao(string Sessao, string Tipo, string Detalhe, long? Valor, DateTime EmUtc);

/// <summary>O resultado das sessões de um período: a matéria-prima do <see cref="ResultadoDoUsoV1"/>.</summary>
/// <param name="Aprovadas">Sessões aprovadas pelo dev (a revisão humana aprovada ou commit).</param>
/// <param name="NaPrimeira">Delas, as aprovadas sem devolução antes.</param>
/// <param name="Rodadas">As avaliações de cada sessão com o objetivo cumprido.</param>
/// <param name="MinutosAteAprovar">Do começo da sessão (o primeiro evento dela) à primeira aprovação, por sessão.</param>
/// <param name="MinutosEsperandoDev">Por sessão aprovada, a espera do dev (o valor das revisões até a aprovação).</param>
public sealed record ResultadoDasSessoes(int Aprovadas, int NaPrimeira, IReadOnlyList<int> Rodadas, IReadOnlyList<double> MinutosAteAprovar, IReadOnlyList<double> MinutosEsperandoDev);

/// <summary>
/// As regras PURAS do painel de utilidade (US #687): ler os recortes dos eventos novos (sem campo novo no wire v1), a chave
/// de cada foto, os turnos do dia pelo desfecho — com a conta de antes para o dev.kit que ainda não manda o desfecho — e o
/// resultado por sessão.
/// </summary>
public static class RegrasDoPainelDeUtilidade
{
    /// <summary>O <c>limite</c> da amostra quando nada está no limite.</summary>
    public const string SemLimite = "nao";

    /// <summary>A amostra mais velha que isto não conta no cartão: a máquina desligada não está "no limite" agora.</summary>
    public static readonly TimeSpan AmostraValidaPor = TimeSpan.FromHours(24);

    /// <summary>A causa de falha de turno que é o limite de uso (o <c>TurnFailure.UsageLimit</c> do git.kit).</summary>
    public const string CausaLimiteDeUso = "limite-de-uso";

    /// <summary>A amostra do recorte <c>disco-mb=N;memoria-mb=N;limite=…</c>; nula quando ele não é uma.</summary>
    public static AmostraLida? LerAmostra(string? detalhe)
    {
        var partes = (detalhe ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
        if (!partes.TryGetValue("limite", out var limite))
            return null;
        static long Numero(Dictionary<string, string> d, string chave)
            => d.TryGetValue(chave, out var texto) && long.TryParse(texto, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : -1;
        return new AmostraLida(Numero(partes, "disco-mb"), Numero(partes, "memoria-mb"), limite);
    }

    /// <summary>A sugestão do recorte <c>causa|sugestao-N|estado|#US</c>; nula quando ele não é uma.</summary>
    public static SugestaoLida? LerSugestao(string? detalhe)
    {
        var partes = (detalhe ?? string.Empty).Split('|');
        if (partes.Length < 3 || partes[0].Trim().Length == 0 || !partes[1].StartsWith("sugestao-", StringComparison.Ordinal)
            || !long.TryParse(partes[1]["sugestao-".Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id))
            return null;
        return new SugestaoLida(partes[0].Trim(), id, partes[2].Trim(), partes.Length > 3 ? partes[3].Trim() : string.Empty);
    }

    /// <summary>
    /// De quem é a foto de um evento: a sessão (situação e pergunta), o work item (fechamento — a sessão, sem ele), a
    /// sugestão (o id dela) ou a máquina (a amostra, chave vazia). Nula quando o tipo não é foto ou o evento não a identifica.
    /// </summary>
    public static string? ChaveDaFoto(string tipo, string sessao, string detalhe, long? valor) => tipo switch
    {
        TiposDeEvento.SituacaoDaTask or TiposDeEvento.PerguntaAoDev => sessao.Length > 0 ? sessao : null,
        TiposDeEvento.FechamentoDaEntrega => valor is > 0 ? $"#{valor}" : sessao.Length > 0 ? sessao : null,
        TiposDeEvento.AmostraDaMaquina => LerAmostra(detalhe) is null ? null : string.Empty,
        TiposDeEvento.SugestaoDeImpacto => LerSugestao(detalhe) is { } s ? $"sugestao-{s.Id}" : null,
        _ => null,
    };

    /// <summary>
    /// Os turnos de UM dia pelo desfecho. Com o <see cref="TiposDeEvento.DesfechoDoTurno"/> (o dev.kit desta US), vale ele;
    /// sem (o dev.kit anterior), a conta sai dos turnos e das falhas: o limite de uso é a causa dele, o resto da falha é
    /// "falhou", e o que sobra trabalhou — o "esperou o dev" só o desfecho novo distingue.
    /// </summary>
    /// <param name="totais">Os totais do dia por tipo e recorte: (tipo, detalhe, eventos).</param>
    public static TurnosDoDiaV1 TurnosDoDia(DateOnly dia, IReadOnlyCollection<(string Tipo, string Detalhe, long Eventos)> totais)
    {
        long De(string tipo, string? detalhe = null) => totais.Where(t => t.Tipo == tipo && (detalhe is null || t.Detalhe == detalhe)).Sum(t => t.Eventos);

        if (De(TiposDeEvento.DesfechoDoTurno) > 0)
            return new TurnosDoDiaV1(dia, De(TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.Trabalhou), De(TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.EsperouDev),
                De(TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.LimiteDeUso), De(TiposDeEvento.DesfechoDoTurno, DesfechosDoTurno.Falhou));

        var limite = De(TiposDeEvento.TurnoFalhou, CausaLimiteDeUso);
        var falhas = De(TiposDeEvento.TurnoFalhou) - limite;
        return new TurnosDoDiaV1(dia, Math.Max(0, De(TiposDeEvento.TurnoExecutado) - limite - falhas), 0, limite, falhas);
    }

    /// <summary>
    /// O resultado das sessões do período a partir dos eventos BRUTOS delas: a aprovação é a revisão humana
    /// <c>aprovada</c> ou <c>commit</c>; "na primeira" é sem <c>devolvida</c> antes; as rodadas são os
    /// <see cref="TiposDeEvento.ObjetivoAvaliado"/> da sessão que cumpriu o objetivo; o tempo até aprovar parte do primeiro
    /// evento da sessão no período; a espera do dev é o valor (ms) das revisões até a aprovação.
    /// </summary>
    public static ResultadoDasSessoes Sessoes(IEnumerable<EventoDaSessao> eventos)
    {
        int aprovadas = 0, naPrimeira = 0;
        var rodadas = new List<int>();
        var ate = new List<double>();
        var esperando = new List<double>();
        foreach (var sessao in eventos.Where(e => e.Sessao.Length > 0).GroupBy(e => e.Sessao, StringComparer.Ordinal))
        {
            var ordem = sessao.OrderBy(e => e.EmUtc).ToList();
            if (ordem.Any(e => e.Tipo == TiposDeEvento.ObjetivoCumprido))
                rodadas.Add(ordem.Count(e => e.Tipo == TiposDeEvento.ObjetivoAvaliado));

            var revisoes = ordem.Where(e => e.Tipo == TiposDeEvento.RevisaoHumana).ToList();
            var aprovacao = revisoes.FirstOrDefault(e => e.Detalhe is "aprovada" or "commit");
            if (aprovacao is null)
                continue;
            aprovadas++;
            var ateAprovar = revisoes.TakeWhile(e => !ReferenceEquals(e, aprovacao)).Append(aprovacao).ToList();
            if (!ateAprovar.Any(e => e.Detalhe == "devolvida"))
                naPrimeira++;
            ate.Add((aprovacao.EmUtc - ordem[0].EmUtc).TotalMinutes);
            esperando.Add(ateAprovar.Sum(e => Math.Max(0, e.Valor ?? 0)) / 60_000d);
        }

        return new ResultadoDasSessoes(aprovadas, naPrimeira, rodadas, ate, esperando);
    }

    /// <summary>A mediana; nula sem valor.</summary>
    public static double? Mediana(IReadOnlyCollection<double> valores)
    {
        if (valores.Count == 0)
            return null;
        var ordem = valores.Order().ToArray();
        var meio = ordem.Length / 2;
        return ordem.Length % 2 == 1 ? ordem[meio] : (ordem[meio - 1] + ordem[meio]) / 2;
    }

    /// <summary>
    /// As causas de "onde as tasks travam" a partir das fotos das sugestões do período: as vezes são as sugestões de cada
    /// causa, e o selo é o da mais nova — promovida é "corrigido" (com a US, quando há), pendente é "sugestão #N", e a
    /// descartada só mostra a US. Da causa mais vista para a menos.
    /// </summary>
    public static IReadOnlyList<CausaDeTravamentoV1> OndeTravam(IEnumerable<SugestaoLida> sugestoes, int maximo = 7)
        => sugestoes
            .GroupBy(s => s.Causa, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var maisNova = g.MaxBy(s => s.Id)!;
                var (selo, tipo) = maisNova.Estado switch
                {
                    "promovida" => (maisNova.Us.Length > 0 ? $"corrigido {maisNova.Us}" : "corrigido", TiposDeSelo.Corrigido),
                    "pendente" => ($"sugestão #{maisNova.Id}", TiposDeSelo.Sugestao),
                    _ => maisNova.Us.Length > 0 ? (maisNova.Us, TiposDeSelo.Us) : (string.Empty, TiposDeSelo.Nenhum),
                };
                return new CausaDeTravamentoV1(maisNova.Causa, g.Count(), selo, tipo);
            })
            .OrderByDescending(c => c.Vezes).ThenBy(c => c.Causa, StringComparer.Ordinal)
            .Take(maximo)
            .ToArray();
}

/// <summary>
/// As regras da EDIÇÃO de uma empresa pelo admin (US #687), antes do banco: os dados básicos (com o CNPJ pelos dígitos
/// verificadores), o plano, o contrato e os assentos — que nunca descem abaixo dos que estão em uso.
/// </summary>
public static partial class RegrasDaEmpresa
{
    public const int TamanhoMaximoDoSegmento = 60;
    public const int TamanhoMaximoDaCidade = 80;
    public const int TamanhoMaximoDoContato = 120;
    public const int TamanhoMaximoDoTelefone = 30;
    public const int TamanhoMaximoDoSite = 200;

    /// <summary>Os erros por campo da edição; vazio quando ela pode ser gravada. Os assentos em uso são conferidos no banco.</summary>
    public static Dictionary<string, string[]> Validar(EmpresaEditadaV1? edicao)
    {
        if (edicao is null)
            return new(StringComparer.Ordinal) { ["empresa"] = ["O corpo do pedido é obrigatório."] };

        var erros = ValidadorDeEmpresa.Validar(new EmpresaNova(edicao.Nome, edicao.Plano, edicao.Assentos));
        if (Cnpj(edicao.Cnpj) is { Length: > 0 } cnpj && !CnpjValido(cnpj))
            erros["cnpj"] = ["CNPJ inválido: confira os 14 dígitos."];
        if ((edicao.Uf ?? string.Empty).Trim() is { Length: > 0 } uf && !Ufs.Todas.Contains(uf.ToUpperInvariant()))
            erros["uf"] = ["UF inválida: use a sigla do estado (ex.: SP)."];
        if ((edicao.Site ?? string.Empty).Trim() is { Length: > 0 } site && !SiteValido(site))
            erros["site"] = ["O site começa por http:// ou https://."];
        Tamanho(erros, "segmento", edicao.Segmento, TamanhoMaximoDoSegmento);
        Tamanho(erros, "cidade", edicao.Cidade, TamanhoMaximoDaCidade);
        Tamanho(erros, "contato", edicao.Contato, TamanhoMaximoDoContato);
        Tamanho(erros, "telefone", edicao.Telefone, TamanhoMaximoDoTelefone);
        Tamanho(erros, "site", edicao.Site, TamanhoMaximoDoSite);
        return erros;
    }

    /// <summary>O problema dos assentos frente aos em uso; vazio quando servem. Quem já está dentro não perde o assento.</summary>
    public static string ValidarAssentos(int assentos, int emUso)
        => assentos < emUso
            ? $"A empresa tem {emUso} {(emUso == 1 ? "assento em uso" : "assentos em uso")}: o mínimo é {emUso}. Bloqueie um usuário antes de reduzir."
            : string.Empty;

    /// <summary>O CNPJ como o banco o guarda: só os dígitos.</summary>
    public static string Cnpj(string? texto) => NaoDigito().Replace(texto ?? string.Empty, string.Empty);

    /// <summary>O CNPJ (só os dígitos) tem 14 dígitos, não é uma sequência repetida e os dois verificadores conferem.</summary>
    public static bool CnpjValido(string digitos)
    {
        if (digitos.Length != 14 || !digitos.All(char.IsAsciiDigit) || digitos.Distinct().Count() == 1)
            return false;
        return digitos[12] - '0' == Verificador(digitos, 12) && digitos[13] - '0' == Verificador(digitos, 13);
    }

    /// <summary>O CNPJ formatado para a tela: 12.345.678/0001-90 (o que não tem 14 dígitos volta como está).</summary>
    public static string FormatarCnpj(string digitos)
        => digitos.Length == 14 ? $"{digitos[..2]}.{digitos[2..5]}.{digitos[5..8]}/{digitos[8..12]}-{digitos[12..]}" : digitos;

    /// <summary>O dígito verificador da posição <paramref name="posicao"/> (12 ou 13), pelos pesos 2–9 da direita para a esquerda.</summary>
    private static int Verificador(string digitos, int posicao)
    {
        var soma = 0;
        for (var i = 0; i < posicao; i++)
            soma += (digitos[i] - '0') * (2 + (posicao - 1 - i) % 8);
        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    private static bool SiteValido(string site)
        => Uri.TryCreate(site, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static void Tamanho(Dictionary<string, string[]> erros, string campo, string? valor, int maximo)
    {
        if ((valor ?? string.Empty).Trim().Length > maximo && !erros.ContainsKey(campo))
            erros[campo] = [$"Até {maximo} caracteres."];
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex NaoDigito();
}
