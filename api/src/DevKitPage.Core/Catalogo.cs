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
    public static bool Valido(ManifestoDoCatalogoV1? manifesto, int versaoDaTag)
        => manifesto is { Itens: not null, Retirados: not null }
           && manifesto.Versao == versaoDaTag
           && manifesto.Itens.All(i => i is not null && TiposDeItem.Todos.Contains(i.Tipo) && !string.IsNullOrWhiteSpace(i.Nome) && !string.IsNullOrWhiteSpace(i.Hash));

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
