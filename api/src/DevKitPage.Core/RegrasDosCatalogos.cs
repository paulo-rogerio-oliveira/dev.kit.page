using System.Text;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>Um item editável já conferido: a identidade, o conteúdo canônico (o JSON que vai no zip) e o que ele usa.</summary>
/// <param name="Embutidos">Os Tech Plans que o fluxo traz dentro de si (<c>planos[]</c>): a importação os cria na máquina.</param>
public sealed record ItemConferido(
    string Tipo, string Nome, string Descricao, string Conteudo, string Hash, IReadOnlyList<string> Dependencias, IReadOnlyList<string> Embutidos);

/// <summary>Um item ativo de um catálogo, para a conferência das dependências na publicação.</summary>
public sealed record ItemParaPublicar(string Tipo, string Nome, IReadOnlyList<string> Dependencias, IReadOnlyList<string> Embutidos);

/// <summary>
/// As regras PURAS dos catálogos nomeados (US #461), antes do banco: o slug, o item editável (o tipo, o formato do
/// dev.kit, o nome e o tamanho), a colisão de nomes e o fechamento das dependências da publicação — "todo recurso que um
/// item referencia segue junto na sincronia".
/// </summary>
public static partial class RegrasDosCatalogos
{
    public const int TamanhoMaximoDoSlug = 40;
    public const int TamanhoMaximoDoNome = 80;
    public const int TamanhoMaximoDaDescricao = 500;
    public const int TamanhoMaximoDaCategoria = 60;

    /// <summary>O slug do catálogo: minúsculas, números e hífen, de 2 a 40, começando por letra ou número, e não reservado.</summary>
    public static bool SlugValido(string? slug)
        => Slug().IsMatch(slug ?? string.Empty) && !Catalogos.Reservados.Contains(slug!);

    /// <summary>Os problemas do catálogo novo, por campo (vazio = válido).</summary>
    public static Dictionary<string, string[]> Validar(CatalogoNovoV1? novo)
    {
        var erros = new Dictionary<string, string[]>();
        if (novo is null)
            return new() { ["corpo"] = ["O corpo do catálogo é obrigatório."] };
        if (!SlugValido(novo.Slug?.Trim()))
            erros["slug"] = [$"Slug inválido: letras minúsculas, números e hífen, de 2 a {TamanhoMaximoDoSlug} caracteres, começando por letra ou número (e não '{string.Join("', '", Catalogos.Reservados)}')."];
        foreach (var (campo, erro) in ValidarTextos(novo.Nome, novo.Descricao))
            erros[campo] = [erro];
        return erros;
    }

    /// <summary>Os problemas da edição do catálogo, por campo.</summary>
    public static Dictionary<string, string[]> Validar(CatalogoEditadoV1? edicao)
        => edicao is null
            ? new() { ["corpo"] = ["O corpo do catálogo é obrigatório."] }
            : ValidarTextos(edicao.Nome, edicao.Descricao).ToDictionary(e => e.Campo, e => new[] { e.Erro });

    /// <summary>
    /// Confere o item como o app o leria (as mensagens são as dele) e devolve o conteúdo CANÔNICO — o mesmo JSON que o
    /// app exporta, na ordem dos campos dele. O nome é a identidade na máquina: o da skill, a chave do plan, o slug do
    /// nome do fluxo; todos têm de servir de nome de pasta (minúsculas, números e hífen).
    /// </summary>
    /// <returns>O item conferido, ou o erro (o item nulo).</returns>
    public static (ItemConferido? Item, string Erro) Conferir(ItemEditadoV1? editado)
    {
        if (editado is null)
            return (null, "O corpo do item é obrigatório.");
        if (!TiposDeItem.Todos.Contains(editado.Tipo ?? string.Empty))
            return (null, $"Tipo inválido: use {string.Join(", ", TiposDeItem.Todos)}.");
        var conteudo = editado.Conteudo ?? string.Empty;
        if (conteudo.Trim().Length == 0)
            return (null, "O conteúdo do item é obrigatório.");
        if (Encoding.UTF8.GetByteCount(conteudo) > RegrasDoCatalogoDaEmpresa.TamanhoMaximoDoConteudo)
            return (null, $"O conteúdo passa de {RegrasDoCatalogoDaEmpresa.TamanhoMaximoDoConteudo / 1024} KB.");
        if ((editado.Categoria ?? string.Empty).Trim().Length > TamanhoMaximoDaCategoria)
            return (null, $"A categoria tem até {TamanhoMaximoDaCategoria} caracteres.");

        ItemConferido item;
        try
        {
            item = editado.Tipo switch
            {
                TiposDeItem.Skill => Skill(conteudo),
                TiposDeItem.Plan => Plan(conteudo),
                _ => Fluxo(conteudo),
            };
        }
        catch (FormatException ex)
        {
            return (null, ex.Message);
        }

        if (!NomeAceito(item.Nome))
            return (null, $"Nome inválido: '{item.Nome}' é a identidade do item na máquina — de 2 a {RegrasDoCatalogoDaEmpresa.TamanhoMaximoDoNome} caracteres, numa linha só.");
        if (item.Descricao.Length > TamanhoMaximoDaDescricao)
            return (null, $"A descrição tem até {TamanhoMaximoDaDescricao} caracteres.");
        return (item, string.Empty);
    }

