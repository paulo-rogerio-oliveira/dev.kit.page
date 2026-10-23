using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DevKitPage.Core;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// As imagens do design no AZURE BLOB STORAGE (US #669), o armazém de produção: um blob por versão, no contêiner de
/// <see cref="OpcoesDoDesign.Conteiner"/>, criado na primeira gravação. A conexão é segredo (Key Vault).
/// </summary>
public sealed class ArmazemDeImagensNoBlob(IOptions<OpcoesDoDesign> opcoes) : IArmazemDeImagens
{
    private BlobContainerClient? _conteiner;

    public async Task GravarAsync(string chave, byte[] conteudo, CancellationToken ct)
    {
        var conteiner = await ConteinerAsync(ct);
        await conteiner.GetBlobClient(chave).UploadAsync(new BinaryData(conteudo),
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "image/png" } }, ct);
    }

    public async Task<byte[]?> LerAsync(string chave, CancellationToken ct)
    {
        try
        {
            var conteudo = await (await ConteinerAsync(ct)).GetBlobClient(chave).DownloadContentAsync(ct);
            return conteudo.Value.Content.ToArray();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    public async Task ApagarAsync(string chave, CancellationToken ct)
        => await (await ConteinerAsync(ct)).GetBlobClient(chave).DeleteIfExistsAsync(cancellationToken: ct);

    private async Task<BlobContainerClient> ConteinerAsync(CancellationToken ct)
    {
        if (_conteiner is not null)
            return _conteiner;
        var cfg = opcoes.Value;
        if (string.IsNullOrWhiteSpace(cfg.ConexaoDoBlob))
            throw new InvalidOperationException("Design:ConexaoDoBlob não configurada: com Design:Armazem=blob ela vem do Key Vault ou da variável Design__ConexaoDoBlob.");
        var conteiner = new BlobContainerClient(cfg.ConexaoDoBlob, cfg.Conteiner);
        await conteiner.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        return _conteiner = conteiner;
    }
}

/// <summary>
/// As imagens do design numa PASTA (US #669): o armazém de desenvolvimento e o dos testes. A chave vira caminho dentro
/// da pasta, e nunca sai dela.
/// </summary>
public sealed class ArmazemDeImagensEmDisco(IOptions<OpcoesDoDesign> opcoes) : IArmazemDeImagens
{
    public async Task GravarAsync(string chave, byte[] conteudo, CancellationToken ct)
    {
        var arquivo = Caminho(chave);
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        await File.WriteAllBytesAsync(arquivo, conteudo, ct);
    }

    public async Task<byte[]?> LerAsync(string chave, CancellationToken ct)
    {
        var arquivo = Caminho(chave);
        return File.Exists(arquivo) ? await File.ReadAllBytesAsync(arquivo, ct) : null;
    }

    public Task ApagarAsync(string chave, CancellationToken ct)
    {
        var arquivo = Caminho(chave);
        if (File.Exists(arquivo))
            File.Delete(arquivo);
        return Task.CompletedTask;
    }

    private string Caminho(string chave)
    {
        var raiz = Path.GetFullPath(opcoes.Value.Pasta);
        var arquivo = Path.GetFullPath(Path.Combine(raiz, chave));
        if (!arquivo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A chave da imagem sai da pasta do design.");
        return arquivo;
    }
}
