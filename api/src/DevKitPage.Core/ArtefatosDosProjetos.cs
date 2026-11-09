using DevKitPage.Contracts.V1;

namespace DevKitPage.Core;

/// <summary>
/// Um ARTEFATO do projeto compartilhado (controle-v4, item 2): o zip do pacote .NET guardado no armazém (<see cref="Chave"/>)
/// ou a URL do preview da página estática (<see cref="Url"/>). O zip vence na retenção (<see cref="RetidoAteUtc"/>) e sai do
/// armazém com a linha; o preview não tem prazo, só a contagem por projeto (<see cref="OpcoesDosArtefatos.MaximoPorProjeto"/>).
/// </summary>
public sealed class ArtefatoDoProjeto
{
    public long Id { get; set; }
    public long ProjetoId { get; set; }
    public ProjetoCompartilhado? Projeto { get; set; }

    /// <summary><see cref="ArtefatosV1.TipoPacote"/> ou <see cref="ArtefatosV1.TipoPreview"/>.</summary>
    public string Tipo { get; set; } = string.Empty;

    public string Repositorio { get; set; } = string.Empty;
    public string Sha { get; set; } = string.Empty;

    /// <summary>A chave do zip no armazém; vazia no preview.</summary>
    public string Chave { get; set; } = string.Empty;

    /// <summary>O nome do zip no download (o <c>Content-Disposition</c>).</summary>
    public string NomeDoArquivo { get; set; } = string.Empty;

    /// <summary>A URL do preview; vazia no pacote.</summary>
    public string Url { get; set; } = string.Empty;

    public string Ambiente { get; set; } = string.Empty;

    /// <summary>O SHA-256 do zip, calculado no recebimento.</summary>
    public string Hash { get; set; } = string.Empty;

    public long Tamanho { get; set; }

    /// <summary>Quem registrou; sem FK, como o autor do projeto (o usuário apagado não leva o artefato).</summary>
    public int AutorId { get; set; }

    public DateTime CriadoEmUtc { get; set; }

    /// <summary>Até quando o zip fica guardado; nulo no preview.</summary>
    public DateTime? RetidoAteUtc { get; set; }
}

/// <summary>
/// Os artefatos dos projetos no site: o pacote recebido no armazém com o link assinado, o preview guardado, e os últimos de
/// cada projeto para o Board. A visibilidade é a do projeto (<c>GET /api/projetos/{id}</c>): quem não vê recebe 404; gravar
/// exige editor ou dono, como alterar o projeto.
/// </summary>
public interface IArtefatosDosProjetos
{
    Task<ResultadoDaGestao<ArtefatoV1>> RegistrarPacoteAsync(EscopoDoDesign escopo, long projeto, PacoteRecebido pacote, CancellationToken ct);
    Task<ResultadoDaGestao<ArtefatoV1>> RegistrarPreviewAsync(EscopoDoDesign escopo, long projeto, PreviewDoProjetoV1 preview, CancellationToken ct);
    Task<ResultadoDaGestao<ArtefatoV1[]>> ListarAsync(EscopoDoDesign escopo, long projeto, CancellationToken ct);
    Task<IReadOnlyList<ArtefatosDoProjetoV1>> RecentesAsync(EscopoDoDesign escopo, CancellationToken ct);
}

/// <summary>O zip recebido pelo POST, já num arquivo temporário (quem chama o apaga): o hash é calculado aqui, não confiado.</summary>
public sealed record PacoteRecebido(string? Repositorio, string? Sha, string? HashInformado, string NomeDoArquivo, string Arquivo, long Tamanho);

/// <summary>
/// Onde os zips dos artefatos ficam: o Blob Storage em produção, uma pasta em desenvolvimento e nos testes. O link é de
/// LEITURA e vence no prazo; nele não vai a chave da conta.
/// </summary>
public interface IArmazemDeArtefatos
{
    Task GravarAsync(string chave, Stream conteudo, CancellationToken ct);
    Task ApagarAsync(string chave, CancellationToken ct);

    /// <summary>O link de download: absoluto (a SAS do Blob) ou relativo à API (<c>/api/artefatos/download/...</c>, no disco).</summary>
    string Link(string chave, string nomeDoArquivo, DateTimeOffset expira);
}

/// <summary>
/// Os artefatos do projeto (seção <c>Artefatos</c>): o armazém, o teto do zip, a retenção e o prazo do link. Sem nada
/// configurado, o armazém e a conexão são os do design (<see cref="OpcoesDoDesign"/>), num contêiner próprio.
/// </summary>
public sealed class OpcoesDosArtefatos
{
    public const string Secao = "Artefatos";

    /// <summary><c>disco</c> ou <c>blob</c>; vazio segue o <see cref="OpcoesDoDesign.Armazem"/>.</summary>
    public string Armazem { get; set; } = string.Empty;

    /// <summary>A pasta dos zips no armazém em disco.</summary>
    public string Pasta { get; set; } = "dados/artefatos";

    /// <summary>A connection string do Storage (segredo); vazia usa a do design (<c>Design__ConexaoDoBlob</c>).</summary>
    public string ConexaoDoBlob { get; set; } = string.Empty;

    /// <summary>O contêiner dos zips, privado (sem acesso anônimo).</summary>
    public string Conteiner { get; set; } = "artefatos";

