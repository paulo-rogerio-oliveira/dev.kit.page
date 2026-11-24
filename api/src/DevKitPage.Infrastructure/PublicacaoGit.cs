using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>Publica os bytes do Git, sem importar nem publicar o rascunho do painel.</summary>
public sealed class PublicacaoGit(DevKitPageDb db, IPublicacaoDeCatalogos publicacao, IArmazemDePacotes armazem,
    TimeProvider relogio) : IPublicacaoGit
{
    public const int MaximoBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ReciboPublicacaoGitV1?> ReciboAsync(string sha, CancellationToken ct)
    {
        var documento = await db.Set<ReciboDoCatalogoGit>().AsNoTracking().Where(r => r.Sha == sha).Select(r => r.Documento).SingleOrDefaultAsync(ct);
        return documento is null ? null : JsonSerializer.Deserialize<ReciboPublicacaoGitV1>(documento, Json);
    }

    public async Task<ResultadoDaGestao<ConferenciaGitV1>> ExecutarAsync(PublicacaoGitV1 pedido, bool aplicar, string autor, CancellationToken ct)
    {
        ManifestoDoCatalogoV1 manifesto;
        try { (manifesto, _) = ConferirPacote(pedido); }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException or DecoderFallbackException or ArgumentException or InvalidOperationException)
        { return Falha(ex.Message); }

        // Serializable tambem coordena publicadores de outras instancias. O indice unico decide a corrida com o painel.
        await using var transacao = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (await ReciboAsync(pedido.Sha, ct) is { } anterior)
        {
            if (anterior.Sha256 != pedido.Sha256 || anterior.Versao != pedido.Versao || anterior.Motor != pedido.Motor || anterior.Referencia != pedido.Referencia)
                return Falha("O SHA ja foi publicado com outro pacote ou origem.", FalhaNaGestao.Conflito);
            return new(new ConferenciaGitV1(!aplicar, true, anterior));
        }
        var catalogo = await db.Catalogos.AsNoTracking().SingleAsync(c => c.Slug == Catalogos.Basico, ct);
        var ultima = await publicacao.UltimaAsync(Catalogos.Basico, new AcessoAoCatalogo(true, null), ct);
        // Nao interpretar indisponibilidade do fallback como catalogo vazio: a numeracao historica e obrigatoria.
        if (ultima is null)
            return Falha("A release anterior do basico esta indisponivel; nao e seguro iniciar outra sequencia.", FalhaNaGestao.Conflito);
        // O manifesto do Git e a fonte e numera os itens na sequencia do repositorio: nao precisa ser a evolucao de um passo
        // da release anterior. So nao substitui (versao maior, com salto) nem retrocede item ou retirado.
        if (RegrasDoCatalogoBase.ProblemaDaSequencia(ultima.Versao, ultima.Manifesto, manifesto) is { } problema)
            return Falha(problema, FalhaNaGestao.Conflito);

        var agora = relogio.GetUtcNow();
        var raiz = $"/api/catalogos/basico/releases/{pedido.Versao}";
        var recibo = new ReciboPublicacaoGitV1(pedido.Sha, pedido.Referencia, pedido.Motor, pedido.Sha256, pedido.Versao, agora, raiz + "/manifesto", raiz + "/download");
        if (!aplicar) return new(new ConferenciaGitV1(true, false, recibo));
        var versao = new VersaoDoCatalogo
        {
            CatalogoId = catalogo.Id, Versao = pedido.Versao, Tag = Catalogos.Tag(Catalogos.Basico, pedido.Versao),
            Manifesto = ManifestoJson.Serializar(manifesto), Sha256 = pedido.Sha256, TamanhoBytes = pedido.Pacote.LongLength,
            Itens = manifesto.Itens.Count, Retirados = manifesto.Retirados.Count, PublicadaEmUtc = agora.UtcDateTime,
            PublicadaPor = autor.Length <= 100 ? autor : autor[..100],
        };
        try
        {
            db.VersoesDoCatalogo.Add(versao);
            await db.SaveChangesAsync(ct);
            await armazem.GuardarAsync(versao.Id, pedido.Pacote, ct);
            var gravado = await armazem.AbrirAsync(versao.Id, ct);
            if (gravado is null || Convert.ToHexStringLower(SHA256.HashData(gravado)) != pedido.Sha256)
                throw new InvalidDataException("O armazenamento nao devolveu o pacote conferido; a transacao sera revertida.");
            db.Set<ReciboDoCatalogoGit>().Add(new() { Sha = pedido.Sha, VersaoId = versao.Id, Documento = JsonSerializer.Serialize(recibo, Json) });
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return new(new ConferenciaGitV1(false, false, recibo));
        }
        catch (DbUpdateException)
        {
            await transacao.RollbackAsync(ct);
            return Falha("Publicacao concorrente: consulte o recibo e repita o mesmo pacote.", FalhaNaGestao.Conflito);
        }
    }

    private static (ManifestoDoCatalogoV1 Manifesto, List<ItemDoCatalogoEditavel> Itens) ConferirPacote(PublicacaoGitV1 p)
    {
        if (!Hex(p.Sha, 40) || !Hex(p.Motor, 40) || !Hex(p.Sha256, 64) || p.Versao < 1 || p.Referencia != $"refs/tags/catalogo-v{p.Versao}")
            throw new FormatException("Informe SHA/motor completos, hash SHA-256 e ref catalogo-vN coerente.");
        if (p.Pacote is null || p.Pacote.Length is 0 or > MaximoBytes || Convert.ToHexStringLower(SHA256.HashData(p.Pacote)) != p.Sha256)
            throw new FormatException("O tamanho ou SHA-256 do pacote e invalido.");
        using var zip = new ZipArchive(new MemoryStream(p.Pacote), ZipArchiveMode.Read);
        if (zip.Entries.Count is 0 or > 2001 || zip.Entries.Sum(e => e.Length) > MaximoBytes)
            throw new FormatException("O pacote excede os limites descompactados.");
        var arquivos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            if (e.Length > 1024 * 1024 || !Regex.IsMatch(e.FullName, @"\A(?:manifesto\.json|(?:skills|plans|fluxos|gatilhos)/[a-zA-Z0-9_.-]+\.json)\z")
                || (e.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new FormatException("O pacote contem caminho, link ou tamanho de arquivo invalido.");
            using var leitor = new StreamReader(e.Open(), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            if (!arquivos.TryAdd(e.FullName, leitor.ReadToEnd())) throw new FormatException("Arquivo repetido no pacote.");
        }
        if (!arquivos.TryGetValue("manifesto.json", out var texto)) throw new FormatException("Manifesto ausente.");
        var m = ManifestoJson.Ler(texto);
        if (m.Versao != p.Versao || m.MinDevKit < 1 || m.Itens is null || m.Retirados is null || m.Itens.Count is 0 or > 2000
            || m.Itens.Any(i => i is null || !RegrasDoCatalogoBase.TipoDoManifesto(i.Tipo) || string.IsNullOrWhiteSpace(i.Nome) || i.Tecnologia is null
                || i.Requer is null || i.Dependencias is null || i.Hashes is null || !Hex(i.Hash, 64) || i.MinDevKit < 1 || i.MinDevKit > m.MinDevKit
                || i.Arquivo is null || !i.Arquivo.StartsWith(FormatosDoCatalogo.Pasta(i.Tipo) + "/", StringComparison.Ordinal))
            || m.Itens.Select(i => i.Tipo + ":" + i.Nome).Distinct().Count() != m.Itens.Count
            || m.Itens.Select(i => i.Arquivo).Distinct(StringComparer.OrdinalIgnoreCase).Count() != m.Itens.Count
            || arquivos.Count != m.Itens.Count + 1 || m.Retirados.Any(r => r is null))
            throw new FormatException("Manifesto invalido, arquivos extras ou MinDevKit incoerente.");
        var (itens, problema) = GestaoDeCatalogos.ItensDosArquivos(arquivos, m with { Itens = m.Itens.Where(i => i.Tipo != GatilhoDoCatalogo.Tipo).ToArray() });
        if (problema.Length > 0) throw new FormatException(problema);
        foreach (var i in m.Itens.Where(i => i.Tipo == GatilhoDoCatalogo.Tipo))
        {
            if (!arquivos.TryGetValue(i.Arquivo, out var textoGatilho)) throw new FormatException("Gatilho do manifesto ausente no pacote.");
            var lido = GatilhoDoCatalogo.Ler(i.Arquivo, textoGatilho);
            if (lido.Nome != i.Nome || lido.Hash != i.Hash) throw new FormatException("Hash ou identidade do gatilho diverge do motor.");
            itens.Add(new() { Tipo = i.Tipo, Nome = i.Nome, Arquivo = i.Arquivo, Conteudo = arquivos[i.Arquivo], Hash = i.Hash, Categoria = i.Tecnologia });
        }
        var validos = new List<ItemParaPublicar>();
        foreach (var i in itens)
        {
            if (i.Tipo == GatilhoDoCatalogo.Tipo) { validos.Add(new(i.Tipo, i.Nome, [], [])); continue; }
            var (c, erro) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(i.Tipo, i.Conteudo, i.Categoria));
            if (c is null || (i.Tipo != TiposDeItem.Fluxo && c.Nome != i.Nome) || RegrasDosCatalogos.RuleDeImpacto(i.Tipo, i.Conteudo))
                throw new FormatException("Item invalido no basico: " + erro);
            validos.Add(new(i.Tipo, i.Nome, c.Dependencias, c.Embutidos));
        }
        var problemas = RegrasDosCatalogos.Dependencias(validos, new HashSet<string>(StringComparer.Ordinal), new Dictionary<string, string>());
        if (problemas.Count > 0) throw new FormatException(string.Join("; ", problemas));
        return (m, itens);
    }

    private static bool Hex(string? valor, int tamanho) => valor is not null && Regex.IsMatch(valor, $"\\A[a-f0-9]{{{tamanho}}}\\z");
    private static ResultadoDaGestao<ConferenciaGitV1> Falha(string mensagem, FalhaNaGestao falha = FalhaNaGestao.Recusada)
        => ResultadoDaGestao<ConferenciaGitV1>.Falhou(falha, mensagem);
}
