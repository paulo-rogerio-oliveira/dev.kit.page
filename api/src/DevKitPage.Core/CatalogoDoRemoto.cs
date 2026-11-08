using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// O último CATÁLOGO de uma máquina (o handshake do chat remoto): os recursos que o dev.kit publicou para os atalhos do chat
/// do Board. Um por (<see cref="UsuarioId"/>, <see cref="MaquinaId"/>), inteiro em JSON — é lido e substituído inteiro, como
/// o retrato. Fica numa tabela própria, e não na da máquina, para o heartbeat de cada minuto não regravar a linha que o
/// catálogo engorda.
/// </summary>
public sealed class CatalogoRemoto
{
    public long Id { get; set; }

    /// <summary>O dono: o usuário do JWT que publicou. Só ele lê.</summary>
    public int UsuarioId { get; set; }

    public string MaquinaId { get; set; } = string.Empty;

    /// <summary>O último catálogo aplicado, em JSON (<c>CatalogoRemotoV1</c>).</summary>
    public string Catalogo { get; set; } = string.Empty;

    /// <summary>O <c>GeradoEm</c> do catálogo gravado: o mais antigo que chegar depois não o substitui.</summary>
    public DateTime GeradoEmUtc { get; set; }

    /// <summary>Quando a API o recebeu (o relógio dela): é a data que o site mostra, e a retenção conta daqui.</summary>
    public DateTime RecebidoEmUtc { get; set; }
}

/// <summary>
/// O catálogo das máquinas no banco: o dev.kit grava (só na máquina que já mandou o heartbeat), o site lê — só o dono; a
/// máquina de outro usuário é inexistente (nulo → 404). Os envios chegam JÁ validados (<see cref="RegrasDoCatalogo"/>).
/// </summary>
public interface ICatalogoRemoto
{
    /// <summary>
    /// Substitui o catálogo — a não ser que o gravado tenha sido gerado depois (o envio fora de ordem). Nulo quando a máquina
    /// não é do usuário ou ainda não mandou heartbeat.
    /// </summary>
    Task<CatalogoRecebidoV1?> GravarAsync(int usuarioId, CatalogoRemotoV1 catalogo, CancellationToken ct);

    /// <summary>O último catálogo da máquina do usuário; nulo quando ela não é dele ou ainda não mandou catálogo.</summary>
    Task<CatalogoDaMaquinaV1?> LerAsync(int usuarioId, string maquinaId, CancellationToken ct);
}

/// <summary>As regras puras do catálogo: a forma (os tetos do <see cref="RemotoV1"/>) e os campos com dado sensível. Sem banco.</summary>
public static class RegrasDoCatalogo
{
    /// <summary>O problema do catálogo, ou vazio quando ele vale (o contrato é conferido à parte, com código próprio).</summary>
    public static string Validar(CatalogoRemotoV1? catalogo)
    {
        if (catalogo is null)
            return "Envie o catálogo.";
        if (!RegrasDoRemoto.MaquinaIdValido(catalogo.MaquinaId))
            return $"O maquinaId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";
        if (catalogo.GeradoEm == default)
            return "Informe quando o catálogo foi gerado (geradoEm).";
        if (!string.IsNullOrEmpty(catalogo.ComandoId) && !RegrasDaConversa.IdValido(catalogo.ComandoId))
            return $"O comandoId usa letras, dígitos e hífen (até {RemotoV1.TamanhoMaximoDoId}).";

        var listas = new (string Nome, int Quantos)[]
        {
            ("workItems", catalogo.WorkItems?.Count ?? 0), ("repositorios", catalogo.Repositorios?.Count ?? 0),
            ("fluxos", catalogo.Fluxos?.Count ?? 0), ("projetos", catalogo.Projetos?.Count ?? 0), ("designs", catalogo.Designs?.Count ?? 0),
            ("modelos", catalogo.Modelos?.Count ?? 0),
        };
        if (listas.FirstOrDefault(l => l.Quantos > RemotoV1.MaximoNoCatalogo) is { Nome: not null } cheia)
            return $"{cheia.Nome}: até {RemotoV1.MaximoNoCatalogo} entradas.";

        foreach (var (w, i) in (catalogo.WorkItems ?? []).Select((w, i) => (w, i)))
        {
            if (w is null || w.Id <= 0 || Invalido(w.Titulo) || Longo(w.Tipo) || Longo(w.Estado))
                return $"workItems[{i}]: id positivo e título, tipo e estado de até {RemotoV1.TamanhoMaximoDoNome} caracteres.";
        }
        foreach (var (r, i) in (catalogo.Repositorios ?? []).Select((r, i) => (r, i)))
        {
            if (r is null || Invalido(r.Nome) || Longo(r.BranchPadrao))
                return $"repositorios[{i}]: informe o nome; nome e branch padrão têm até {RemotoV1.TamanhoMaximoDoNome} caracteres.";
        }
        foreach (var (f, i) in (catalogo.Fluxos ?? []).Select((f, i) => (f, i)))
        {
            if (f is null || Invalido(f.Nome) || (f.Descricao?.Length ?? 0) > RemotoV1.TamanhoMaximoDoTexto)
                return $"fluxos[{i}]: informe o nome (até {RemotoV1.TamanhoMaximoDoNome}) e a descrição de até {RemotoV1.TamanhoMaximoDoTexto} caracteres.";
            if (f.Agente is not null && !AgenteValido(f.Agente))
                return $"fluxos[{i}]: o agente padrão é cli ou cli:modelo (letras, dígitos, ponto, hífen e sublinhado; até {RemotoV1.TamanhoMaximoDoNome}).";
        }
        foreach (var (m, i) in (catalogo.Modelos ?? []).Select((m, i) => (m, i)))
        {
            if (m is null || !AgenteValido(m.Valor) || Invalido(m.Rotulo))
                return $"modelos[{i}]: o valor é cli ou cli:modelo e o rótulo tem até {RemotoV1.TamanhoMaximoDoNome} caracteres.";
        }
        foreach (var (p, i) in (catalogo.Projetos ?? []).Select((p, i) => (p, i)))
        {
            if (p is null || !RegrasDosProjetos.SlugValido(p.Slug))
                return $"projetos[{i}]: o slug usa minúsculas, dígitos e hífen (até {ProjetosV1.TamanhoMaximoDoSlug}).";
            if (Invalido(p.Nome) || Longo(p.Situacao))
                return $"projetos[{i}]: informe o nome; nome e situação têm até {RemotoV1.TamanhoMaximoDoNome} caracteres.";
        }
        foreach (var (d, i) in (catalogo.Designs ?? []).Select((d, i) => (d, i)))
        {
            if (d is null || Invalido(d.Nome) || d.Telas < 0)
                return $"designs[{i}]: informe o nome (até {RemotoV1.TamanhoMaximoDoNome} caracteres) e as telas, não negativas.";
        }
        return string.Empty;
    }

