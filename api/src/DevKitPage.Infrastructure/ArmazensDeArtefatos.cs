using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using DevKitPage.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// Os zips dos artefatos no AZURE BLOB STORAGE (controle-v4, item 2): um blob por artefato, num contêiner PRIVADO
/// (<see cref="OpcoesDosArtefatos.Conteiner"/>). O link é uma SAS de serviço do blob: só leitura, só HTTPS, com prazo e o nome
/// do download — a assinatura vai no link, a chave da conta não.
/// </summary>
public sealed class ArmazemDeArtefatosNoBlob(IOptions<OpcoesDosArtefatos> opcoes) : IArmazemDeArtefatos
{
    private BlobContainerClient? _conteiner;

    public async Task GravarAsync(string chave, Stream conteudo, CancellationToken ct)
    {
        var conteiner = Conteiner();
        await conteiner.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        await conteiner.GetBlobClient(chave).UploadAsync(conteudo,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = "application/zip" } }, ct);
    }

    public async Task ApagarAsync(string chave, CancellationToken ct)
        => await Conteiner().GetBlobClient(chave).DeleteIfExistsAsync(cancellationToken: ct);

    public string Link(string chave, string nomeDoArquivo, DateTimeOffset expira)
    {
        var blob = Conteiner().GetBlobClient(chave);
        if (!blob.CanGenerateSasUri)
            throw new InvalidOperationException("Artefatos:ConexaoDoBlob (ou Design:ConexaoDoBlob) precisa da chave da conta para assinar o link de download.");
        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expira)
        {
            BlobContainerName = blob.BlobContainerName,
            BlobName = blob.Name,
            Resource = "b",
            Protocol = SasProtocol.Https,
            ContentDisposition = $"attachment; filename=\"{nomeDoArquivo}\"",
        };
        return blob.GenerateSasUri(sas).ToString();
    }

    private BlobContainerClient Conteiner()
    {
        if (_conteiner is not null)
            return _conteiner;
        var cfg = opcoes.Value;
        if (string.IsNullOrWhiteSpace(cfg.ConexaoDoBlob))
            throw new InvalidOperationException("Artefatos:ConexaoDoBlob não configurada: com o armazém blob ela vem do Key Vault (a do design serve).");
        return _conteiner = new BlobContainerClient(cfg.ConexaoDoBlob, cfg.Conteiner);
    }
}

/// <summary>
/// Os zips dos artefatos numa PASTA: o armazém de desenvolvimento e dos testes. O link é a rota anônima
/// <c>/api/artefatos/download/{token}</c>, com o token protegido pelo DataProtection da API e com prazo: ele leva a chave e o
/// nome cifrados, nada que abra outro arquivo.
/// </summary>
public sealed class ArmazemDeArtefatosEmDisco(IOptions<OpcoesDosArtefatos> opcoes, IDataProtectionProvider protecao, TimeProvider relogio)
    : IArmazemDeArtefatos
{
    public const string RotaDoDownload = "/api/artefatos/download/";
    private ITimeLimitedDataProtector Protetor => protecao.CreateProtector("DevKitPage.Artefatos.Download").ToTimeLimitedDataProtector();

    public async Task GravarAsync(string chave, Stream conteudo, CancellationToken ct)
    {
        var arquivo = Caminho(chave);
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        await using var destino = File.Create(arquivo);
        await conteudo.CopyToAsync(destino, ct);
    }

    public Task ApagarAsync(string chave, CancellationToken ct)
    {
        var arquivo = Caminho(chave);
        if (File.Exists(arquivo))
            File.Delete(arquivo);
        return Task.CompletedTask;
    }

    public string Link(string chave, string nomeDoArquivo, DateTimeOffset expira)
        => RotaDoDownload + Protetor.Protect(chave + "\n" + nomeDoArquivo, expira);

    /// <summary>O arquivo e o nome do download do token, ou nulo quando ele venceu, foi adulterado ou o zip já saiu.</summary>
    public (string Arquivo, string Nome)? Abrir(string token)
    {
        try
        {
            var conteudo = Protetor.Unprotect(token, out var expira);
            if (expira <= relogio.GetUtcNow())
                return null;
            var partes = conteudo.Split('\n', 2);
            var arquivo = Caminho(partes[0]);
            return File.Exists(arquivo) ? (arquivo, partes.Length > 1 ? partes[1] : "artefato.zip") : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private string Caminho(string chave)
    {
        var raiz = Path.GetFullPath(opcoes.Value.Pasta);
        var arquivo = Path.GetFullPath(Path.Combine(raiz, chave));
        if (!arquivo.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A chave do artefato sai da pasta dos artefatos.");
        return arquivo;
    }
}
