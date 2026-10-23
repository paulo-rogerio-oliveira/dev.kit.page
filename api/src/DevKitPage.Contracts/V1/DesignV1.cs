namespace DevKitPage.Contracts.V1;

/// <summary>
/// Um PROJETO DE DESIGN (US #669): as telas de UM repositório (a URL normalizada, <see cref="DesignV1.ChaveDoRepositorio"/>)
/// num nível de compartilhamento — <c>empresa</c>, <c>time</c> ou <c>usuario</c>, a mesma regra do contexto. O branch NÃO
/// separa projetos: é atributo de cada versão.
/// </summary>
/// <param name="Id">O número do projeto — o primeiro segmento do link estável.</param>
/// <param name="Repositorio">O caminho do repositório como veio na primeira versão.</param>
/// <param name="Chave">O caminho normalizado (a unicidade no nível).</param>
/// <param name="Nivel">Um de <see cref="NiveisDeConvencao"/>.</param>
/// <param name="Time">O time do nível <c>time</c>; vazio nos outros.</param>
/// <param name="Dono">O dono do nível <c>usuario</c> (o nome, ou o login); vazio nos outros.</param>
/// <param name="AtualizadoEm">A última versão enviada ou apagada — o <c>?desde=</c> da sincronização compara com ela.</param>
public sealed record ProjetoDeDesignV1(
    long Id, string Repositorio, string Chave, string Nivel, string Time, string Dono, DateTimeOffset AtualizadoEm,
    IReadOnlyList<TelaDeDesignV1> Telas);

/// <summary>Uma tela do projeto: o identificador (o do dev.kit), o nome, o estado e as versões em ordem.</summary>
public sealed record TelaDeDesignV1(string Id, string Nome, string Estado, IReadOnlyList<VersaoDeDesignV1> Versoes);

/// <summary>
/// Uma VERSÃO imutável de uma tela: captura (a tela real) ou proposta (imagem, nada implementado). A imagem é baixada
/// pelo <see cref="Link"/> estável — o mesmo que a PR da semana cita.
/// </summary>
/// <param name="Base">A versão de origem da proposta (ou da recaptura); nula na primeira captura.</param>
/// <param name="Origem"><c>web</c>, <c>desktop</c> ou <c>proposta</c>.</param>
/// <param name="Alvo">A URL, o executável ou o HTML de onde a versão saiu.</param>
/// <param name="Branch">O branch de onde a versão saiu (atributo, e não projeto).</param>
/// <param name="Commit">O commit (curto) do branch quando a versão foi gravada.</param>
/// <param name="Autor">Quem enviou (o nome, ou o login).</param>
/// <param name="AutorTipo"><c>usuario</c> (o dev, pela tela) ou <c>agente</c> (pelo devcli).</param>
/// <param name="Runner">O agente que produziu a versão (claude, kiro…); vazio quando foi o dev.</param>
/// <param name="CriadaEm">Quando a versão foi gravada no dev.kit (preservada na migração).</param>
/// <param name="Sha256">O hash do PNG — a idempotência do reenvio.</param>
/// <param name="Link">O caminho da imagem na API (<see cref="DesignV1.Link"/>).</param>
/// <param name="PodeApagar">Quem consulta pode apagar a versão (o autor ou o gestor).</param>
public sealed record VersaoDeDesignV1(
    int Numero, string Tipo, int? Base, string Tema, int Largura, int Altura, string Origem, string Alvo,
    string Branch, string Commit, string Descricao, string Autor, string AutorTipo, string Runner,
    DateTimeOffset CriadaEm, string Sha256, long Bytes, string Link, bool PodeApagar);

/// <summary>A resposta do envio: o projeto, a tela e a versão gravada — <see cref="Nova"/> falso é o reenvio do mesmo PNG.</summary>
public sealed record VersaoEnviadaV1(long Projeto, string Repositorio, string Tela, VersaoDeDesignV1 Versao, bool Nova);

