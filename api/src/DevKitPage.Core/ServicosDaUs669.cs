using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// Quem consulta o DESIGN (US #669): o usuário do token, a empresa dele (nula para quem não tem — o design dele fica no
/// nível usuário) e se é gestor. Ao contrário do <see cref="EscopoDaEmpresa"/>, existe SEM empresa: o pedido é que o
/// design de quem trabalha sozinho também saia da máquina.
/// </summary>
public sealed record EscopoDoDesign(int UsuarioId, int? EmpresaId, bool EhGestor)
{
    /// <summary>O escopo das claims do token: qualquer usuário autenticado; gestor só com empresa.</summary>
    public static EscopoDoDesign? DasClaims(string? papel, string? empresa, string? usuario)
    {
        if (!int.TryParse(usuario, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var usuarioId) || usuarioId <= 0)
            return null;
        int? empresaId = int.TryParse(empresa, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
        return new EscopoDoDesign(usuarioId, empresaId, empresaId is not null && papel == Papeis.Gestor);
    }
}

/// <summary>Os metadados de uma versão enviada (o multipart, sem o PNG), JÁ lidos da requisição.</summary>
/// <param name="Numero">O número da versão no dev.kit; nulo, o próximo da tela.</param>
/// <param name="Nivel">O nível pedido; sem empresa, vale sempre <c>usuario</c>.</param>
/// <param name="CriadaEm">A data da versão no dev.kit (a migração preserva a original); nula, agora.</param>
public sealed record EnvioDeVersaoDeDesign(
    string? Repositorio, string? Nivel, string? Time, string? Tela, string? Nome, string? Estado, int? Numero, string? Tipo,
    int? Base, string? Tema, string? Origem, string? Alvo, string? Branch, string? Commit, string? Descricao,
    string? AutorTipo, string? Runner, DateTimeOffset? CriadaEm);

/// <summary>Como terminou um envio de versão — cada um vira um status HTTP na rota.</summary>
public enum SituacaoDoEnvio
{
    /// <summary>Gravada agora: 201.</summary>
    Nova,

    /// <summary>O reenvio do MESMO PNG para o mesmo número: 200 com a versão que já existia.</summary>
    Repetida,

    /// <summary>O número já tem outro PNG: 409 — o dev.kit não sobrescreve.</summary>
    Conflito,

    /// <summary>O teto do plano estourou (quantidade ou tamanho): 413.</summary>
    Teto,

    /// <summary>Os dados não valem (a base que não existe, o time sem empresa): 400.</summary>
    Recusada,
}

/// <summary>O resultado do envio: a versão (na nova e na repetida) ou a mensagem.</summary>
public sealed record ResultadoDoEnvio(SituacaoDoEnvio Situacao, VersaoEnviadaV1? Versao, string Mensagem = "");

/// <summary>A imagem aberta do armazém, com o tamanho e a data de quando foi gravada.</summary>
public sealed record ImagemDeDesign(byte[] Conteudo, string Sha256, DateTimeOffset CriadaEm);

/// <summary>
/// O ARMAZÉM das imagens do design (US #669) — no Azure, o Blob Storage; em desenvolvimento e nos testes, o disco. A
/// chave é opaca para quem chama (<see cref="RegrasDoDesign.ChaveDaImagem"/>), e gravar a mesma chave de novo substitui.
/// </summary>
public interface IArmazemDeImagens
{
    Task GravarAsync(string chave, byte[] conteudo, CancellationToken ct);

    /// <summary>Os bytes da chave; nulo quando ela não existe.</summary>
    Task<byte[]?> LerAsync(string chave, CancellationToken ct);

    /// <summary>Apaga a chave; a que não existe é ignorada.</summary>
    Task ApagarAsync(string chave, CancellationToken ct);
}

/// <summary>
/// O DESIGN compartilhado (US #669): os projetos por repositório, as telas e as versões, com as imagens no
/// <see cref="IArmazemDeImagens"/>. Toda operação recebe o <see cref="EscopoDoDesign"/> e o aplica aqui, num ponto só: a
/// mesma visibilidade do contexto — o nível empresa para todos da empresa, o time para quem o declara, o usuário só
/// para o dono.
/// </summary>
public interface IDesignDaEmpresa
{
    /// <summary>
    /// Os projetos visíveis, com as telas e as versões. Com <paramref name="time"/>, o nível time só desse time (o que o
    /// dev.kit lê); sem ele, todos os times da empresa (o site). Com <paramref name="desde"/>, só os projetos alterados
    /// depois — a sincronização incremental da abertura do dev.kit.
    /// </summary>
    Task<IReadOnlyList<ProjetoDeDesignV1>> ProjetosAsync(EscopoDoDesign escopo, string? time, DateTimeOffset? desde, CancellationToken ct);

