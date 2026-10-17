using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>O papel de quem lê ou grava o contexto (US #556): decide o que ele vê e o que ele grava.</summary>
public enum PapelNoContexto
{
    /// <summary>O usuário do dev.kit (e o agente dele, com o mesmo token).</summary>
    Dev,

    /// <summary>O admin da EMPRESA: também o contexto da empresa e o de todas as equipes dela.</summary>
    Gestor,

    /// <summary>O admin do BOARD dev.kit: tudo, de qualquer empresa.</summary>
    Admin,
}

/// <summary>
/// O escopo do CONTEXTO (US #556): a empresa, quem consulta e o papel. É o filtro ÚNICO de isolamento — o serviço o aplica
/// num ponto só, e o admin do board entra pelo MESMO serviço com <see cref="DoAdmin"/>, sem caminho paralelo.
/// </summary>
public sealed record EscopoDoContexto(int EmpresaId, int UsuarioId, PapelNoContexto Papel)
{
    public bool EhAdmin => Papel == PapelNoContexto.Admin;

    /// <summary>O gestor e o admin administram a empresa: o contexto dela e o de todas as equipes.</summary>
    public bool AdministraAEmpresa => Papel is PapelNoContexto.Gestor or PapelNoContexto.Admin;

    /// <summary>O escopo do token de quem tem empresa (o dev e o gestor).</summary>
    public static EscopoDoContexto DoToken(EscopoDaEmpresa escopo)
        => new(escopo.EmpresaId, escopo.UsuarioId, escopo.EhGestor ? PapelNoContexto.Gestor : PapelNoContexto.Dev);

    /// <summary>O admin do board sobre a empresa escolhida na landing (as rotas <c>/api/admin/empresas/{id}/contexto</c>).</summary>
    public static EscopoDoContexto DoAdmin(int empresaId, int adminId) => new(empresaId, adminId, PapelNoContexto.Admin);
}

/// <summary>O resultado de uma gravação do contexto: o item, ou a falha — o conflito (409) leva o item atual.</summary>
public sealed record ResultadoDoContexto(ContextoV1? Valor, FalhaNaGestao Falha = FalhaNaGestao.Nenhuma, string Mensagem = "", ContextoV1? Atual = null)
{
    public static ResultadoDoContexto Falhou(FalhaNaGestao falha, string mensagem, ContextoV1? atual = null) => new(null, falha, mensagem, atual);
}

/// <summary>
/// O CONTEXTO do dev.kit (US #556) — usuário, equipe e empresa, instruções e sugestões. Toda operação recebe o
/// <see cref="EscopoDoContexto"/> e confere a permissão pela <see cref="RegrasDoContexto"/>: nada disso mora no front.
/// O item de outra empresa é <see cref="FalhaNaGestao.NaoEncontrado"/> (404, não revela); o da equipe sem participação
/// aprovada e o de outro dono, <see cref="FalhaNaGestao.Proibida"/> (403).
/// </summary>
public interface IContextoDaEmpresa
{
    /// <summary>
    /// O que o dev.kit sincroniza: a empresa, a <paramref name="equipe"/> declarada e o usuário. A equipe sem participação
    /// vira um pedido PENDENTE (o dev só pede; o gestor aprova) e não entra no composto até ser aprovada.
    /// </summary>
    Task<ContextoCompostoV1> ComporAsync(EscopoDoContexto escopo, string? equipe, CancellationToken ct);

    /// <summary>O que a landing mostra ao papel do escopo; nula quando a empresa (do admin) não existe.</summary>
    Task<VisaoDoContextoV1?> VisaoAsync(EscopoDoContexto escopo, CancellationToken ct);

    /// <summary>O contexto de UMA equipe: 403 sem a participação aprovada (o pedido pendente não conta).</summary>
    Task<ResultadoDaGestao<IReadOnlyList<ContextoV1>>> DaEquipeAsync(EscopoDoContexto escopo, string equipe, CancellationToken ct);

    /// <summary>
    /// Grava o item JÁ validado (<see cref="RegrasDoContexto.Validar"/>): o novo nasce na versão 1; o que existe soma 1. A
    /// <see cref="GravacaoDeContextoV1.VersaoEsperada"/> diferente da atual (ou o item excluído) é conflito, com o atual.
    /// </summary>
    Task<ResultadoDoContexto> GravarAsync(EscopoDoContexto escopo, GravacaoDeContextoV1 gravacao, CancellationToken ct);

    /// <summary>Exclui o item (descartar a sugestão do agente é isto): quem grava no nível dele, ou o admin.</summary>
    Task<ResultadoDoContexto> ExcluirAsync(EscopoDoContexto escopo, long id, CancellationToken ct);

    /// <summary>
    /// Promove a SUGESTÃO a INSTRUÇÃO (ação do usuário): a instrução da mesma chave recebe o texto da sugestão, e a sugestão
    /// sai. Só quem grava instrução naquele nível.
    /// </summary>
    Task<ResultadoDoContexto> PromoverAsync(EscopoDoContexto escopo, long id, CancellationToken ct);

    /// <summary>
    /// O gestor (ou o admin) aprova, recusa ou cadastra direto a participação de um usuário DA empresa numa equipe. O dev
    /// recebe a recusa; o usuário de outra empresa é <see cref="FalhaNaGestao.NaoEncontrado"/>.
    /// </summary>
    Task<ResultadoDaGestao<ParticipanteDeEquipeV1>> DecidirParticipacaoAsync(EscopoDoContexto escopo, string equipe, DecisaoDeParticipacaoV1 decisao, CancellationToken ct);
}

/// <summary>
/// As regras PURAS do contexto (US #556): a validação, a permissão por papel (testável sem banco, usada por toda rota e
/// pelo serviço) e a barreira de credenciais. Base: as regras das convenções da US #491.
/// </summary>
public static partial class RegrasDoContexto
{
    /// <summary>A nota do agente é mais curta que a instrução: o agente não polui o contexto.</summary>
    public const int TamanhoMaximoDaNota = 1000;

    /// <summary>A equipe como chave: sem espaço nas pontas e em minúsculas ("Vendas" e "vendas" são a mesma equipe).</summary>
    public static string NormalizarEquipe(string? equipe) => (equipe ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>A origem da gravação: vazia é a do usuário (instrução).</summary>
    public static string NormalizarOrigem(string? origem)
    {
        var valor = (origem ?? string.Empty).Trim().ToLowerInvariant();
        return valor.Length == 0 ? OrigensDeContexto.Usuario : valor;
    }

    /// <summary>O erro da gravação (400), ou vazio quando ela vale. A credencial é outra recusa (<see cref="ContemSegredo"/>, 422).</summary>
    public static string Validar(GravacaoDeContextoV1? gravacao)
    {
        var nivel = gravacao?.Nivel?.Trim().ToLowerInvariant() ?? string.Empty;
        var chave = gravacao?.Chave?.Trim().ToLowerInvariant() ?? string.Empty;
        var valor = gravacao?.Valor?.Trim() ?? string.Empty;
        var equipe = NormalizarEquipe(gravacao?.Equipe);
        var origem = NormalizarOrigem(gravacao?.Origem);
        if (nivel is not (NiveisDeContexto.Usuario or NiveisDeContexto.Equipe or NiveisDeContexto.Empresa))
            return $"O nível do contexto é '{NiveisDeContexto.Usuario}', '{NiveisDeContexto.Equipe}' ou '{NiveisDeContexto.Empresa}'.";
        if (origem is not (OrigensDeContexto.Usuario or OrigensDeContexto.Agente))
            return $"A origem do contexto é '{OrigensDeContexto.Usuario}' (instrução) ou '{OrigensDeContexto.Agente}' (sugestão).";
        if (chave.Length == 0 || chave.Length > LimitesDoContexto.TamanhoMaximoDaChave || !ChaveValida().IsMatch(chave))
            return "A chave do contexto é um nome curto em minúsculas, sem acento, como 'contexto.testes' ou 'branch.hotfix'.";
        if (valor.Length == 0)
            return "Informe o texto do contexto (para excluir, use a exclusão).";
        var teto = origem == OrigensDeContexto.Agente ? TamanhoMaximoDaNota : LimitesDoContexto.TamanhoMaximoDoValor;
        if (valor.Length > teto)
            return $"O texto do contexto passa de {teto} caracteres.";
        if (nivel == NiveisDeContexto.Equipe && equipe.Length == 0)
            return "Informe a equipe do contexto (a Equipe das Configurações do dev.kit).";
        if (equipe.Length > LimitesDoContexto.TamanhoMaximoDaEquipe)
            return $"O nome da equipe passa de {LimitesDoContexto.TamanhoMaximoDaEquipe} caracteres.";
        return gravacao!.VersaoEsperada is <= 0 ? "A versão esperada começa em 1." : string.Empty;
    }

    /// <summary>
    /// Se o escopo VÊ um item: o admin vê tudo; o da empresa, todos da empresa (o dev só lê); o da equipe, o gestor e quem
    /// participa dela APROVADO; o do usuário, só o dono.
    /// </summary>
    public static bool PodeVer(EscopoDoContexto escopo, string nivel, string equipe, int dono, IReadOnlySet<string> equipesAprovadas)
        => escopo.EhAdmin || nivel switch
        {
            NiveisDeContexto.Empresa => true,
            NiveisDeContexto.Equipe => escopo.AdministraAEmpresa || equipesAprovadas.Contains(NormalizarEquipe(equipe)),
            NiveisDeContexto.Usuario => dono == escopo.UsuarioId,
            _ => false,
        };

    /// <summary>
    /// Por que o escopo NÃO grava (nem exclui, nem promove) no nível — ou vazio quando grava. O agente (origem
    /// <c>agente</c>, com o token do usuário) grava só onde o usuário grava, e sempre como sugestão.
    /// </summary>
    public static string PodeGravar(EscopoDoContexto escopo, string nivel, string equipe, int dono, IReadOnlySet<string> equipesAprovadas)
    {
        if (escopo.EhAdmin)
            return string.Empty;
        return nivel switch
        {
            NiveisDeContexto.Empresa when !escopo.AdministraAEmpresa
                => "Só o admin da empresa (o gestor) grava o contexto da empresa.",
            NiveisDeContexto.Equipe when !escopo.AdministraAEmpresa && !equipesAprovadas.Contains(NormalizarEquipe(equipe))
                => $"Você não participa da equipe '{NormalizarEquipe(equipe)}' (ou o pedido ainda aguarda a aprovação do gestor).",
            NiveisDeContexto.Usuario when dono != escopo.UsuarioId
                => "O contexto de um usuário é só dele.",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Se o texto parece CREDENCIAL (<see cref="SegredosNoContexto"/>): token, senha, connection string com senha, chave
    /// privada ou uma sequência aleatória longa. O contexto é compartilhado e vai para o arquivo de regras de todo agente.
    /// </summary>
    public static bool ContemSegredo(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
            return false;
        if (Padroes.Any(p => p.IsMatch(valor)))
            return true;
        return Candidatas().Matches(valor).Any(m => AltaEntropia(m.Value));
    }

    /// <summary>A recusa da credencial (422) — sem ecoar o valor.</summary>
    public const string MensagemDeSegredo =
        "O texto parece conter uma credencial (token, senha, connection string ou chave privada). O contexto é compartilhado com a equipe e a empresa e vai para o arquivo de regras dos agentes: remova-a e grave de novo.";

    private static readonly Regex[] Padroes = SegredosNoContexto.Padroes
        .Select(p => new Regex(p, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200)))
        .ToArray();

    /// <summary>Maiúscula, minúscula, dígito e a entropia de Shannon acima do mínimo.</summary>
    private static bool AltaEntropia(string trecho)
    {
        if (!trecho.Any(char.IsUpper) || !trecho.Any(char.IsLower) || !trecho.Any(char.IsDigit))
            return false;
        var entropia = trecho.GroupBy(c => c)
            .Select(g => (double)g.Count() / trecho.Length)
            .Sum(p => -p * Math.Log2(p));
        return entropia >= SegredosNoContexto.EntropiaMinima;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ChaveValida();

    [GeneratedRegex("[A-Za-z0-9+_=]{32,}", RegexOptions.CultureInvariant)]
    private static partial Regex Candidatas();
}

/// <summary>
/// As rotas ANTIGAS das convenções (<c>/api/empresa/convencoes</c>, US #491) como adaptador fino do contexto: o dev.kit já
/// instalado continua lendo e gravando o nome do branch. Time ↔ equipe, e só as instruções (origem usuário).
/// </summary>
public static class AdaptadorDeConvencoes
{
    /// <summary>A convenção como gravação de contexto (origem usuário).</summary>
    public static GravacaoDeContextoV1 ParaGravacao(ConvencaoV1 convencao) => new(
        Nivel(convencao.Nivel), convencao.Chave, convencao.Valor, convencao.Time, OrigensDeContexto.Usuario);

    /// <summary>O item de contexto como convenção.</summary>
    public static ConvencaoV1 ParaConvencao(ContextoV1 item) => new(
        item.Nivel == NiveisDeContexto.Equipe ? NiveisDeConvencao.Time : item.Nivel,
        item.Chave, item.Valor, item.Equipe, item.Usuario, item.Autor, item.AtualizadoEm);

    /// <summary>As instruções como convenções (as sugestões dos agentes não existiam para o dev.kit antigo).</summary>
    public static IReadOnlyList<ConvencaoV1> Convencoes(IEnumerable<ContextoV1> itens)
        => itens.Where(i => i.Origem == OrigensDeContexto.Usuario).Select(ParaConvencao).ToArray();

    /// <summary>O nível da convenção no contexto: <c>time</c> é <c>equipe</c>.</summary>
    public static string Nivel(string? nivel)
    {
        var valor = nivel?.Trim().ToLowerInvariant() ?? string.Empty;
        return valor == NiveisDeConvencao.Time ? NiveisDeContexto.Equipe : valor;
    }
}
