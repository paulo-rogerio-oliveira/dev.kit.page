using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O controle remoto (projeto controle-remoto, fase 1): o que o dev.kit publica de cada máquina e o que o site lê. Todo
/// método recebe o usuário do TOKEN, nunca do corpo: não há como publicar nem ler a máquina de outro, e a do outro é 404,
/// sem dizer se existe. Nem o admin nem o gestor leem: o dado é do dono.
/// </summary>
public interface IControleRemoto
{
    /// <summary>Grava o heartbeat JÁ validado (<see cref="RegrasDoRemoto"/>); a máquina nova conta no teto do usuário.</summary>
    Task<ResultadoDaGestao<HeartbeatRecebidoV1>> RegistrarHeartbeatAsync(int usuario, HeartbeatV1 heartbeat, CancellationToken ct);

    /// <summary>Grava o retrato JÁ validado, se for mais recente que o gravado. A máquina sem heartbeat é 404.</summary>
    Task<ResultadoDaGestao<RetratoRecebidoV1>> RegistrarRetratoAsync(int usuario, RetratoV1 retrato, CancellationToken ct);

    /// <summary>As máquinas do usuário, da que falou por último à mais antiga.</summary>
    Task<IReadOnlyList<MaquinaRemotaV1>> MaquinasAsync(int usuario, CancellationToken ct);

    /// <summary>A máquina do usuário e o último retrato; nulo quando ela não é dele ou não existe.</summary>
    Task<PainelRemotoV1?> PainelAsync(int usuario, string maquinaId, CancellationToken ct);
}

/// <summary>O expurgo das máquinas sem heartbeat além da retenção, com o retrato delas.</summary>
public interface IExpurgoDoRemoto
{
    /// <summary>Apaga as máquinas anteriores ao corte e devolve quantas.</summary>
    Task<int> ExpurgarAsync(CancellationToken ct);
}

/// <summary>Por que o heartbeat ou o retrato foi recusado.</summary>
/// <param name="Status">400 fora do contrato; 422 com dado que não pode sair da máquina.</param>
/// <param name="Mensagem">O motivo, sem ecoar o valor recusado.</param>
/// <param name="Campo">O caminho do campo (<c>fluxos[3].titulo</c>), no 422.</param>
/// <param name="Motivo"><see cref="RemotoV1.MotivoSegredo"/> ou <see cref="RemotoV1.MotivoDadoDeCliente"/>, no 422.</param>
public sealed record RecusaDoRemoto(int Status, string Mensagem, string Campo = "", string Motivo = "");

/// <summary>
/// As regras puras do que entra pelo controle remoto, sem banco. A sanitização é do dev.kit (<c>TraceSanitizado</c>); aqui
/// fica a defesa em dobro, e ela RECUSA em vez de mascarar: o retrato com token ou dado de cliente não é gravado nem em parte.
/// Os segredos são os de <see cref="RegrasDosRelatos.ContemSegredo"/> e os dados, os da máscara de
/// <see cref="RegrasDeErro.Mascarar"/>, mais o documento e o telefone. Nome de cliente escrito num título não tem formato
/// para reconhecer: o que protege é o contrato, sem campo de descrição, prompt, repositório ou conversa.
/// </summary>
public static partial class RegrasDoRemoto
{
    /// <summary>O id da máquina é um GUID: é o que impede o hostname de entrar por aqui.</summary>
    public static bool MaquinaValida(string? id) => Guid.TryParseExact(id, "N", out _) || Guid.TryParseExact(id, "D", out _);

    /// <summary>O id como a base guarda: o mesmo GUID, com hífen ou em maiúsculas, é a mesma máquina.</summary>
    public static string NormalizarMaquina(string id) => Guid.Parse(id).ToString("N");

    /// <summary>O nome da máquina sem apelido, no padrão da telemetria.</summary>
    public static string ApelidoPadrao(string maquinaId) => $"máquina {maquinaId[..8]}";

