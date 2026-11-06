using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O controle remoto, fase 1: o heartbeat e o retrato que o dev.kit publica com o JWT do usuário, e a leitura do site.
/// O isolamento é o <c>usuarioId</c> do token em TODA operação — a máquina de outro usuário é inexistente
/// (nulo → 404), sem revelar que existe. Os envios chegam JÁ validados (<see cref="RegrasDoRemoto"/>).
/// </summary>
public interface IRemoto
{
    /// <summary>Grava o heartbeat (cria a máquina no primeiro).</summary>
    Task<HeartbeatRecebidoV1> RegistrarHeartbeatAsync(int usuarioId, HeartbeatV1 heartbeat, CancellationToken ct);

    /// <summary>Substitui o retrato — a não ser que o gravado tenha sido gerado depois (o envio fora de ordem).</summary>
    Task<RetratoRecebidoV1> GravarRetratoAsync(int usuarioId, RetratoV1 retrato, CancellationToken ct);

    /// <summary>As máquinas do usuário, as conectadas primeiro.</summary>
    Task<IReadOnlyList<MaquinaRemotaV1>> MaquinasAsync(int usuarioId, CancellationToken ct);

    /// <summary>O último retrato da máquina do usuário; nulo quando ela não é dele ou ainda não mandou retrato.</summary>
    Task<RetratoDaMaquinaV1?> RetratoAsync(int usuarioId, string maquinaId, CancellationToken ct);
}

/// <summary>A retenção do controle remoto: a máquina sem contato há mais de <see cref="OpcoesDoRemoto.RetencaoDias"/> sai, com o retrato.</summary>
public interface IExpurgoDoRemoto
{
    Task<int> ExpurgarAsync(CancellationToken ct);
}