    /// <summary>
    /// A situação do rascunho frente ao item da última release (<paramref name="publicado"/>, nulo quando ele não está
    /// nela) e a versão publicada. Dados da vitrine diferentes (descrição, categoria, arquivo) também são pendência: a
    /// próxima release os leva, como o gerador faz.
    /// </summary>
    public static (string Situacao, int VersaoPublicada) Situacao(
        string tipo, string nome, string hash, string descricao, string categoria, string arquivo, bool retirado, ItemDoCatalogoV1? publicado)
    {
        if (retirado)
            return (publicado is null ? SituacoesDoItem.Retirado : SituacoesDoItem.RetiradaPendente, publicado?.Versao ?? 0);
        if (publicado is null)
            return (SituacoesDoItem.Novo, 0);
        var mudou = publicado.Hash != hash || publicado.Descricao != descricao || publicado.Arquivo != arquivo
                    || publicado.Tecnologia != Tecnologia(tipo, nome, categoria);
        return (mudou ? SituacoesDoItem.Alterado : SituacoesDoItem.Publicado, publicado.Versao);
    }

    /// <summary>A categoria que vai para o manifesto: a informada, ou a derivada do nome.</summary>
    public static string Tecnologia(string tipo, string nome, string? categoria)
        => string.IsNullOrWhiteSpace(categoria) ? GeradorDoManifesto.Tecnologia(tipo, nome) : categoria.Trim();

    /// <summary>
    /// O nome que o APP aceita (e não o do catálogo da empresa, só minúsculas e hífen): o básico real tem a skill
    /// <c>resolver conflitos</c>, com espaço, e a pasta dela na máquina sai do slug. Recusa-se só o vazio, o curto, o
    /// longo demais e o que quebra a linha.
    /// </summary>
    public static bool NomeAceito(string? nome)
        => nome is { Length: >= 2 } && nome.Length <= RegrasDoCatalogoDaEmpresa.TamanhoMaximoDoNome && !nome.Any(char.IsControl);

    /// <summary>O caminho do item no zip quando ele nasce no site: a pasta do tipo e o slug do nome (o nome pode ter espaço).</summary>
    public static string Arquivo(string tipo, string nome) => $"{FormatosDoCatalogo.Pasta(tipo)}/{FormatosDoCatalogo.Slug(nome, tipo)}.json";

