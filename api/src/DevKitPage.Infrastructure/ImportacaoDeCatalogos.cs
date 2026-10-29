using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;
using Microsoft.EntityFrameworkCore;

namespace DevKitPage.Infrastructure;

/// <summary>Leitura limitada de arquivos selecionados ou de arquivo público do GitHub; nunca clona nem executa código.</summary>
public sealed class ImportacaoDeCatalogos(DevKitPageDb db, IGestaoDeCatalogos gestao,
    IPublicacaoDeCatalogos publicacao, IHttpClientFactory clientes, TimeProvider relogio) : IImportacaoDeCatalogos
{
    public const string ClienteGitHub = "catalogos-publicos-github";
    public const int MaximoBytes = 16 * 1024 * 1024;
    private const int MaximoArquivos = 2000;

    public async Task<ResultadoDaGestao<CatalogoImportadoV1>> ImportarAsync(ImportarCatalogoV1 pedido, string autor, CancellationToken ct)
    {
        if (RegrasDosCatalogos.Validar(pedido.Catalogo).Count > 0)
            return Falha("Confira o slug, o nome e a descrição do catálogo.");
        if (await db.Catalogos.AnyAsync(c => c.Slug == pedido.Catalogo.Slug.Trim(), ct))
            return ResultadoDaGestao<CatalogoImportadoV1>.Falhou(FalhaNaGestao.Conflito, "Já existe um catálogo com este slug.");

        try
        {
            var github = !string.IsNullOrWhiteSpace(pedido.Repositorio);
            if (github && pedido.Arquivos is not null)
                return Falha("Escolha uma única origem: pasta local ou GitHub.");
            var arquivos = github ? await DoGitHubAsync(pedido, ct) : Locais(pedido.Arquivos);
            if (!arquivos.TryGetValue("manifesto.json", out var texto))
                return Falha("Escolha a pasta que contém manifesto.json e as pastas skills, plans e fluxos (ou base).");
            var manifesto = ManifestoJson.Ler(texto);
            if (manifesto.Versao < 1 || manifesto.MinDevKit < 1 || manifesto.Itens.Count is 0 or > MaximoArquivos)
                return Falha("O manifesto deve ter versão e dev.kit mínimo positivos e de 1 a 2000 itens.");
            var chaves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var caminhos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in manifesto.Itens)
            {
                if (item is null || !TiposDeItem.Todos.Contains(item.Tipo) || string.IsNullOrWhiteSpace(item.Nome)
                    || item.MinDevKit < 1 || item.Tecnologia is null
                    || !CaminhoSeguro(item.Arquivo) || !item.Arquivo.StartsWith(FormatosDoCatalogo.Pasta(item.Tipo) + "/", StringComparison.Ordinal)
                    || !chaves.Add($"{item.Tipo}:{item.Nome}") || !caminhos.Add(item.Arquivo))
                    return Falha("O manifesto contém tipo, nome ou caminho inválido, ou itens repetidos.");
                // O repositório do catálogo básico guarda os itens sob base/; o pacote usa as pastas diretamente.
                if (!arquivos.ContainsKey(item.Arquivo) && arquivos.TryGetValue("base/" + item.Arquivo, out var conteudo))
                    arquivos.Add(item.Arquivo, conteudo);
            }
            var (itens, problema) = GestaoDeCatalogos.ItensDosArquivos(arquivos, manifesto);
            if (problema.Length > 0)
                return Falha(problema);
            foreach (var item in itens.Where(i => i.Tipo != TiposDeItem.Fluxo))
            {
                var (conferido, _) = RegrasDosCatalogos.Conferir(new ItemEditadoV1(item.Tipo, item.Conteudo));
                if (conferido!.Nome != item.Nome)
                    return Falha("O nome do item difere do nome no manifesto.");
            }
            var basico = await publicacao.UltimaAsync(Catalogos.Basico, new AcessoAoCatalogo(true, null), ct);
            if (basico is not null && itens.Any(i => basico.Manifesto.Itens.Any(b => b.Tipo == i.Tipo && b.Nome == i.Nome)))
                return ResultadoDaGestao<CatalogoImportadoV1>.Falhou(FalhaNaGestao.Conflito, "Há itens com nomes já existentes no catálogo básico. Renomeie-os antes de importar.");

            // Tudo é validado antes da transação; catálogo e rascunhos entram juntos, sem importação parcial.
            await using var transacao = await db.Database.BeginTransactionAsync(ct);
            var criado = await gestao.CriarAsync(pedido.Catalogo, ct);
            if (criado.Valor is null)
                return ResultadoDaGestao<CatalogoImportadoV1>.Falhou(criado.Falha, criado.Mensagem);
            var catalogo = await db.Catalogos.SingleAsync(c => c.Id == criado.Valor.Id, ct);
            catalogo.Origem = github ? $"GitHub · {Repositorio(pedido.Repositorio!)}@{Referencia(pedido.Referencia)}{(string.IsNullOrWhiteSpace(pedido.Pasta) ? "" : ":" + pedido.Pasta)}" : "Pasta local";
            catalogo.MinDevKitDaOrigem = Math.Max(manifesto.MinDevKit, manifesto.Itens.Max(i => i.MinDevKit));
            foreach (var item in itens)
            {
                item.CatalogoId = catalogo.Id;
                item.AlteradoEmUtc = relogio.GetUtcNow().UtcDateTime;
                item.AlteradoPor = autor.Length <= 100 ? autor : autor[..100];
            }
            db.ItensEditaveis.AddRange(itens);
            await db.SaveChangesAsync(ct);
            await transacao.CommitAsync(ct);
            return new(new CatalogoImportadoV1(criado.Valor with { Itens = itens.Count, Pendentes = itens.Count, Origem = catalogo.Origem },
                itens.Count(i => i.Tipo == TiposDeItem.Skill), itens.Count(i => i.Tipo == TiposDeItem.Plan), itens.Count(i => i.Tipo == TiposDeItem.Fluxo)));
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or DecoderFallbackException)
        {
            return Falha(ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return Falha("O repositório público ou a referência do GitHub está indisponível. Confira os dados e tente novamente.");
        }
        catch (DbUpdateException)
        {
            return ResultadoDaGestao<CatalogoImportadoV1>.Falhou(FalhaNaGestao.Conflito, "O catálogo acabou de ser gravado. Recarregue a lista.");
        }
    }

    private static Dictionary<string, string> Locais(IReadOnlyList<ArquivoDeCatalogoV1>? arquivos)
    {
        if (arquivos is null || arquivos.Count is 0 or > MaximoArquivos)
            throw new FormatException("Selecione de 1 a 2000 arquivos do catálogo.");
        var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long tamanho = 0;
        foreach (var arquivo in arquivos)
        {
            if (arquivo is null || !CaminhoSeguro(arquivo.Caminho) || arquivo.Conteudo is null || !resultado.TryAdd(arquivo.Caminho, arquivo.Conteudo))
                throw new FormatException("A pasta contém caminhos inválidos ou repetidos.");
            var bytes = Encoding.UTF8.GetByteCount(arquivo.Conteudo);
            tamanho += bytes;
            if (bytes > 1024 * 1024 || tamanho > MaximoBytes)
                throw new FormatException("O catálogo excede o limite de 16 MB ou um arquivo excede 1 MB.");
        }
        return resultado;
    }

    private async Task<Dictionary<string, string>> DoGitHubAsync(ImportarCatalogoV1 pedido, CancellationToken ct)
    {
        var repo = Repositorio(pedido.Repositorio!);
        var referencia = Referencia(pedido.Referencia);
        var pasta = (pedido.Pasta ?? "").Trim().Trim('/');
        if (pasta.Length > 0 && !CaminhoSeguro(pasta))
            throw new FormatException("A pasta do repositório é inválida.");
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        prazo.CancelAfter(TimeSpan.FromSeconds(45));
        ct = prazo.Token;
        // Host fixo, sem credencial e sem redirecionamentos: entrada alguma escolhe o servidor de destino.
        using var cliente = clientes.CreateClient(ClienteGitHub);
        using var resposta = await cliente.GetAsync(
            $"https://codeload.github.com/{repo}/zip/{Uri.EscapeDataString(referencia)}", HttpCompletionOption.ResponseHeadersRead, ct);
        resposta.EnsureSuccessStatusCode();
        await using var entrada = await resposta.Content.ReadAsStreamAsync(ct);
        using var memoria = new MemoryStream(await LerLimitadoAsync(entrada, MaximoBytes, ct));
        using var zip = new ZipArchive(memoria, ZipArchiveMode.Read);
        if (zip.Entries.Count > 10000 || zip.Entries.Sum(e => e.Length) > 32L * 1024 * 1024)
            throw new FormatException("O repositório excede o limite de arquivos ou 32 MB descompactados.");
        var arquivos = new List<ArquivoDeCatalogoV1>();
        long totalLido = 0;
        foreach (var item in zip.Entries)
        {
            if (item.FullName.EndsWith('/')) continue;
            if (!CaminhoSeguro(item.FullName) || (item.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new FormatException("O repositório contém caminho inválido ou link simbólico.");
            var relativo = item.FullName[(item.FullName.IndexOf('/') + 1)..];
            var prefixo = pasta.Length == 0 ? "" : pasta + "/";
            if (!relativo.StartsWith(prefixo, StringComparison.Ordinal) || !relativo.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (item.Length > 1024 * 1024)
                throw new FormatException("Um arquivo do catálogo excede 1 MB.");
            await using var conteudo = item.Open();
            var bytes = await LerLimitadoAsync(conteudo, 1024 * 1024, ct);
            totalLido += bytes.Length;
            if (totalLido > MaximoBytes || arquivos.Count >= MaximoArquivos)
                throw new FormatException("O catálogo excede 16 MB ou 2000 arquivos.");
            arquivos.Add(new ArquivoDeCatalogoV1(relativo[prefixo.Length..], new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF')));
        }
        return Locais(arquivos);
    }

    private static async Task<byte[]> LerLimitadoAsync(Stream entrada, int teto, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        var buffer = new byte[8192];
        int lidos;
        while ((lidos = await entrada.ReadAsync(buffer, ct)) > 0)
        {
            if (memoria.Length + lidos > teto)
                throw new FormatException("O download ou arquivo do catálogo excede o limite de tamanho.");
            await memoria.WriteAsync(buffer.AsMemory(0, lidos), ct);
        }
        return memoria.ToArray();
    }

    private static bool CaminhoSeguro(string? caminho) => !string.IsNullOrEmpty(caminho) && caminho.Length <= 300
        && !caminho.Contains('\\') && !caminho.Contains(':') && !caminho.Any(char.IsControl)
        && caminho.Split('/').All(p => p.Length > 0 && p is not "." and not "..");

    private static string Repositorio(string texto)
    {
        var repo = texto.Trim();
        if (repo.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) repo = repo[19..].TrimEnd('/');
        else if (repo.StartsWith("github.com/", StringComparison.OrdinalIgnoreCase)) repo = repo[11..].TrimEnd('/');
        if (!Regex.IsMatch(repo, @"\A[A-Za-z0-9][A-Za-z0-9-]{0,99}/[A-Za-z0-9][A-Za-z0-9._-]{0,99}\z"))
            throw new FormatException("Informe um repositório público no formato org/repositorio ou https://github.com/org/repositorio.");
        return repo;
    }

    private static string Referencia(string? texto)
    {
        var referencia = string.IsNullOrWhiteSpace(texto) ? "main" : texto.Trim();
        if (!Regex.IsMatch(referencia, @"\A[A-Za-z0-9][A-Za-z0-9._/-]{0,199}\z") || referencia.Contains("..", StringComparison.Ordinal)
            || referencia.Contains("//", StringComparison.Ordinal) || referencia.EndsWith('/'))
            throw new FormatException("A branch ou tag é inválida.");
        return referencia;
    }

    private static ResultadoDaGestao<CatalogoImportadoV1> Falha(string mensagem)
        => ResultadoDaGestao<CatalogoImportadoV1>.Falhou(FalhaNaGestao.Recusada, mensagem);
}