    /// <summary>A máquina está conectada quando o último heartbeat cabe na tolerância.</summary>
    public static bool Conectada(DateTimeOffset ultimoHeartbeat, DateTimeOffset agora, int toleranciaSegundos)
        => agora - ultimoHeartbeat <= TimeSpan.FromSeconds(toleranciaSegundos);

    /// <summary>
    /// O texto tem dado de cliente? Caminho, URL, e-mail, usuário de domínio ou GUID (a máscara do trace) e CPF, CNPJ ou
    /// telefone. A comparação ignora as pontas, que a máscara tira.
    /// </summary>
    public static bool ContemDadoDeCliente(string? texto)
    {
        var limpo = (texto ?? string.Empty).Trim();
        return limpo.Length > 0 && (Documento().IsMatch(limpo) || Telefone().IsMatch(limpo) || !string.Equals(RegrasDeErro.Mascarar(limpo), limpo, StringComparison.Ordinal));
    }

    /// <summary>A recusa do heartbeat, ou nula quando ele vale.</summary>
    public static RecusaDoRemoto? Validar(HeartbeatV1? heartbeat)
    {
        if (heartbeat is null)
            return Formato("Envie o heartbeat.");
        if (Cabecalho(heartbeat.Versao, heartbeat.MaquinaId) is { } recusa)
            return recusa;
        if (!Codigo(heartbeat.Agente))
            return Formato("O agente é um código em minúsculas (ativo, pausado, limite ou desconhecido).");
        if (heartbeat.TurnosEmAndamento < 0)
            return Formato("Os turnos em andamento não são negativos.");
        return Textos(
            new Texto("apelido", heartbeat.Apelido, RemotoV1.TamanhoMaximoDoApelido),
            new Texto("versaoDevKit", heartbeat.VersaoDevKit, RemotoV1.TamanhoMaximoDaVersaoDoDevKit, Obrigatorio: true));
    }

    /// <summary>A recusa do retrato, ou nula quando ele vale.</summary>
    public static RecusaDoRemoto? Validar(RetratoV1? retrato)
    {
        if (retrato is null)
            return Formato("Envie o retrato.");
        if (Cabecalho(retrato.Versao, retrato.MaquinaId) is { } recusa)
            return recusa;
        if (retrato.GeradoEm == default)
            return Formato("Informe quando o retrato foi gerado.");
        if (retrato.Projetos is null || retrato.Fluxos is null || retrato.Pendencias is null)
            return Formato("O retrato é inteiro: envie projetos, fluxos e pendências, vazios quando não há.");
        if (retrato.Projetos.Count > RemotoV1.MaximoDeProjetos || retrato.Fluxos.Count > RemotoV1.MaximoDeFluxos || retrato.Pendencias.Count > RemotoV1.MaximoDePendencias)
            return Formato($"O retrato tem até {RemotoV1.MaximoDeProjetos} projetos, {RemotoV1.MaximoDeFluxos} fluxos e {RemotoV1.MaximoDePendencias} pendências.");
        return ValidarProjetos(retrato.Projetos) ?? ValidarFluxos(retrato.Fluxos) ?? ValidarPendencias(retrato.Pendencias) ?? Textos(TextosDe(retrato).ToArray());
    }

    private static RecusaDoRemoto? ValidarProjetos(IReadOnlyList<ProjetoDoRetratoV1> projetos)
    {
        foreach (var (projeto, i) in projetos.Select((p, i) => (p, i)))
        {
            if (projeto is null || !RegrasDosProjetos.SlugValido(projeto.Slug))
                return Formato($"O slug de projetos[{i}] usa minúsculas, dígitos e hífen (até {ProjetosV1.TamanhoMaximoDoSlug}).");
            if (!Codigo(projeto.Situacao))
                return Formato($"A situação de projetos[{i}] é um código em minúsculas.");
            if (projeto.Itens < 0 || projeto.Integrados < 0 || projeto.Rodando < 0 || projeto.Integrados + projeto.Rodando > projeto.Itens)
                return Formato($"Os contadores de itens de projetos[{i}] não fecham.");
            if (projeto.Onda < 0 || projeto.Ondas < 0 || projeto.Onda > projeto.Ondas)
                return Formato($"A onda de projetos[{i}] não fecha com as ondas do plano.");
        }

        return Unicos(projetos.Select(p => p.Slug), "projeto");
    }