    /// <summary>Um projeto visível; nulo quando ele não existe ou não é visível (404, sem revelar qual dos dois).</summary>
    Task<ProjetoDeDesignV1?> ProjetoAsync(EscopoDoDesign escopo, long projeto, CancellationToken ct);

    /// <summary>
    /// Grava a versão JÁ validada (<see cref="RegrasDoDesign.Validar"/>) e o PNG dela, criando o projeto e a tela que
    /// faltarem. Idempotente: a chave é (projeto, tela, número) + o hash.
    /// </summary>
    Task<ResultadoDoEnvio> EnviarAsync(EscopoDoDesign escopo, EnvioDeVersaoDeDesign envio, byte[] png, CancellationToken ct);

    /// <summary>A imagem da versão visível; nula quando ela não existe ou não é visível.</summary>
    Task<ImagemDeDesign?> ImagemAsync(EscopoDoDesign escopo, long projeto, string tela, int numero, CancellationToken ct);

    /// <summary>Apaga a versão (e a imagem): só o autor dela ou o gestor da empresa.</summary>
    Task<ResultadoDaGestao<VersaoDeDesignV1>> ApagarAsync(EscopoDoDesign escopo, long projeto, string tela, int numero, CancellationToken ct);

    /// <summary>O uso do teto do plano por quem consulta (a empresa dele, ou ele mesmo).</summary>
    Task<UsoDoDesignV1> UsoAsync(EscopoDoDesign escopo, CancellationToken ct);
}

/// <summary>As regras puras do design (US #669): validação, visibilidade, quem apaga e o teto — sem banco.</summary>
public static partial class RegrasDoDesign
{
    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,95}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdValido();

    /// <summary>A assinatura de um PNG: os 8 primeiros bytes.</summary>
    private static ReadOnlySpan<byte> AssinaturaDoPng => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>O identificador de tela do dev.kit: letras, números, hífen e sublinhado (o mesmo do <c>DesignStore</c>).</summary>
    public static bool IdDeTelaValido(string? tela) => tela is not null && IdValido().IsMatch(tela);

    /// <summary>
    /// O nível em que a versão fica: sem empresa, SEMPRE <c>usuario</c> (o pedido); com empresa, o pedido — e, sem
    /// pedido, <c>empresa</c>, que é o compartilhamento que motivou a US.
    /// </summary>
    public static string NivelEfetivo(EscopoDoDesign escopo, string? pedido)
        => RegrasDeCompartilhamento.NivelEfetivo(escopo, pedido);

    /// <summary>De quem é o teto: a empresa, ou o próprio usuário sem empresa.</summary>
    public static string EspacoDe(EscopoDoDesign escopo)
        => RegrasDeCompartilhamento.EspacoDe(escopo);

    /// <summary>
    /// A visibilidade, numa regra só (a do contexto): o nível usuário só para o dono; o resto só na mesma empresa — o
    /// empresa para todos, o time para quem o declara (<paramref name="time"/>; sem ele, qualquer time).
    /// </summary>
    public static bool Visivel(EscopoDoDesign escopo, string nivel, int? empresaDoProjeto, string timeDoProjeto, int donoDoProjeto, string? time)
    {
        if (nivel == NiveisDeConvencao.Usuario)
            return donoDoProjeto == escopo.UsuarioId;
        if (escopo.EmpresaId is null || empresaDoProjeto != escopo.EmpresaId)
            return false;
        if (nivel == NiveisDeConvencao.Empresa)
            return true;
        var doTime = RegrasDoConhecimentoDaEmpresa.NormalizarTime(time);
        return nivel == NiveisDeConvencao.Time && (doTime.Length == 0 || doTime == timeDoProjeto);
    }

    /// <summary>Quem apaga a versão: o autor dela, ou o gestor da empresa do projeto.</summary>
    public static bool PodeApagar(EscopoDoDesign escopo, int autor, int? empresaDoProjeto)
        => autor == escopo.UsuarioId || (escopo.EhGestor && empresaDoProjeto is not null && empresaDoProjeto == escopo.EmpresaId);

    /// <summary>A chave da imagem no armazém: o espaço, o projeto, a tela e o número — sem nada que venha cru do cliente.</summary>
    public static string ChaveDaImagem(string espaco, long projeto, string tela, int numero, string sha256)
        => $"{espaco.Replace(':', '-')}/{projeto}/{tela}/v{numero:D3}-{sha256[..12]}.png";

    /// <summary>O teto do plano: o do <paramref name="plano"/> em <see cref="OpcoesDoDesign.Tetos"/>, ou o padrão.</summary>
    public static TetoDoDesign TetoDo(OpcoesDoDesign opcoes, string? plano)
        => !string.IsNullOrWhiteSpace(plano) && opcoes.Tetos.TryGetValue(plano.Trim(), out var teto) ? teto : opcoes.TetoPadrao;