/// <summary>O uso do teto do plano por quem consulta: a empresa dele, ou ele mesmo sem empresa.</summary>
public sealed record UsoDoDesignV1(int Versoes, int TetoDeVersoes, long Bytes, long TetoDeBytes, string Plano);

/// <summary>Os nomes e os tetos do contrato v1 do design (o dev.kit os confere antes de enfileirar).</summary>
public static class DesignV1
{
    /// <summary>O PNG de uma versão, em bytes: 16 MB.</summary>
    public const int TamanhoMaximoDaImagem = 16 * 1024 * 1024;

    public const int TamanhoMaximoDoId = 96;
    public const int TamanhoMaximoDoNome = 200;
    public const int TamanhoMaximoDoEstado = 60;
    public const int TamanhoMaximoDoAlvo = 1000;
    public const int TamanhoMaximoDoBranch = 250;
    public const int TamanhoMaximoDoCommit = 64;
    public const int TamanhoMaximoDaDescricao = 2000;
    public const int TamanhoMaximoDoRunner = 60;
    public const int TamanhoMaximoDoRepositorio = 400;

    public const string Captura = "captura";
    public const string Proposta = "proposta";
    public const string AutorUsuario = "usuario";
    public const string AutorAgente = "agente";

    public static IReadOnlyList<string> Tipos { get; } = [Captura, Proposta];
    public static IReadOnlyList<string> Temas { get; } = ["light", "dark"];
    public static IReadOnlyList<string> Origens { get; } = ["web", "desktop", Proposta];
    public static IReadOnlyList<string> Autores { get; } = [AutorUsuario, AutorAgente];

    /// <summary>
    /// O caminho estável da imagem de uma versão na API. A página do site que a mostra é o mesmo caminho sem o
    /// <c>/api</c> (<c>/design/{projeto}/{tela}/v{numero}</c>) — é ela que a PR da semana cita.
    /// </summary>
    public static string Link(long projeto, string tela, int numero) => $"/api/design/{projeto}/{tela}/v{numero}";

    /// <summary>A página estável da versão no site (o link da PR).</summary>
    public static string Pagina(long projeto, string tela, int numero) => $"/design/{projeto}/{tela}/v{numero}";

    /// <summary>
    /// O caminho do repositório normalizado — a MESMA regra do dev.kit, para os dois lados chegarem à mesma chave:
    /// sem espaço nas pontas, sem esquema (<c>https://</c>, <c>ssh://</c>), sem usuário (<c>git@</c>), com o
    /// <c>host:caminho</c> do ssh virando <c>host/caminho</c>, sem barra nem <c>.git</c> no fim e em minúsculas.
    /// <c>https://github.com/Acme/Portal.git</c>, <c>git@github.com:acme/portal</c> e
    /// <c>ssh://git@github.com/acme/portal/</c> são <c>github.com/acme/portal</c>. Uma pasta local fica com barras normais.
    /// </summary>
    public static string ChaveDoRepositorio(string? repositorio)
    {
        var chave = (repositorio ?? string.Empty).Trim().Replace('\\', '/');
        var esquema = chave.IndexOf("://", StringComparison.Ordinal);
        var tinhaEsquema = esquema > 0;
        if (tinhaEsquema)
            chave = chave[(esquema + 3)..];
        var arroba = chave.IndexOf('@');
        var barra = chave.IndexOf('/');
        if (arroba > 0 && (barra < 0 || arroba < barra))
            chave = chave[(arroba + 1)..];
        // O scp-like do ssh (host:org/repo): o primeiro ':' antes da primeira barra separa o host do caminho. Não vale
        // para a letra de drive de uma pasta (C:/...), nem para a porta de uma URL (host:22/...).
        var doisPontos = chave.IndexOf(':');
        barra = chave.IndexOf('/');
        if (!tinhaEsquema && doisPontos > 1 && (barra < 0 || doisPontos < barra))
            chave = chave[..doisPontos] + "/" + chave[(doisPontos + 1)..].TrimStart('/');
        chave = chave.TrimEnd('/');
        if (chave.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            chave = chave[..^4];
        return chave.TrimEnd('/').ToLowerInvariant();
    }
}