    private static RecusaDoRemoto? ValidarFluxos(IReadOnlyList<FluxoDoRetratoV1> fluxos)
    {
        foreach (var (fluxo, i) in fluxos.Select((f, i) => (f, i)))
        {
            if (fluxo is null || !Id(fluxo.Id))
                return Formato($"O id de fluxos[{i}] é um identificador interno (até {RemotoV1.TamanhoMaximoDoId}).");
            if (!Codigo(fluxo.Situacao))
                return Formato($"A situação de fluxos[{i}] é um código em minúsculas.");
            if (Vinculo($"fluxos[{i}]", fluxo.Projeto, fluxo.Item) is { } recusa)
                return recusa;
        }

        return Unicos(fluxos.Select(f => f.Id), "fluxo");
    }

    private static RecusaDoRemoto? ValidarPendencias(IReadOnlyList<PendenciaDoRetratoV1> pendencias)
    {
        foreach (var (pendencia, i) in pendencias.Select((p, i) => (p, i)))
        {
            if (pendencia is null || !Id(pendencia.Id))
                return Formato($"O id de pendencias[{i}] é um identificador interno (até {RemotoV1.TamanhoMaximoDoId}).");
            if (!Codigo(pendencia.Tipo))
                return Formato($"O tipo de pendencias[{i}] é um código em minúsculas.");
            if (pendencia.Opcoes is null || pendencia.Opcoes.Count > RemotoV1.MaximoDeOpcoes)
                return Formato($"pendencias[{i}] tem até {RemotoV1.MaximoDeOpcoes} opções, e a lista vai vazia quando não há.");
            if (Vinculo($"pendencias[{i}]", pendencia.Projeto, pendencia.Item) is { } recusa)
                return recusa;
        }

        return Unicos(pendencias.Select(p => p.Id), "pendência");
    }

    /// <summary>Todos os textos do retrato, com o caminho de cada um; os ids e os slugs passam pela conferência de segredo.</summary>
    private static IEnumerable<Texto> TextosDe(RetratoV1 retrato)
    {
        foreach (var (p, i) in retrato.Projetos.Select((p, i) => (p, i)))
        {
            yield return new Texto($"projetos[{i}].slug", p.Slug, ProjetosV1.TamanhoMaximoDoSlug, Livre: false);
            yield return new Texto($"projetos[{i}].nome", p.Nome, RemotoV1.TamanhoMaximoDoTitulo, Obrigatorio: true);
        }

        foreach (var (f, i) in retrato.Fluxos.Select((f, i) => (f, i)))
        {
            yield return new Texto($"fluxos[{i}].id", f.Id, RemotoV1.TamanhoMaximoDoId, Livre: false);
            yield return new Texto($"fluxos[{i}].projeto", f.Projeto, ProjetosV1.TamanhoMaximoDoSlug, Livre: false);
            yield return new Texto($"fluxos[{i}].titulo", f.Titulo, RemotoV1.TamanhoMaximoDoTitulo, Obrigatorio: true);
            yield return new Texto($"fluxos[{i}].fluxo", f.Fluxo, RemotoV1.TamanhoMaximoDoTitulo);
            yield return new Texto($"fluxos[{i}].etapa", f.Etapa, RemotoV1.TamanhoMaximoDoTitulo);
        }

        foreach (var (p, i) in retrato.Pendencias.Select((p, i) => (p, i)))
        {
            yield return new Texto($"pendencias[{i}].id", p.Id, RemotoV1.TamanhoMaximoDoId, Livre: false);
            yield return new Texto($"pendencias[{i}].projeto", p.Projeto, ProjetosV1.TamanhoMaximoDoSlug, Livre: false);
            yield return new Texto($"pendencias[{i}].titulo", p.Titulo, RemotoV1.TamanhoMaximoDoTitulo, Obrigatorio: true);
            yield return new Texto($"pendencias[{i}].resumo", p.Resumo, RemotoV1.TamanhoMaximoDoResumo);
            foreach (var (opcao, j) in p.Opcoes.Select((o, j) => (o, j)))
                yield return new Texto($"pendencias[{i}].opcoes[{j}]", opcao, RemotoV1.TamanhoMaximoDoTitulo, Obrigatorio: true);
        }
    }

