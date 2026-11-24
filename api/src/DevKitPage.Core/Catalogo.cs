using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// As regras do catálogo BÁSICO (US #425) — puras, sem HTTP. As releases <c>catalogo-vN</c> do repositório do
/// dev.kit trazem o zip do catálogo e o <c>manifesto.json</c>; elas NUNCA entram na lista de versões do app
/// (<see cref="RegrasDeVersao.Versao"/> as recusa explicitamente), e as do app nunca entram aqui.
/// </summary>
public static partial class RegrasDoCatalogoBase
{
    /// <summary>O prefixo da tag das releases do catálogo.</summary>
    public const string PrefixoDaTag = "catalogo-v";

    /// <summary>O nome do manifesto anexado à release.</summary>
    public const string Manifesto = "manifesto.json";

    /// <summary>A versão do catálogo da tag <c>catalogo-vN</c>; nula para qualquer outra tag.</summary>
    public static int? Versao(string? tag)
    {
        var lido = Tag().Match((tag ?? string.Empty).Trim());
        return lido.Success && int.TryParse(lido.Groups[1].Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var versao) && versao > 0
            ? versao
            : null;
    }

    /// <summary>A tag é do catálogo (e, portanto, nunca uma versão do app)?</summary>
    public static bool EhDoCatalogo(string? tag) => (tag ?? string.Empty).Trim().StartsWith(PrefixoDaTag, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A delta para quem já tem a versão <paramref name="desde"/>: os itens que mudaram e os retirados depois
    /// dela. Nula quando nada mudou (a rota responde 304) — inclusive para quem diz ter uma versão À FRENTE.
    /// </summary>
    public static ManifestoDoCatalogoV1? Delta(ManifestoDoCatalogoV1 manifesto, int desde)
    {
        if (manifesto.Versao <= Math.Max(0, desde))
            return null;
        return manifesto with
        {
            Itens = manifesto.Itens.Where(i => i.Catalogo > desde).ToArray(),
            Retirados = manifesto.Retirados.Where(r => r.Catalogo > desde).ToArray(),
        };
    }

    /// <summary>A vitrine pública: os itens agrupados por tecnologia (a sem tecnologia vai para "Geral"), em ordem de nome.</summary>
    public static VitrineV1 Vitrine(ManifestoDoCatalogoV1 manifesto, DateTimeOffset publicadaEm)
        => new(manifesto.Versao, publicadaEm, manifesto.Itens
            .Select(i => new ItemDaVitrineV1(i.Tipo, i.Nome, i.Descricao, string.IsNullOrWhiteSpace(i.Tecnologia) ? "Geral" : i.Tecnologia.Trim(), i.Versao))
            .GroupBy(i => i.Tecnologia, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new GrupoDaVitrineV1(g.Key, g.OrderBy(i => i.Tipo, StringComparer.Ordinal).ThenBy(i => i.Nome, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray());

    /// <summary>
    /// O manifesto lido é usável? A versão bate com a tag e todo item tem tipo, nome e hash. O manifesto torto
    /// é tratado como "indisponível" (503), e nunca entregue às máquinas.
    /// </summary>
    public static bool Valido(ManifestoDoCatalogoV1? manifesto, int versaoDaTag) => Problema(manifesto, versaoDaTag) is null;

    /// <summary>
    /// O que torna o manifesto inusável, numa frase para o log (sem conteúdo do item); nulo quando ele é válido. O
    /// "inválido" sem motivo custou o diagnóstico do 503 do básico (ajustes-2, item 15): a <c>catalogo-v28</c> trazia
    /// gatilhos, e só o log genérico ficava.
    /// </summary>
    public static string? Problema(ManifestoDoCatalogoV1? manifesto, int versaoDaTag)
    {
        if (manifesto is not { Itens: not null, Retirados: not null })
            return "o manifesto não tem itens e retirados";
        if (manifesto.Versao != versaoDaTag)
            return $"a versão do manifesto ({manifesto.Versao}) não é a da tag ({versaoDaTag})";
        if (manifesto.Itens.Any(i => i is null))
            return "há item nulo no manifesto";
        if (manifesto.Itens.FirstOrDefault(i => !TipoDoManifesto(i.Tipo)) is { } desconhecido)
            return $"o item \"{desconhecido.Nome}\" tem o tipo desconhecido \"{desconhecido.Tipo}\"";
        if (manifesto.Itens.FirstOrDefault(i => string.IsNullOrWhiteSpace(i.Nome) || string.IsNullOrWhiteSpace(i.Hash)) is { } incompleto)
            return $"o item {incompleto.Tipo}:{incompleto.Nome} não tem nome ou hash";
        return null;
    }

    /// <summary>
    /// A SEQUÊNCIA da publicação pelo Git (o CD do básico, <c>PublicacaoGit</c>) diante da release publicada, numa frase
    /// para a recusa; nula quando ela é aceita. O manifesto do repositório é a fonte: ele numera os itens na sequência
    /// dele e não precisa ser a evolução de UM passo da release anterior. O que ele não pode é substituir nem andar
    /// para trás:
    /// <list type="bullet">
    /// <item>a versão do catálogo é MAIOR que a publicada — o salto é aceito; igual ou menor substituiria uma release;</item>
    /// <item>o item que as duas releases têm não volta a uma versão menor; o item novo entra livremente;</item>
    /// <item>os retirados seguem a regra do <see cref="GeradorDoManifesto"/>: o item que sai vai para os retirados (é
    /// por eles que a máquina o remove), o retirado continua listado ou volta ao catálogo, nenhum com versão menor, e
    /// nenhum item fica ativo e retirado ao mesmo tempo.</item>
    /// </list>
    /// As frases são ASCII, como as demais da publicação pelo Git: chegam ao log do CD pelo <c>devcli catalogo-publicacao</c>.
    /// </summary>
    /// <param name="publicada">A versão da release publicada (a última do básico).</param>
    /// <param name="anterior">O manifesto dessa release.</param>
    /// <param name="novo">O manifesto do pacote do Git, já conferido contra os arquivos.</param>
    public static string? ProblemaDaSequencia(int publicada, ManifestoDoCatalogoV1 anterior, ManifestoDoCatalogoV1 novo)
    {
        if (novo.Versao <= publicada)
            return $"A versao recebida ({novo.Versao}) deve ser maior que a release publicada ({publicada}); igual ou menor substituiria uma release.";

        var ativos = new Dictionary<(string, string), int>();
        foreach (var item in novo.Itens)
            if (!ativos.TryAdd((item.Tipo, item.Nome), item.Versao))
                return $"O item {item.Tipo}:{item.Nome} aparece mais de uma vez no manifesto {novo.Versao}.";
        var retirados = new Dictionary<(string, string), int>();
        foreach (var saiu in novo.Retirados)
        {
            if (ativos.ContainsKey((saiu.Tipo, saiu.Nome)))
                return $"O item {saiu.Tipo}:{saiu.Nome} esta nos itens e nos retirados do manifesto {novo.Versao}.";
            if (!retirados.TryAdd((saiu.Tipo, saiu.Nome), saiu.Versao))
                return $"O retirado {saiu.Tipo}:{saiu.Nome} aparece mais de uma vez no manifesto {novo.Versao}.";
        }

        foreach (var antes in anterior.Itens)
        {
            if (ativos.TryGetValue((antes.Tipo, antes.Nome), out var versao))
            {
                if (versao < antes.Versao)
                    return $"O item {antes.Tipo}:{antes.Nome} retrocede da v{antes.Versao} (release {publicada}) para a v{versao} (manifesto {novo.Versao}).";
            }
            else if (!retirados.TryGetValue((antes.Tipo, antes.Nome), out var retirada))
                return $"O item {antes.Tipo}:{antes.Nome} (v{antes.Versao} na release {publicada}) saiu do manifesto {novo.Versao} sem entrar nos retirados.";
            else if (retirada < antes.Versao)
                return $"O item {antes.Tipo}:{antes.Nome} retrocede da v{antes.Versao} (release {publicada}) para a v{retirada} nos retirados do manifesto {novo.Versao}.";
        }

        foreach (var antes in anterior.Retirados)
        {
            int? versao = ativos.TryGetValue((antes.Tipo, antes.Nome), out var voltou) ? voltou
                : retirados.TryGetValue((antes.Tipo, antes.Nome), out var continua) ? continua : (int?)null;
            if (versao is null)
                return $"O retirado {antes.Tipo}:{antes.Nome} (v{antes.Versao} na release {publicada}) sumiu do manifesto {novo.Versao} sem voltar ao catalogo.";
            if (versao < antes.Versao)
                return $"O retirado {antes.Tipo}:{antes.Nome} retrocede da v{antes.Versao} (release {publicada}) para a v{versao} (manifesto {novo.Versao}).";
        }

        return null;
    }

    /// <summary>
    /// Os tipos que o manifesto do BÁSICO carrega — os do motor do git.kit: skill, plan e fluxo e, desde a US #515, o
    /// gatilho. É a mesma regra do fallback do GitHub e da publicação pelo Git (<c>PublicacaoGit</c>): o
    /// <see cref="TiposDeItem.Todos"/> sozinho é o do catálogo da EMPRESA, que não publica gatilho.
    /// </summary>
    public static bool TipoDoManifesto(string? tipo)
        => tipo is not null && (TiposDeItem.Todos.Contains(tipo) || tipo == GatilhoDoCatalogo.Tipo);

    [GeneratedRegex(@"^catalogo-v(\d{1,9})$", RegexOptions.IgnoreCase)]
    private static partial Regex Tag();
}

/// <summary>
/// As regras do catálogo da EMPRESA (US #425, fase 2), antes do banco: o nome válido, o tamanho, a colisão com o
/// catálogo básico e o hash — publicar o mesmo conteúdo não gera versão nova.
/// </summary>
public static partial class RegrasDoCatalogoDaEmpresa
{
    /// <summary>O tamanho máximo do conteúdo de uma versão (UTF-8).</summary>
    public const int TamanhoMaximoDoConteudo = 256 * 1024;

    public const int TamanhoMaximoDoNome = 64;
    public const int TamanhoMaximoDaDescricao = 500;
    public const int DependenciasMaximas = 50;

    /// <summary>
    /// O problema da publicação, ou vazio. O <paramref name="basico"/> são os nomes do catálogo básico por tipo
    /// (<c>tipo:nome</c>): publicar com o nome de um item do básico o esconderia nas máquinas — recusado.
    /// </summary>
    public static string Validar(PublicacaoV1? publicacao, IReadOnlySet<string> basico)
    {
        if (publicacao is null)
            return "O corpo da publicação é obrigatório.";
        if (!TiposDeItem.Todos.Contains(publicacao.Tipo ?? string.Empty))
            return $"Tipo inválido: use {string.Join(", ", TiposDeItem.Todos)}.";
        var nome = (publicacao.Nome ?? string.Empty).Trim();
        if (!NomeValido(nome))
            return $"Nome inválido: letras minúsculas, números e hífen, de 2 a {TamanhoMaximoDoNome} caracteres, começando por letra ou número.";
        if (basico.Contains(Chave(publicacao.Tipo!, nome)))
            return $"Já existe '{nome}' no catálogo básico do dev.kit: publique com outro nome.";
        if ((publicacao.Descricao ?? string.Empty).Trim().Length > TamanhoMaximoDaDescricao)
            return $"A descrição tem até {TamanhoMaximoDaDescricao} caracteres.";
        var conteudo = publicacao.Conteudo ?? string.Empty;
        if (conteudo.Trim().Length == 0)
            return "O conteúdo do item é obrigatório.";
        if (Encoding.UTF8.GetByteCount(conteudo) > TamanhoMaximoDoConteudo)
            return $"O conteúdo passa de {TamanhoMaximoDoConteudo / 1024} KB.";
        var dependencias = publicacao.Dependencias ?? [];
        if (dependencias.Count > DependenciasMaximas || dependencias.Any(d => Dependencia(d) is null))
            return $"Dependências inválidas: até {DependenciasMaximas}, cada uma como tipo:nome.";
        return string.Empty;
    }

    /// <summary>O nome do item: minúsculas, números e hífen (o nome da pasta da skill no dev.kit).</summary>
    public static bool NomeValido(string? nome) => Nome().IsMatch(nome ?? string.Empty);

    /// <summary>A chave <c>tipo:nome</c> de um item.</summary>
    public static string Chave(string tipo, string nome) => $"{tipo}:{nome}";

    /// <summary>A dependência <c>tipo:nome</c> separada, ou nula quando não é uma.</summary>
    public static (string Tipo, string Nome)? Dependencia(string? texto)
    {
        var partes = (texto ?? string.Empty).Trim().Split(':', 2);
        return partes.Length == 2 && TiposDeItem.Todos.Contains(partes[0]) && NomeValido(partes[1]) ? (partes[0], partes[1]) : null;
    }

    /// <summary>O hash do conteúdo como o dev.kit o calcula: SHA-256 do texto com as quebras de linha normalizadas (LF).</summary>
    public static string Hash(string conteudo)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes((conteudo ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal))));

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{1,63}$")]
    private static partial Regex Nome();
}

/// <summary>
/// O escopo de quem usa o catálogo da empresa e a gestão dela (US #425) — o filtro ÚNICO de isolamento entre
/// empresas, como o <see cref="EscopoDoPainel"/> é o do painel: toda operação o recebe, e o serviço o aplica num
/// ponto só. Nulo (das claims) é quem não tem empresa — o admin, ou um token torto: a rota responde 403.
/// </summary>
/// <param name="EmpresaId">A empresa de quem consulta.</param>
/// <param name="UsuarioId">Quem consulta (o autor de uma publicação).</param>
/// <param name="EhGestor">O gestor administra a empresa: retira o item de qualquer autor e bloqueia usuários.</param>
/// <param name="EhAdmin">
/// O admin geral agindo NUMA empresa (US #550, <see cref="DoAdmin"/>): vale como gestor dela e grava também o nível
/// usuário de quem é dela. Nunca vem das claims — só das rotas do admin, que dizem a empresa no caminho.
/// </param>
public sealed record EscopoDaEmpresa(int EmpresaId, int UsuarioId, bool EhGestor, bool EhAdmin = false)
{
    /// <summary>O escopo do admin geral na empresa <paramref name="empresaId"/> (US #550): gestor dela, e mais.</summary>
    public static EscopoDaEmpresa DoAdmin(int empresaId, int adminId) => new(empresaId, adminId, EhGestor: true, EhAdmin: true);

    /// <summary>O escopo das claims do token: gestor ou dev COM empresa; qualquer outra combinação é nula.</summary>
    public static EscopoDaEmpresa? DasClaims(string? papel, string? empresa, string? usuario)
    {
        if (papel is not (Papeis.Gestor or Papeis.Dev))
            return null;
        if (!int.TryParse(empresa, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var empresaId) || empresaId <= 0)
            return null;
        if (!int.TryParse(usuario, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var usuarioId) || usuarioId <= 0)
            return null;
        return new EscopoDaEmpresa(empresaId, usuarioId, papel == Papeis.Gestor);
    }
}