/// <summary>
/// As regras puras do controle remoto: a forma do heartbeat e do retrato (os tetos do <see cref="RemotoV1"/>) e os campos
/// com dado sensível (<see cref="DadosSensiveis"/>). Sem banco.
/// </summary>
public static partial class RegrasDoRemoto
{
    [GeneratedRegex("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdDaMaquina();

    /// <summary>O id anônimo da máquina: letras, dígitos e hífen, até 64 (o GUID do dev.kit, com ou sem hífens).</summary>
    public static bool MaquinaIdValido(string? maquinaId) => maquinaId is not null && IdDaMaquina().IsMatch(maquinaId);

    /// <summary>O contrato do envio é o que esta API fala?</summary>
    public static bool ContratoConhecido(int contrato) => contrato == RemotoV1.Contrato;

    /// <summary>O problema do heartbeat, ou vazio quando ele vale (o contrato é conferido à parte, com código próprio).</summary>
    public static string Validar(HeartbeatV1? heartbeat)
    {
        if (heartbeat is null)
            return "Envie o heartbeat.";
        if (!MaquinaIdValido(heartbeat.MaquinaId))
            return $"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if ((heartbeat.Nome?.Length ?? 0) > RemotoV1.TamanhoMaximoDoNome)
            return $"O nome da máquina passa de {RemotoV1.TamanhoMaximoDoNome} caracteres.";
        if ((heartbeat.VersaoDevKit?.Length ?? 0) > RemotoV1.TamanhoMaximoDaVersao)
            return $"A versão do dev.kit passa de {RemotoV1.TamanhoMaximoDaVersao} caracteres.";
        if (!RemotoV1.Origens.Contains(Normalizar(heartbeat.Origem)))
            return $"A origem é {string.Join(" ou ", RemotoV1.Origens)}.";
        var estado = Normalizar(heartbeat.EstadoDoAgente);
        if (estado.Length > 0 && !RemotoV1.EstadosDoAgente.Contains(estado))
            return $"O estado do agente é um de: {string.Join(", ", RemotoV1.EstadosDoAgente)}.";
        return string.Empty;
    }

    /// <summary>O problema do retrato, ou vazio quando ele vale.</summary>
    public static string Validar(RetratoV1? retrato)
    {
        if (retrato is null)
            return "Envie o retrato.";
        if (!MaquinaIdValido(retrato.MaquinaId))
            return $"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if (retrato.GeradoEm == default)
            return "Informe quando o retrato foi gerado (geradoEm).";

        var projetos = retrato.Projetos ?? [];
        if (projetos.Count > RemotoV1.MaximoDeProjetos)
            return $"O retrato tem até {RemotoV1.MaximoDeProjetos} projetos.";
        foreach (var (projeto, i) in projetos.Select((p, i) => (p, i)))
        {
            if (projeto is null || !RegrasDosProjetos.SlugValido(projeto.Slug))
                return $"projetos[{i}]: o slug usa minúsculas, dígitos e hífen (até {ProjetosV1.TamanhoMaximoDoSlug}).";
            if (Vazio(projeto.Nome) || projeto.Nome.Length > RemotoV1.TamanhoMaximoDoNome)
                return $"projetos[{i}]: informe o nome (até {RemotoV1.TamanhoMaximoDoNome} caracteres).";
            if (!ProjetosV1.Situacoes.Contains(projeto.Situacao ?? string.Empty))
                return $"projetos[{i}]: a situação é uma de: {string.Join(", ", ProjetosV1.Situacoes)}.";
            if (projeto.OndaAtual < 0)
                return $"projetos[{i}]: a onda não é negativa.";
            var itens = projeto.Itens ?? [];
            if (itens.Count > RemotoV1.MaximoDeItens)
                return $"projetos[{i}]: até {RemotoV1.MaximoDeItens} itens.";
            foreach (var (item, j) in itens.Select((x, j) => (x, j)))
            {
                if (item is null || item.Numero <= 0 || item.Onda < 0 || Vazio(item.Titulo) || item.Titulo.Length > RemotoV1.TamanhoMaximoDoNome)
                    return $"projetos[{i}].itens[{j}]: número positivo, onda não negativa e título de até {RemotoV1.TamanhoMaximoDoNome} caracteres.";
                if (!ProjetosV1.SituacoesDoItem.Contains(item.Situacao ?? string.Empty))
                    return $"projetos[{i}].itens[{j}]: a situação é uma de: {string.Join(", ", ProjetosV1.SituacoesDoItem)}.";
            }
        }

        var fluxos = retrato.Fluxos ?? [];
        if (fluxos.Count > RemotoV1.MaximoDeFluxos)
            return $"O retrato tem até {RemotoV1.MaximoDeFluxos} fluxos.";
        foreach (var (fluxo, i) in fluxos.Select((f, i) => (f, i)))
        {
            if (fluxo is null || Vazio(fluxo.Id) || fluxo.Id.Length > RemotoV1.TamanhoMaximoDoId)
                return $"fluxos[{i}]: informe o id (até {RemotoV1.TamanhoMaximoDoId} caracteres).";
            if (Vazio(fluxo.Nome) || new[] { fluxo.Nome, fluxo.Etapa, fluxo.Situacao, fluxo.Projeto, fluxo.Agente }.Any(t => (t?.Length ?? 0) > RemotoV1.TamanhoMaximoDoNome))
                return $"fluxos[{i}]: informe o nome; nome, etapa, situação, projeto e agente têm até {RemotoV1.TamanhoMaximoDoNome} caracteres.";
            if (fluxo.Item is <= 0)
                return $"fluxos[{i}]: o item é positivo.";
        }

        var pendencias = retrato.Pendencias ?? [];
        if (pendencias.Count > RemotoV1.MaximoDePendencias)
            return $"O retrato tem até {RemotoV1.MaximoDePendencias} pendências.";
        if (pendencias.Where(p => p is not null).Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != pendencias.Count)
            return "O retrato precisa de ids de pendência únicos.";
        foreach (var (pendencia, i) in pendencias.Select((p, i) => (p, i)))
        {
            if (pendencia is null || Vazio(pendencia.Id) || pendencia.Id.Length > RemotoV1.TamanhoMaximoDoId)
                return $"pendencias[{i}]: informe o id (até {RemotoV1.TamanhoMaximoDoId} caracteres).";
            if (!RemotoV1.TiposDePendencia.Contains(pendencia.Tipo ?? string.Empty))
                return $"pendencias[{i}]: o tipo é um de: {string.Join(", ", RemotoV1.TiposDePendencia)}.";
            if (Vazio(pendencia.Titulo) || pendencia.Titulo.Length > RemotoV1.TamanhoMaximoDoNome)
                return $"pendencias[{i}]: informe o título (até {RemotoV1.TamanhoMaximoDoNome} caracteres).";
            if (new[] { pendencia.Pergunta, pendencia.JaTentei }.Any(t => (t?.Length ?? 0) > RemotoV1.TamanhoMaximoDoTexto)
                || new[] { pendencia.Projeto, pendencia.Fluxo }.Any(t => (t?.Length ?? 0) > RemotoV1.TamanhoMaximoDoNome))
                return $"pendencias[{i}]: um dos textos passa do tamanho do contrato.";
            var opcoes = pendencia.Opcoes ?? [];
            if (opcoes.Count > RemotoV1.MaximoDeOpcoes || opcoes.Any(o => Vazio(o) || o.Length > RemotoV1.TamanhoMaximoDoNome))
                return $"pendencias[{i}]: até {RemotoV1.MaximoDeOpcoes} opções, cada uma de até {RemotoV1.TamanhoMaximoDoNome} caracteres.";
            if (pendencia.Item is <= 0)
                return $"pendencias[{i}]: o item é positivo.";
            if (RegrasDaDecisao.Validar(pendencia.Snapshot) is { } erroSnapshot)
                return erroSnapshot;
        }

        return string.Empty;
    }

    /// <summary>Os campos do heartbeat com dado sensível; vazio quando ele pode ser gravado.</summary>
    public static IReadOnlyList<CampoSensivelV1> CamposSensiveis(HeartbeatV1 heartbeat)
        => Sensiveis(
        [
            ("maquinaId", heartbeat.MaquinaId), ("nome", heartbeat.Nome), ("versaoDevKit", heartbeat.VersaoDevKit),
            ("origem", heartbeat.Origem), ("estadoDoAgente", heartbeat.EstadoDoAgente),
        ]);

    /// <summary>
    /// Os campos do retrato com dado sensível — TODO texto, inclusive ids e slugs, com o caminho JSON dele
    /// (<c>pendencias[0].opcoes[1]</c>); vazio quando o retrato pode ser gravado.
    /// </summary>
    public static IReadOnlyList<CampoSensivelV1> CamposSensiveis(RetratoV1 retrato)
        => Sensiveis(Textos(retrato));

    private static IEnumerable<(string Campo, string? Texto)> Textos(RetratoV1 retrato)
    {
        yield return ("maquinaId", retrato.MaquinaId);
        foreach (var (p, i) in (retrato.Projetos ?? []).Select((p, i) => (p, i)))
        {
            yield return ($"projetos[{i}].slug", p.Slug);
            yield return ($"projetos[{i}].nome", p.Nome);
            yield return ($"projetos[{i}].situacao", p.Situacao);
            foreach (var (item, j) in (p.Itens ?? []).Select((x, j) => (x, j)))
            {
                yield return ($"projetos[{i}].itens[{j}].titulo", item.Titulo);
                yield return ($"projetos[{i}].itens[{j}].situacao", item.Situacao);
            }
        }

        foreach (var (f, i) in (retrato.Fluxos ?? []).Select((f, i) => (f, i)))
        {
            yield return ($"fluxos[{i}].id", f.Id);
            yield return ($"fluxos[{i}].nome", f.Nome);
            yield return ($"fluxos[{i}].etapa", f.Etapa);
            yield return ($"fluxos[{i}].situacao", f.Situacao);
            yield return ($"fluxos[{i}].projeto", f.Projeto);
            yield return ($"fluxos[{i}].agente", f.Agente);
        }

        foreach (var (p, i) in (retrato.Pendencias ?? []).Select((p, i) => (p, i)))
        {
            yield return ($"pendencias[{i}].id", p.Id);
            yield return ($"pendencias[{i}].tipo", p.Tipo);
            yield return ($"pendencias[{i}].titulo", p.Titulo);
            yield return ($"pendencias[{i}].pergunta", p.Pergunta);
            yield return ($"pendencias[{i}].jaTentei", p.JaTentei);
            yield return ($"pendencias[{i}].projeto", p.Projeto);
            yield return ($"pendencias[{i}].fluxo", p.Fluxo);
            foreach (var (opcao, j) in (p.Opcoes ?? []).Select((o, j) => (o, j)))
                yield return ($"pendencias[{i}].opcoes[{j}]", opcao);
            if (p.Snapshot is { } s)
            {
                yield return ($"pendencias[{i}].snapshot.origem", s.Origem);
                yield return ($"pendencias[{i}].snapshot.destino", s.Destino);
                yield return ($"pendencias[{i}].snapshot.alteracoes", s.Alteracoes);
                foreach (var (nota, j) in s.Notas.Select((n, j) => (n, j)))
                    yield return ($"pendencias[{i}].snapshot.notas[{j}].avaliador", nota.Avaliador);
                foreach (var (captura, j) in s.Capturas.Select((c, j) => (c, j)))
                {
                    yield return ($"pendencias[{i}].snapshot.capturas[{j}].titulo", captura.Titulo);
                    yield return ($"pendencias[{i}].snapshot.capturas[{j}].url", captura.Url);
                }
            }
        }
    }

    /// <summary>Os textos com dado sensível, com o campo de cada um (também os da conversa, <see cref="RegrasDaConversa"/>).</summary>
    internal static IReadOnlyList<CampoSensivelV1> Sensiveis(IEnumerable<(string Campo, string? Texto)> textos)
        => textos.Select(t => DadosSensiveis.Tipo(t.Texto) is { } tipo ? new CampoSensivelV1(t.Campo, tipo) : null)
            .OfType<CampoSensivelV1>().ToArray();

    /// <summary>A origem ou o estado como a base guarda: sem espaço nas pontas, em minúsculas.</summary>
    public static string Normalizar(string? valor) => (valor ?? string.Empty).Trim().ToLowerInvariant();

    private static bool Vazio(string? texto) => string.IsNullOrWhiteSpace(texto);
}

/// <summary>
/// O detector de dado que não sai da máquina do dev: o SEGREDO (os mesmos padrões da máscara dos relatos,
/// <see cref="RegrasDosRelatos.ContemSegredo"/>), o e-mail (<see cref="RegrasDeErro.Email"/>) e os dados de cliente
/// brasileiros — CPF, CNPJ e telefone.
/// <para>
/// SEM falso positivo nos textos comuns do dev.kit: o CPF e o CNPJ só contam com os dígitos verificadores certos (o CNPJ
/// também no formato alfanumérico de 2026, então só formatado), e nos lugares do número — o carimbo
/// <c>20261102-095333</c> tem 14 dígitos, mas o hífen fora da posição do DV o tira do padrão. O telefone exige o DDD
/// válido e o assinante no formato da Anatel (celular com 9 na frente, fixo de 2 a 5), e não conta a sequência colada
/// por hífen a uma palavra — o slug <c>sprint-11-2026-2027</c> — nem uma faixa de anos (<c>11 2026-2027</c>). O fixo sem
/// DDD não é detectado: é indistinguível de uma faixa de anos.
/// </para>
/// </summary>
public static partial class DadosSensiveis
{
    /// <summary>O primeiro tipo sensível do texto (<see cref="RemotoV1.TiposSensiveis"/>), ou nulo quando ele é limpo.</summary>
    public static string? Tipo(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;
        if (RegrasDosRelatos.ContemSegredo(texto))
            return RemotoV1.SensivelSegredo;
        if (RegrasDeErro.Email().IsMatch(texto))
            return RemotoV1.SensivelEmail;
        if (ContemCnpj(texto))
            return RemotoV1.SensivelCnpj;
        if (ContemCpf(texto))
            return RemotoV1.SensivelCpf;
        if (ContemTelefone(texto))
            return RemotoV1.SensivelTelefone;
        return null;
    }

    /// <summary>
    /// Um CNPJ com os DVs certos (a regra única, <see cref="RegrasDaEmpresa.CnpjValido"/>): numérico, formatado ou só os
    /// 14 dígitos; alfanumérico (desde 2026), só formatado — o <c>XX.XXX.XXX/XXXX-DV</c> —, para um id de 14 caracteres
    /// não ser lido como CNPJ.
    /// </summary>
    public static bool ContemCnpj(string texto)
    {
        foreach (Match m in Cnpj().Matches(texto))
        {
            var bruto = m.Value.Replace(".", string.Empty, StringComparison.Ordinal).Replace("/", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal);
            if (bruto.Any(char.IsLetter) && !m.Value.Contains('/', StringComparison.Ordinal))
                continue;
            if (RegrasDaEmpresa.CnpjValido(bruto))
                return true;
        }
        return false;
    }

    /// <summary>Um CPF com os DVs certos, formatado (<c>XXX.XXX.XXX-DV</c>) ou só os 11 dígitos.</summary>
    public static bool ContemCpf(string texto)
        => Cpf().Matches(texto).Any(m => CpfValido(new string(m.Value.Where(char.IsAsciiDigit).ToArray())));

    /// <summary>
    /// Um telefone brasileiro: com +55, com o DDD entre parênteses, ou com o DDD solto e ao menos um separador (o celular
    /// de 11 dígitos também colado). O celular sem DDD só com o separador (<c>98765-4321</c>).
    /// </summary>
    public static bool ContemTelefone(string texto)
    {
        if (ComPais().Matches(texto).Any(m => DddValido(m.Groups["ddd"].Value)))
            return true;
        if (ComParenteses().Matches(texto).Any(m => DddValido(m.Groups["ddd"].Value)))
            return true;
        foreach (Match m in ComDdd().Matches(texto))
        {
            var numero = m.Groups["num"].Value;
            var colado = m.Groups["sep1"].Length == 0 && m.Groups["sep2"].Length == 0;
            // Sem separador nenhum, só o celular de 11 dígitos: um id de 10 dígitos não é telefone fixo.
            if (!DddValido(m.Groups["ddd"].Value) || (colado && numero.Length != 5) || FaixaDeAnos(numero, m.Groups["fim"].Value))
                continue;
            return true;
        }
        return CelularSemDdd().IsMatch(texto);
    }

    /// <summary>Os DVs do CPF; os 11 dígitos iguais não valem.</summary>
    public static bool CpfValido(string cpf)
    {
        if (cpf.Length != 11 || !cpf.All(char.IsAsciiDigit) || cpf.Distinct().Count() == 1)
            return false;

        static int Dv(string parte)
        {
            var soma = 0;
            for (var i = 0; i < parte.Length; i++)
                soma += (parte[i] - '0') * (parte.Length + 1 - i);
            var resto = soma * 10 % 11;
            return resto == 10 ? 0 : resto;
        }

        return Dv(cpf[..9]) == cpf[9] - '0' && Dv(cpf[..10]) == cpf[10] - '0';
    }

    /// <summary>Os DDDs em uso no Brasil (Anatel).</summary>
    public static bool DddValido(string ddd)
        => int.TryParse(ddd, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && Ddds.Contains(n);

    private static readonly HashSet<int> Ddds =
    [
        11, 12, 13, 14, 15, 16, 17, 18, 19, 21, 22, 24, 27, 28, 31, 32, 33, 34, 35, 37, 38, 41, 42, 43, 44, 45, 46, 47, 48, 49,
        51, 53, 54, 55, 61, 62, 63, 64, 65, 66, 67, 68, 69, 71, 73, 74, 75, 77, 79, 81, 82, 83, 84, 85, 86, 87, 88, 89,
        91, 92, 93, 94, 95, 96, 97, 98, 99,
    ];

    /// <summary>O "fixo" que é uma faixa de anos (<c>2026-2027</c>): dois anos de 1900 a 2099, o segundo não antes do primeiro.</summary>
    private static bool FaixaDeAnos(string numero, string fim)
        => numero.Length == 4 && int.Parse(numero, System.Globalization.CultureInfo.InvariantCulture) is >= 1900 and <= 2099 and var de
           && int.Parse(fim, System.Globalization.CultureInfo.InvariantCulture) is >= 1900 and <= 2099 and var ate && ate >= de;

    // As fronteiras: nada alfanumérico colado (o número dentro de um id maior não conta). O telefone com DDD solto também
    // não pode vir colado por hífen, ponto, barra ou dois-pontos — é o pedaço de um slug, de uma versão ou de um caminho.
    [GeneratedRegex(@"(?<![0-9A-Za-z])[0-9A-Z]{2}\.?[0-9A-Z]{3}\.?[0-9A-Z]{3}/?[0-9A-Z]{4}-?\d{2}(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex Cnpj();

    [GeneratedRegex(@"(?<![0-9A-Za-z])\d{3}\.?\d{3}\.?\d{3}-?\d{2}(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex Cpf();

    [GeneratedRegex(@"(?<![0-9A-Za-z+])\+55[ .-]?\(?(?<ddd>\d{2})\)?[ .-]?9?\d{4}[ .-]?\d{4}(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex ComPais();

    [GeneratedRegex(@"\((?<ddd>0?\d{2})\)[ ]?9?\d{4}[ .-]?\d{4}(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex ComParenteses();

    [GeneratedRegex(@"(?<![0-9A-Za-z_\-./:#])0?(?<ddd>[1-9]\d)(?<sep1>[ -]?)(?<num>9\d{4}|[2-5]\d{3})(?<sep2>-?)(?<fim>\d{4})(?![0-9A-Za-z_\-/])", RegexOptions.CultureInvariant)]
    private static partial Regex ComDdd();

    [GeneratedRegex(@"(?<![0-9A-Za-z_\-./:#])9\d{4}[ -]\d{4}(?![0-9A-Za-z_\-/])", RegexOptions.CultureInvariant)]
    private static partial Regex CelularSemDdd();
}