    /// <summary>Confere os textos em ordem: primeiro o formato (400), e o que não pode sair da máquina (422) sem ecoar o valor.</summary>
    private static RecusaDoRemoto? Textos(params Texto[] textos)
    {
        foreach (var texto in textos)
        {
            var valor = texto.Valor?.Trim() ?? string.Empty;
            if (texto.Obrigatorio && valor.Length == 0)
                return Formato($"Informe {texto.Campo}.");
            if (valor.Length > texto.Teto)
                return Formato($"{texto.Campo} passa de {texto.Teto} caracteres.");
            if (valor.Any(char.IsControl))
                return Formato($"{texto.Campo} é uma linha só, sem caractere de controle.");
        }

        foreach (var texto in textos)
        {
            if (RegrasDosRelatos.ContemSegredo(texto.Valor))
                return new RecusaDoRemoto(422, $"{texto.Campo} parece ter um segredo (token, senha, chave ou connection string): nada foi gravado.", texto.Campo, RemotoV1.MotivoSegredo);
            if (texto.Livre && ContemDadoDeCliente(texto.Valor))
                return new RecusaDoRemoto(422, $"{texto.Campo} parece ter dado de cliente (caminho, URL, e-mail, usuário, documento ou telefone): nada foi gravado.", texto.Campo, RemotoV1.MotivoDadoDeCliente);
        }

        return null;
    }

    private static RecusaDoRemoto? Cabecalho(string? versao, string? maquinaId)
    {
        if (!string.Equals(versao, RemotoV1.Versao, StringComparison.Ordinal))
            return Formato($"Versão do contrato não suportada: esta rota aceita '{RemotoV1.Versao}'.");
        return MaquinaValida(maquinaId) ? null : Formato("O id da máquina é o GUID anônimo do dev.kit, nunca o hostname.");
    }

    private static RecusaDoRemoto? Vinculo(string onde, string? projeto, int? item)
    {
        if (projeto is not null && !RegrasDosProjetos.SlugValido(projeto))
            return Formato($"O projeto de {onde} é o slug dele.");
        return item is <= 0 || (item is not null && projeto is null)
            ? Formato($"O item de {onde} é positivo e vem com o projeto.")
            : null;
    }

    private static RecusaDoRemoto? Unicos(IEnumerable<string> ids, string oQue)
    {
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        return ids.All(vistos.Add) ? null : Formato($"Há {oQue} repetido no retrato.");
    }

    private static RecusaDoRemoto Formato(string mensagem) => new(400, mensagem);

    private static bool Codigo(string? valor) => valor is not null && CodigoRegex().IsMatch(valor);

    private static bool Id(string? valor) => valor is not null && IdRegex().IsMatch(valor);

    /// <summary>Um texto do envio. <paramref name="Livre"/> falso é identificador: pode ser um GUID, mas nunca um segredo.</summary>
    private readonly record struct Texto(string Campo, string? Valor, int Teto, bool Obrigatorio = false, bool Livre = true);

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,29}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodigoRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._:-]{0,99}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegex();

    /// <summary>CPF e CNPJ, com ou sem pontuação.</summary>
    [GeneratedRegex(@"(?<![\d.\-/])(?:\d{3}\.?\d{3}\.?\d{3}-?\d{2}|\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2})(?![\d.\-/])")]
    private static partial Regex Documento();

    /// <summary>Telefone brasileiro com DDD: <c>(11) 91234-5678</c>, <c>11 3456-7890</c>.</summary>
    [GeneratedRegex(@"(?<!\d)\(?\d{2}\)?[ -]?9?\d{4}-\d{4}(?!\d)")]
    private static partial Regex Telefone();
}