    /// <summary>O teto do zip, em MB; acima dele, 413.</summary>
    public int TamanhoMaximoMb { get; set; } = 100;

    /// <summary>Quantos dias o zip fica guardado; depois sai do armazém com a linha.</summary>
    public int RetencaoDias { get; set; } = 7;

    /// <summary>O prazo do link de download, em horas (nunca além da retenção).</summary>
    public int LinkHoras { get; set; } = 24;

    /// <summary>Quantos artefatos cada projeto guarda; os mais antigos saem.</summary>
    public int MaximoPorProjeto { get; set; } = 10;

    public long TetoEmBytes => Math.Max(1, TamanhoMaximoMb) * 1024L * 1024L;
}

/// <summary>As regras dos artefatos, sem banco: o que o POST aceita e o que o comando de ação leva.</summary>
public static class RegrasDosArtefatos
{
    /// <summary>O problema do pacote (sem o zip), ou vazio quando ele vale.</summary>
    public static string ValidarPacote(string? repositorio, string? sha, string? hash)
    {
        var problema = ValidarOrigem(repositorio, sha);
        if (problema.Length > 0)
            return problema;
        return hash is { Length: > 0 } && !Hexadecimal(hash, 64, 64)
            ? "O hash do zip é o SHA-256 em 64 caracteres hexadecimais."
            : string.Empty;
    }

    /// <summary>O problema do preview, ou vazio quando ele vale: HTTPS, sem credencial nem consulta na URL, e nunca produção.</summary>
    public static string ValidarPreview(PreviewDoProjetoV1? preview)
    {
        if (preview is null)
            return "Envie o preview.";
        var problema = ValidarOrigem(preview.Repositorio, preview.Sha);
        if (problema.Length > 0)
            return problema;
        var ambiente = preview.Ambiente?.Trim() ?? string.Empty;
        if (ambiente.Length == 0 || ambiente.Length > ArtefatosV1.TamanhoMaximoDoAmbiente || !ambiente.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            return $"Informe o ambiente de preview (letras, números e -, até {ArtefatosV1.TamanhoMaximoDoAmbiente} caracteres).";
        if (string.Equals(ambiente, ArtefatosV1.AmbienteDeProducao, StringComparison.OrdinalIgnoreCase))
            return "O artefato nunca publica em produção: use o ambiente de preview do projeto.";
        var url = preview.Url?.Trim() ?? string.Empty;
        if (url.Length is 0 or > ArtefatosV1.TamanhoMaximoDaUrl || !Uri.TryCreate(url, UriKind.Absolute, out var endereco)
            || endereco.Scheme != Uri.UriSchemeHttps)
            return $"A URL do preview é HTTPS (até {ArtefatosV1.TamanhoMaximoDaUrl} caracteres).";
        if (endereco.UserInfo.Length > 0 || endereco.Query.Length > 0 || endereco.Fragment.Length > 0)
            return "A URL do preview vai sem usuário, consulta nem fragmento: o link não leva segredo.";
        return string.Empty;
    }

    /// <summary>O problema da ação no projeto pelo site, ou vazio quando ela vale.</summary>
    public static string ValidarAcao(AcaoNoProjetoRemotoV1? acao)
    {
        if (acao is null)
            return "Envie a ação no projeto.";
        if (acao.Acao != ArtefatosV1.AcaoArtefato)
            return $"A ação no projeto é {ArtefatosV1.AcaoArtefato}.";
        if (string.IsNullOrWhiteSpace(acao.Projeto) || acao.Projeto.Length > ProjetosV1.TamanhoMaximoDoSlug)
            return "Informe o projeto (o slug).";
        if (acao.Repositorio is { Length: > ArtefatosV1.TamanhoMaximoDoRepositorio })
            return $"O repositório tem até {ArtefatosV1.TamanhoMaximoDoRepositorio} caracteres.";
        return acao.Sha is { Length: > 0 } sha && !Hexadecimal(sha, 7, 40) ? "O SHA tem de 7 a 40 caracteres hexadecimais." : string.Empty;
    }

    /// <summary>O nome do zip no download: o do envio, sem caminho nem caractere de cabeçalho; sem ele, <c>artefato.zip</c>.</summary>
    public static string NomeDoArquivo(string? nome)
    {
        var limpo = new string(Path.GetFileName(nome ?? string.Empty)
            .Where(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_').ToArray());
        if (limpo.Length > 120)
            limpo = limpo[^120..];
        return limpo.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && limpo.Length > 4 ? limpo : "artefato.zip";
    }

    private static string ValidarOrigem(string? repositorio, string? sha)
    {
        if (string.IsNullOrWhiteSpace(repositorio) || repositorio.Length > ArtefatosV1.TamanhoMaximoDoRepositorio)
            return $"Informe o repositório do artefato (até {ArtefatosV1.TamanhoMaximoDoRepositorio} caracteres).";
        return string.IsNullOrWhiteSpace(sha) || !Hexadecimal(sha, 7, 40) ? "Informe o SHA do artefato (de 7 a 40 caracteres hexadecimais)." : string.Empty;
    }

    private static bool Hexadecimal(string texto, int minimo, int maximo)
        => texto.Length >= minimo && texto.Length <= maximo && texto.All(char.IsAsciiHexDigit);
}