    /// <summary>Os campos do catálogo com dado sensível — TODO texto, com o caminho JSON dele; vazio quando ele pode ser gravado.</summary>
    public static IReadOnlyList<CampoSensivelV1> CamposSensiveis(CatalogoRemotoV1 catalogo)
        => RegrasDoRemoto.Sensiveis(Textos(catalogo));

    private static IEnumerable<(string Campo, string? Texto)> Textos(CatalogoRemotoV1 c)
    {
        yield return ("maquinaId", c.MaquinaId);
        yield return ("comandoId", c.ComandoId);
        foreach (var (w, i) in (c.WorkItems ?? []).Select((w, i) => (w, i)))
        {
            yield return ($"workItems[{i}].titulo", w.Titulo);
            yield return ($"workItems[{i}].tipo", w.Tipo);
            yield return ($"workItems[{i}].estado", w.Estado);
        }
        foreach (var (r, i) in (c.Repositorios ?? []).Select((r, i) => (r, i)))
        {
            yield return ($"repositorios[{i}].nome", r.Nome);
            yield return ($"repositorios[{i}].branchPadrao", r.BranchPadrao);
        }
        foreach (var (f, i) in (c.Fluxos ?? []).Select((f, i) => (f, i)))
        {
            yield return ($"fluxos[{i}].nome", f.Nome);
            yield return ($"fluxos[{i}].descricao", f.Descricao);
            yield return ($"fluxos[{i}].agente", f.Agente);
        }
        foreach (var (m, i) in (c.Modelos ?? []).Select((m, i) => (m, i)))
        {
            yield return ($"modelos[{i}].valor", m.Valor);
            yield return ($"modelos[{i}].rotulo", m.Rotulo);
        }
        foreach (var (p, i) in (c.Projetos ?? []).Select((p, i) => (p, i)))
        {
            yield return ($"projetos[{i}].slug", p.Slug);
            yield return ($"projetos[{i}].nome", p.Nome);
            yield return ($"projetos[{i}].situacao", p.Situacao);
        }
        foreach (var (d, i) in (c.Designs ?? []).Select((d, i) => (d, i)))
            yield return ($"designs[{i}].nome", d.Nome);
    }

    private static bool Invalido(string? texto) => string.IsNullOrWhiteSpace(texto) || texto.Length > RemotoV1.TamanhoMaximoDoNome;

    /// <summary>
    /// (US #867) O agente de um fluxo: <c>cli</c> ou <c>cli:modelo</c>, só com letras, dígitos, ponto, hífen, sublinhado, barra e
    /// colchetes (<c>opus[1m]</c>, <c>z-ai/glm-4.6</c>) — ele vai ao Orquestrador como o <c>--agente</c> do devcli, e um espaço,
    /// aspas ou barra vertical quebrariam o argumento. O dev.kit confere a MESMA forma antes de enviar (<c>CatalogoDoChatRemoto</c>).
    /// </summary>
    public static bool AgenteValido(string? agente)
        => !string.IsNullOrWhiteSpace(agente) && agente.Length <= RemotoV1.TamanhoMaximoDoNome
           && System.Text.RegularExpressions.Regex.IsMatch(agente, @"^[a-z][a-z0-9-]*(:[A-Za-z0-9._/\[\]-]+)?$");

    private static bool Longo(string? texto) => (texto?.Length ?? 0) > RemotoV1.TamanhoMaximoDoNome;
}