    /// <summary>
    /// As colisões de um catálogo com os outros já relacionados à MESMA empresa: na máquina o item é identificado por
    /// tipo e nome, e dois catálogos com o mesmo item brigariam por ele a cada sincronia.
    /// </summary>
    /// <param name="itens">As chaves <c>tipo:nome</c> dos itens ativos do catálogo.</param>
    /// <param name="outros">Os outros catálogos da empresa: o nome e as chaves dos itens ativos de cada um.</param>
    public static IReadOnlyList<string> Colisoes(IReadOnlySet<string> itens, IEnumerable<(string Catalogo, IReadOnlySet<string> Itens)> outros)
        => outros.SelectMany(o => o.Itens.Where(itens.Contains).Select(chave => $"{chave} (no catálogo '{o.Catalogo}')"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// O FECHAMENTO das dependências da publicação. Cada dependência (<c>tipo:nome</c>) de um item ativo tem de resolver
    /// no próprio catálogo (o item vai no mesmo pacote), num plan embutido no próprio fluxo (a importação o cria) ou no
    /// básico (que toda máquina sincroniza). A de outro catálogo nomeado, ou inexistente, recusa a publicação: a
    /// máquina receberia o fluxo sem a skill.
    /// </summary>
    /// <param name="basico">As chaves dos itens do básico (vazio quando o catálogo publicado É o básico).</param>
    /// <param name="outros">Chave → nome do catálogo nomeado onde o item está (para a mensagem dizer onde copiar).</param>
    public static IReadOnlyList<string> Dependencias(
        IReadOnlyList<ItemParaPublicar> itens, IReadOnlySet<string> basico, IReadOnlyDictionary<string, string> outros)
    {
        var proprios = itens.Select(i => RegrasDoCatalogoDaEmpresa.Chave(i.Tipo, i.Nome)).ToHashSet(StringComparer.Ordinal);
        var problemas = new List<string>();
        foreach (var item in itens)
        {
            var embutidos = item.Embutidos.Select(p => RegrasDoCatalogoDaEmpresa.Chave(TiposDeItem.Plan, p)).ToHashSet(StringComparer.Ordinal);
            foreach (var dependencia in item.Dependencias)
            {
                if (proprios.Contains(dependencia) || embutidos.Contains(dependencia) || basico.Contains(dependencia))
                    continue;
                var dono = $"{item.Tipo}:{item.Nome}";
                problemas.Add(outros.TryGetValue(dependencia, out var catalogo)
                    ? $"{dono} usa {dependencia}, que está no catálogo '{catalogo}': copie o item para este catálogo (a máquina pode não ter o outro)."
                    : $"{dono} usa {dependencia}, que não está neste catálogo nem no básico.");
            }
        }

        return problemas;
    }

    private static IEnumerable<(string Campo, string Erro)> ValidarTextos(string? nome, string? descricao)
    {
        var limpo = (nome ?? string.Empty).Trim();
        if (limpo.Length == 0 || limpo.Length > TamanhoMaximoDoNome)
            yield return ("nome", $"Informe o nome do catálogo (até {TamanhoMaximoDoNome} caracteres): é o que o dev.kit mostra na coluna Origem.");
        if ((descricao ?? string.Empty).Trim().Length > TamanhoMaximoDaDescricao)
            yield return ("descricao", $"A descrição tem até {TamanhoMaximoDaDescricao} caracteres.");
    }

    private static ItemConferido Skill(string conteudo)
    {
        var skill = FormatosDoCatalogo.LerSkill(conteudo);
        return new ItemConferido(TiposDeItem.Skill, skill.Name, skill.Description, FormatosDoCatalogo.ExportarSkill(skill), FormatosDoCatalogo.HashDaSkill(skill), [], []);
    }

    private static ItemConferido Plan(string conteudo)
    {
        var plano = FormatosDoCatalogo.LerPlan(conteudo);
        return new ItemConferido(TiposDeItem.Plan, FormatosDoCatalogo.Chave(plano), plano.Description, FormatosDoCatalogo.ExportarPlan(plano),
            FormatosDoCatalogo.HashDoPlan(plano), [], []);
    }

    private static ItemConferido Fluxo(string conteudo)
    {
        var fluxo = FormatosDoCatalogo.LerFluxo(conteudo);
        var texto = FormatosDoCatalogo.ExportarFluxo(fluxo.Raiz);
        return new ItemConferido(TiposDeItem.Fluxo, FormatosDoCatalogo.Slug(fluxo.Nome, "fluxo"), fluxo.Descricao.Length > 0 ? fluxo.Descricao : fluxo.Nome,
            texto, FormatosDoCatalogo.HashDoFluxo(texto), GeradorDoManifesto.DependenciasDoFluxo(fluxo.Raiz), GeradorDoManifesto.PlansEmbutidos(fluxo.Raiz));
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9\-]{1,39}$")]
    private static partial Regex Slug();
}