    /// <summary>A mensagem do teto, ou vazio quando a versão nova cabe.</summary>
    public static string ConferirTeto(TetoDoDesign teto, int versoes, long bytes, long novos, string plano)
    {
        if (versoes + 1 > teto.Versoes)
            return $"O teto do plano {plano} é de {teto.Versoes} versões de design: apague versões antigas ou fale com o gestor.";
        var tetoDeBytes = teto.MegaBytes * 1024L * 1024L;
        return bytes + novos > tetoDeBytes
            ? $"O teto do plano {plano} é de {teto.MegaBytes} MB de imagens de design: apague versões antigas ou fale com o gestor."
            : string.Empty;
    }

    /// <summary>As dimensões do PNG (o cabeçalho IHDR); nulo quando os bytes não são PNG.</summary>
    public static (int Largura, int Altura)? DimensoesDoPng(ReadOnlySpan<byte> png)
    {
        if (png.Length < 24 || !png[..8].SequenceEqual(AssinaturaDoPng) || !png.Slice(12, 4).SequenceEqual("IHDR"u8))
            return null;
        var largura = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        var altura = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        return largura > 0 && altura > 0 ? (largura, altura) : null;
    }

    /// <summary>O erro do envio, ou vazio quando ele vale (o PNG e os metadados; a base e o time dependem do banco).</summary>
    public static string Validar(EnvioDeVersaoDeDesign? envio, ReadOnlySpan<byte> png)
    {
        if (envio is null)
            return "Envie os metadados da versão.";
        var repositorio = envio.Repositorio?.Trim() ?? string.Empty;
        if (repositorio.Length == 0 || DesignV1.ChaveDoRepositorio(repositorio).Length == 0)
            return "Informe o repositório da tela (a URL do GitHub ou a pasta).";
        if (repositorio.Length > DesignV1.TamanhoMaximoDoRepositorio)
            return $"O repositório passa de {DesignV1.TamanhoMaximoDoRepositorio} caracteres.";
        if (!IdDeTelaValido(envio.Tela))
            return "O identificador da tela usa letras, números, hífen ou sublinhado (até 96).";
        var nivel = envio.Nivel?.Trim().ToLowerInvariant() ?? string.Empty;
        if (nivel.Length > 0 && nivel is not (NiveisDeConvencao.Empresa or NiveisDeConvencao.Time or NiveisDeConvencao.Usuario))
            return $"O nível é '{NiveisDeConvencao.Empresa}', '{NiveisDeConvencao.Time}' ou '{NiveisDeConvencao.Usuario}'.";
        if (RegrasDoConhecimentoDaEmpresa.NormalizarTime(envio.Time).Length > RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoTime)
            return $"O nome do time passa de {RegrasDoConhecimentoDaEmpresa.TamanhoMaximoDoTime} caracteres.";
        if (envio.Numero is <= 0)
            return "O número da versão é positivo.";
        if (!DesignV1.Tipos.Contains(envio.Tipo ?? string.Empty))
            return "O tipo da versão é 'captura' ou 'proposta'.";
        if (envio.Base is { } basis && (basis <= 0 || (envio.Numero is { } numero && basis >= numero)))
            return "A base é uma versão anterior da mesma tela.";
        if (!DesignV1.Temas.Contains(envio.Tema ?? "light"))
            return "O tema é 'light' ou 'dark'.";
        if (!DesignV1.Origens.Contains(envio.Origem ?? string.Empty))
            return "A origem é 'web', 'desktop' ou 'proposta'.";
        if (!DesignV1.Autores.Contains(envio.AutorTipo ?? DesignV1.AutorUsuario))
            return "O autor é 'usuario' ou 'agente'.";
        if ((envio.Nome?.Trim().Length ?? 0) > DesignV1.TamanhoMaximoDoNome
            || (envio.Estado?.Trim().Length ?? 0) > DesignV1.TamanhoMaximoDoEstado
            || (envio.Alvo?.Trim().Length ?? 0) > DesignV1.TamanhoMaximoDoAlvo
            || (envio.Branch?.Trim().Length ?? 0) > DesignV1.TamanhoMaximoDoBranch
            || (envio.Commit?.Trim().Length ?? 0) > DesignV1.TamanhoMaximoDoCommit
            || (envio.Descricao?.Trim().Length ?? 0) > DesignV1.TamanhoMaximoDaDescricao
            || (envio.Runner?.Trim().Length ?? 0) > DesignV1.TamanhoMaximoDoRunner)
            return "Um dos textos da versão passa do tamanho do contrato.";
        if (png.Length == 0)
            return "Envie o PNG da versão (o campo 'imagem').";
        if (png.Length > DesignV1.TamanhoMaximoDaImagem)
            return $"A imagem passa de {DesignV1.TamanhoMaximoDaImagem / (1024 * 1024)} MB.";
        return DimensoesDoPng(png) is null ? "A imagem não é um PNG válido." : string.Empty;
    }
}
