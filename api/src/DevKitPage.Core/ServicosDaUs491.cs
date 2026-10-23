using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O que a EMPRESA sabe e compartilha com o dev.kit (US #491), além do catálogo de skills: o catálogo de repositórios e
/// as convenções da empresa e dos times. Toda operação recebe o <see cref="EscopoDaEmpresa"/> e o aplica aqui, num
/// ponto só — o que é de outra empresa não aparece, nem se grava por cima.
/// </summary>
public interface IConhecimentoDaEmpresa
{
    /// <summary>Os repositórios da empresa cujo caminho ou descrição contém o <paramref name="filtro"/> (todos, sem ele).</summary>
    Task<IReadOnlyList<RepositorioDaEmpresaV1>> RepositoriosAsync(EscopoDaEmpresa escopo, string? filtro, CancellationToken ct);

    /// <summary>
    /// Grava o repositório JÁ validado (<see cref="RegrasDoConhecimentoDaEmpresa.ValidarRepositorio"/>): o novo entra; o
    /// mesmo caminho (pela chave normalizada) troca a descrição. Qualquer um da empresa grava.
    /// </summary>
    Task<ResultadoDaGestao<RepositorioDaEmpresaV1>> GravarRepositorioAsync(EscopoDaEmpresa escopo, GravacaoDeRepositorioV1 gravacao, CancellationToken ct);

    /// <summary>
    /// Remove o repositório do catálogo: o gestor, ou quem gravou a descrição atual. Caminho que não está no catálogo da
    /// empresa é <see cref="FalhaNaGestao.NaoEncontrado"/>.
    /// </summary>
    Task<ResultadoDaGestao<RepositorioDaEmpresaV1>> RemoverRepositorioAsync(EscopoDaEmpresa escopo, string origem, CancellationToken ct);

    /// <summary>
    /// As convenções que valem para quem consulta — o que o dev.kit lê: as da empresa, as do <paramref name="time"/>
    /// declarado e as do próprio usuário. Só as REGRAS (<see cref="OrigensDeConvencao.Usuario"/>): o dev.kit anterior à
    /// US #663 trataria a sugestão de um agente como regra.
    /// </summary>
    Task<IReadOnlyList<ConvencaoV1>> ConvencoesAsync(EscopoDaEmpresa escopo, string? time, CancellationToken ct)
        => ConvencoesAsync(escopo, time, comSugestoes: false, ct);

    /// <summary>
    /// As convenções que valem para quem consulta; com <paramref name="comSugestoes"/> (<c>?origem=todas</c>, US #663),
    /// também as SUGESTÕES dos agentes nos mesmos níveis, cada uma com o id, a origem e a versão.
    /// </summary>
    Task<IReadOnlyList<ConvencaoV1>> ConvencoesAsync(EscopoDaEmpresa escopo, string? time, bool comSugestoes, CancellationToken ct);

    /// <summary>
    /// TODAS as convenções que quem consulta pode auditar no site, com o autor e a data: o gestor vê todos os níveis
    /// (inclusive as de cada usuário); o dev vê as da empresa, as de todos os times e as dele.
    /// </summary>
    Task<IReadOnlyList<ConvencaoV1>> AuditoriaDeConvencoesAsync(EscopoDaEmpresa escopo, CancellationToken ct);

    /// <summary>
    /// Grava a convenção JÁ validada (<see cref="RegrasDoConhecimentoDaEmpresa.ValidarConvencao"/>); valor vazio apaga.
    /// Quem pode é a <see cref="RegrasDoConhecimentoDaEmpresa.PodeGravar"/>. O admin geral (<see cref="EscopoDaEmpresa.EhAdmin"/>)
    /// grava o nível usuário de quem é da empresa pelo login em <see cref="ConvencaoV1.Usuario"/>: login que não é
    /// dela é <see cref="FalhaNaGestao.NaoEncontrado"/>, sem revelar se existe noutra.
    /// </summary>
    Task<ResultadoDaGestao<ConvencaoV1>> GravarConvencaoAsync(EscopoDaEmpresa escopo, ConvencaoV1 convencao, CancellationToken ct);

    /// <summary>
    /// As EQUIPES CONHECIDAS da empresa (US #550): os times distintos, já normalizados, das convenções gravadas nela. A
    /// equipe continua um nome livre — a lista só ajuda a escolher, e uma equipe nova entra ao gravar a primeira.
    /// </summary>
    Task<IReadOnlyList<string>> EquipesAsync(EscopoDaEmpresa escopo, CancellationToken ct);

    /// <summary>
    /// Registra a SUGESTÃO de contexto JÁ validada (<see cref="RegrasDoConhecimentoDaEmpresa.ValidarSugestao"/>) que o
    /// agente do dev.kit fez com o token do usuário (US #663): sempre <see cref="OrigensDeConvencao.Agente"/>, na linha
    /// própria da sugestão — a regra do mesmo tema nunca é tocada. A mesma sugestão (autor, tema e texto) devolve a que
    /// já existe, sem subir a versão; outro texto no mesmo tema e camada substitui a sugestão anterior.
    /// </summary>
    Task<ResultadoDaGestao<ConvencaoV1>> SugerirAsync(EscopoDaEmpresa escopo, SugestaoDeContextoV1 sugestao, CancellationToken ct);

    /// <summary>
    /// PROMOVE a sugestão <paramref name="id"/> a regra (US #663): grava (ou sobrescreve) a regra do tema na camada da
    /// sugestão e remove a sugestão, numa transação. Quem pode é o <see cref="RegrasDoConhecimentoDaEmpresa.PodePromover"/>;
    /// a <paramref name="versaoEsperada"/> diferente da atual é <see cref="FalhaNaGestao.Conflito"/>.
    /// </summary>
    Task<ResultadoDaGestao<ConvencaoV1>> PromoverAsync(EscopoDaEmpresa escopo, long id, int? versaoEsperada, CancellationToken ct);

    /// <summary>
    /// DESCARTA a sugestão <paramref name="id"/> (US #663): quem grava a camada, ou o autor da sugestão. A regra do mesmo
    /// tema fica. Id que não é sugestão visível na empresa é <see cref="FalhaNaGestao.NaoEncontrado"/>.
    /// </summary>
    Task<ResultadoDaGestao<ConvencaoV1>> DescartarAsync(EscopoDaEmpresa escopo, long id, CancellationToken ct);
}

/// <summary>As regras puras do catálogo de repositórios e das convenções da empresa (US #491): validação e normalização.</summary>
public static class RegrasDoConhecimentoDaEmpresa
{
    public const int TamanhoMaximoDaOrigem = 400;
    public const int TamanhoMaximoDaDescricao = 1000;
    public const int TamanhoMaximoDaChave = 60;
    public const int TamanhoMaximoDoValor = 200;
    public const int TamanhoMaximoDoTime = 80;

    /// <summary>
    /// O CONTEXTO do dev.kit (US #550) é a família de chaves <c>contexto.&lt;tema&gt;</c> das mesmas convenções: texto que
    /// os agentes recebem no arquivo de regras de toda task, composto empresa → equipe → usuário.
    /// </summary>
    public const string PrefixoDoContexto = "contexto.";

    /// <summary>O teto do valor de uma chave de contexto: texto de várias linhas, e não um modelo de nome de branch.</summary>
    public const int TamanhoMaximoDoValorDeContexto = 4000;

    /// <summary>Nível de convenção (o maior dos dois nomes).</summary>
    public const int TamanhoMaximoDoNivel = 10;

    /// <summary>A origem da convenção (US #663): <c>usuario</c> ou <c>agente</c>.</summary>
    public const int TamanhoMaximoDaOrigemDaConvencao = 10;

    private static readonly Regex ChaveValida = new("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// O caminho normalizado — sem espaço nas pontas, sem barra nem <c>.git</c> no fim, em minúsculas: a mesma origem
    /// escrita de dois jeitos é UM repositório no catálogo.
    /// </summary>
    public static string ChaveDaOrigem(string origem)
    {
        var chave = (origem ?? string.Empty).Trim().TrimEnd('/', '\\');
        if (chave.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            chave = chave[..^4];
        return chave.ToLowerInvariant();
    }

    /// <summary>O time como chave: sem espaço nas pontas e em minúsculas ("Vendas" e "vendas" são o mesmo time).</summary>
    public static string NormalizarTime(string? time) => (time ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>A chave é do contexto do dev.kit (<see cref="PrefixoDoContexto"/>), e não de uma convenção como a do branch.</summary>
    public static bool EhContexto(string? chave)
        => (chave ?? string.Empty).Trim().ToLowerInvariant().StartsWith(PrefixoDoContexto, StringComparison.Ordinal);

    /// <summary>O teto do valor da chave: o do contexto, ou o das outras convenções.</summary>
    public static int TetoDoValor(string? chave) => EhContexto(chave) ? TamanhoMaximoDoValorDeContexto : TamanhoMaximoDoValor;

    /// <summary>
    /// Quem grava a convenção (US #550), numa regra só, sem banco. O admin geral grava tudo. A da EMPRESA é do gestor. A
    /// de um TIME: o CONTEXTO da equipe é do gestor; as outras (o branch, da #491), de qualquer um da empresa. A do
    /// USUÁRIO é do dono — o gestor a vê na auditoria, mas não a edita.
    /// </summary>
    /// <param name="usuarioAlvo">O dono da de nível usuário; 0 é o próprio de quem grava.</param>
    public static bool PodeGravar(EscopoDaEmpresa escopo, string? nivel, string? chave, int usuarioAlvo = 0)
    {
        if (escopo.EhAdmin)
            return true;
        return (nivel ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            NiveisDeConvencao.Empresa => escopo.EhGestor,
            NiveisDeConvencao.Time => escopo.EhGestor || !EhContexto(chave),
            NiveisDeConvencao.Usuario => usuarioAlvo == 0 || usuarioAlvo == escopo.UsuarioId,
            _ => false,
        };
    }

    /// <summary>
    /// Quem SUGERE contexto (US #663): o agente, com o token do usuário, sugere em toda camada que o usuário vê — a dele,
    /// a das equipes e a da empresa. A sugestão nunca vale como regra, então não fere o papel; no nível usuário, só na do
    /// próprio (o admin geral, em qualquer uma).
    /// </summary>
    public static bool PodeSugerir(EscopoDaEmpresa escopo, string? nivel, int usuarioAlvo = 0)
    {
        if (escopo.EhAdmin)
            return true;
        return (nivel ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            NiveisDeConvencao.Empresa or NiveisDeConvencao.Time => true,
            NiveisDeConvencao.Usuario => usuarioAlvo == 0 || usuarioAlvo == escopo.UsuarioId,
            _ => false,
        };
    }

    /// <summary>Quem PROMOVE a sugestão a regra (US #663): quem grava a regra da camada (<see cref="PodeGravar"/>).</summary>
    public static bool PodePromover(EscopoDaEmpresa escopo, string? nivel, string? chave, int usuarioAlvo = 0)
        => PodeGravar(escopo, nivel, chave, usuarioAlvo);

    /// <summary>Quem DESCARTA a sugestão (US #663): quem grava a camada, ou o autor da sugestão.</summary>
    public static bool PodeDescartar(EscopoDaEmpresa escopo, string? nivel, string? chave, int usuarioAlvo, int autorDaSugestao)
        => PodeGravar(escopo, nivel, chave, usuarioAlvo) || (autorDaSugestao != 0 && autorDaSugestao == escopo.UsuarioId);

    /// <summary>
    /// A chave de contexto do tema (US #663): <c>stack</c> e <c>contexto.stack</c> são <c>contexto.stack</c>, sem espaço
    /// nas pontas e em minúsculas.
    /// </summary>
    public static string ChaveDoTema(string? tema)
    {
        var chave = (tema ?? string.Empty).Trim().ToLowerInvariant();
        return chave.Length == 0 || EhContexto(chave) ? chave : PrefixoDoContexto + chave;
    }

    /// <summary>
    /// O erro da sugestão de contexto (US #663), ou vazio quando ela vale: só o contexto aceita a origem agente (o
    /// <c>branch.*</c> continua só regra), o texto é obrigatório e cabe no teto. O segredo é barrado à parte
    /// (<see cref="RegrasDosRelatos.ContemSegredo"/>, 422).
    /// </summary>
    public static string ValidarSugestao(SugestaoDeContextoV1? sugestao)
    {
        var tema = (sugestao?.Tema ?? string.Empty).Trim().ToLowerInvariant();
        if (tema.Length > 0 && !EhContexto(tema) && tema.Contains('.'))
            return "O agente só sugere CONTEXTO (contexto.<tema>): as outras convenções, como o branch, são regra do usuário.";
        var texto = sugestao?.Texto?.Trim() ?? string.Empty;
        if (texto.Length == 0)
            return "Escreva o texto da sugestão: o que o agente aprendeu.";
        return ValidarConvencao(new ConvencaoV1(sugestao?.Camada, ChaveDoTema(tema), texto, sugestao?.Time));
    }

    /// <summary>O erro da gravação de repositório, ou vazio quando ela vale.</summary>
    public static string ValidarRepositorio(GravacaoDeRepositorioV1? gravacao)
    {
        var origem = gravacao?.Origem?.Trim() ?? string.Empty;
        var descricao = gravacao?.Descricao?.Trim() ?? string.Empty;
        if (origem.Length == 0)
            return "Informe o caminho do repositório (a URL do GitHub ou a pasta).";
        if (origem.Length > TamanhoMaximoDaOrigem)
            return $"O caminho passa de {TamanhoMaximoDaOrigem} caracteres.";
        if (descricao.Length == 0)
            return "Informe a descrição do repositório: para que serve e o que tem.";
        return descricao.Length > TamanhoMaximoDaDescricao
            ? $"A descrição passa de {TamanhoMaximoDaDescricao} caracteres."
            : string.Empty;
    }

    /// <summary>O erro da gravação de convenção, ou vazio quando ela vale.</summary>
    public static string ValidarConvencao(ConvencaoV1? convencao)
    {
        var nivel = convencao?.Nivel?.Trim().ToLowerInvariant() ?? string.Empty;
        var chave = convencao?.Chave?.Trim().ToLowerInvariant() ?? string.Empty;
        var time = NormalizarTime(convencao?.Time);
        if (nivel is not (NiveisDeConvencao.Empresa or NiveisDeConvencao.Time or NiveisDeConvencao.Usuario))
            return $"O nível da convenção é '{NiveisDeConvencao.Empresa}', '{NiveisDeConvencao.Time}' ou '{NiveisDeConvencao.Usuario}'.";
        if (chave.Length == 0 || chave.Length > TamanhoMaximoDaChave || !ChaveValida.IsMatch(chave))
            return "A chave da convenção é um nome curto em minúsculas, sem acento, como 'branch.hotfix'.";
        if ((convencao?.Valor?.Trim().Length ?? 0) > TetoDoValor(chave))
            return EhContexto(chave)
                ? $"O texto do contexto passa de {TamanhoMaximoDoValorDeContexto} caracteres."
                : $"O valor da convenção passa de {TamanhoMaximoDoValor} caracteres.";
        if (nivel == NiveisDeConvencao.Time && time.Length == 0)
            return "Informe o time da convenção (a Equipe das Configurações do dev.kit).";
        return time.Length > TamanhoMaximoDoTime ? $"O nome do time passa de {TamanhoMaximoDoTime} caracteres." : string.Empty;
    }
}
