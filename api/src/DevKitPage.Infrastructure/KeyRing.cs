using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Azure;
using Azure.Core;
using Azure.Core.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using DevKitPage.Core;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure;

/// <summary>
/// O key ring do DataProtection da API (<see cref="OpcoesDaProtecaoDeDados"/>). A escolha é feita na criação das opções, e não no
/// registro: a configuração final (as variáveis de ambiente, a dos testes) só está completa depois do Build do host — a mesma
/// regra do banco em <see cref="Composicao"/>.
/// </summary>
public static class KeyRing
{
    /// <summary>
    /// Recusa subir em Production sem o key ring persistido e compartilhado: sem ele, cada reinício troca a chave, e o push do
    /// board e o segundo fator protegidos antes deixam de abrir sem erro nenhum na subida.
    /// </summary>
    public static void Validar(OpcoesDaProtecaoDeDados opcoes, IHostEnvironment ambiente)
    {
        var blob = opcoes.Blob.Trim();
        var cofre = opcoes.ChaveDoCofre.Trim();
        if (blob.Length > 0 && !Https(blob))
            throw new InvalidOperationException($"ProtecaoDeDados:Blob não é um endereço https de blob: '{blob}'.");
        if (cofre.Length > 0 && !Https(cofre))
            throw new InvalidOperationException($"ProtecaoDeDados:ChaveDoCofre não é o endereço https de uma chave do Key Vault: '{cofre}'.");
        var semRepositorio = blob.Length == 0 && opcoes.Pasta.Trim().Length == 0;
        if (cofre.Length > 0 && semRepositorio)
            throw new InvalidOperationException("ProtecaoDeDados:ChaveDoCofre exige o ProtecaoDeDados__Blob (ou a Pasta): o cofre protege o key ring, e não o guarda.");
        if (ambiente.IsProduction() && semRepositorio)
            throw new InvalidOperationException(
                "ProtecaoDeDados não configurada: em Production o key ring do DataProtection precisa ser persistido e compartilhado entre as " +
                "instâncias. Configure ProtecaoDeDados__Blob (o blob do key ring, lido com a identidade da API) e, havendo cofre, " +
                "ProtecaoDeDados__ChaveDoCofre — ou ProtecaoDeDados__Pasta num disco compartilhado. Ver docs/publicacao-azure.md.");
    }

    internal static TokenCredential Credencial(IServiceProvider sp)
        => CredencialDoAzure.Criar(sp.GetRequiredService<IOptions<OpcoesDaProtecaoDeDados>>().Value.IdentidadeClientId,
            sp.GetService<IHostEnvironment>()?.IsProduction() ?? true);

    private static bool Https(string texto) => Uri.TryCreate(texto, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}

/// <summary>
/// Põe nas <see cref="KeyManagementOptions"/> o repositório (o blob ou a pasta) e, com o cofre, o cifrador do Key Vault. Sem
/// nada configurado, não mexe: vale o padrão do ASP.NET (e o <see cref="KeyRing.Validar"/> já recusou Production).
/// </summary>
public sealed class ConfiguracaoDoKeyRing(IOptions<OpcoesDaProtecaoDeDados> opcoes, IServiceProvider sp, ILoggerFactory log)
    : IConfigureOptions<KeyManagementOptions>
{
    public void Configure(KeyManagementOptions options)
    {
        var cfg = opcoes.Value;
        if (cfg.Blob.Trim() is { Length: > 0 } blob)
            options.XmlRepository = new RepositorioDeChavesNoBlob(new BlobClient(new Uri(blob), KeyRing.Credencial(sp),
                sp.GetService<BlobClientOptions>() ?? new BlobClientOptions()));
        else if (cfg.Pasta.Trim() is { Length: > 0 } pasta)
            options.XmlRepository = new FileSystemXmlRepository(Directory.CreateDirectory(pasta), log);

        if (cfg.ChaveDoCofre.Trim() is { Length: > 0 } chave)
            options.XmlEncryptor = new CifradorDoCofre(sp.GetRequiredService<IKeyEncryptionKeyResolver>(), chave);
    }
}

/// <summary>
/// O key ring num blob só (um XML com todas as chaves), gravado com concorrência otimista pelo ETag: duas instâncias que criam
/// a chave ao mesmo tempo não se apagam — a segunda relê e acrescenta. O contêiner já existe (o script de infraestrutura o cria);
/// a identidade da API precisa do <c>Storage Blob Data Contributor</c> nele.
/// </summary>
public sealed class RepositorioDeChavesNoBlob(BlobClient blob) : IXmlRepository
{
    private const int Tentativas = 10;

    public IReadOnlyCollection<XElement> GetAllElements() => Ler().Documento.Root!.Elements().ToList();

    public void StoreElement(XElement element, string friendlyName)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var (documento, etag) = Ler();
            documento.Root!.Add(new XElement(element));
            using var conteudo = new MemoryStream();
            documento.Save(conteudo, SaveOptions.DisableFormatting);
            conteudo.Position = 0;
            try
            {
                blob.Upload(conteudo, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = "application/xml; charset=utf-8" },
                    // O blob lido (If-Match) ou, se não havia, nenhum (If-None-Match: *): quem perde a corrida relê.
                    Conditions = etag is { } atual ? new BlobRequestConditions { IfMatch = atual } : new BlobRequestConditions { IfNoneMatch = ETag.All },
                });
                return;
            }
            catch (RequestFailedException e) when (e.Status is 409 or 412 && tentativa < Tentativas)
            {
                // Outra instância gravou no meio: relê e acrescenta de novo.
            }
            catch (RequestFailedException e) when (e.Status is 401 or 403 or 404)
            {
                throw new InvalidOperationException(
                    $"O key ring não foi gravado em {blob.Uri} ({e.Status} {e.ErrorCode}): confira se o contêiner existe e se a identidade da API tem o Storage Blob Data Contributor nele.", e);
            }
        }
    }

    private (XDocument Documento, ETag? Etag) Ler()
    {
        try
        {
            var lido = blob.DownloadContent().Value;
            using var conteudo = lido.Content.ToStream();
            return (XDocument.Load(conteudo), lido.Details.ETag);
        }
        catch (RequestFailedException e) when (e.Status == 404 && e.ErrorCode is null or "BlobNotFound")
        {
            return (new XDocument(new XElement("repository")), null);
        }
    }
}

/// <summary>
/// Protege cada chave do key ring com a chave RSA do Key Vault (envelope): a chave do DataProtection é cifrada com AES-256-GCM
/// por uma chave de dados aleatória, e só esta vai ao cofre, embrulhada (<c>RSA-OAEP-256</c>). O cofre nunca vê o key ring, e
/// o blob nunca guarda chave em claro. Guarda o id VERSIONADO da chave do cofre: girar a chave no cofre não perde as antigas.
/// </summary>
public sealed class CifradorDoCofre(IKeyEncryptionKeyResolver resolvedor, string chave) : IXmlEncryptor
{
    internal const string Algoritmo = "RSA-OAEP-256";

    public EncryptedXmlInfo Encrypt(XElement plaintextElement)
    {
        var dados = RandomNumberGenerator.GetBytes(32);
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
            var claro = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
            var cifrado = new byte[claro.Length];
            var tag = new byte[AesGcm.TagByteSizes.MaxSize];
            using (var aes = new AesGcm(dados, tag.Length))
                aes.Encrypt(nonce, claro, cifrado, tag);
            CryptographicOperations.ZeroMemory(claro);

            var kek = resolvedor.Resolve(chave);
            var embrulhada = kek.WrapKey(Algoritmo, dados);
            return new EncryptedXmlInfo(new XElement("chaveNoCofre",
                new XAttribute("versao", 1),
                new XElement("kid", kek.KeyId),
                new XElement("chave", Convert.ToBase64String(embrulhada)),
                new XElement("nonce", Convert.ToBase64String(nonce)),
                new XElement("tag", Convert.ToBase64String(tag)),
                new XElement("dados", Convert.ToBase64String(cifrado))), typeof(DecifradorDoCofre));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dados);
        }
    }
}

/// <summary>
/// O par do <see cref="CifradorDoCofre"/>: o DataProtection o cria pelo tipo gravado no XML, com o <see cref="IServiceProvider"/>,
/// e o resolvedor do cofre vem do contêiner (o da identidade da API).
/// </summary>
public sealed class DecifradorDoCofre(IServiceProvider servicos) : IXmlDecryptor
{
    public XElement Decrypt(XElement encryptedElement)
    {
        var kid = (string?)encryptedElement.Element("kid") ?? throw new CryptographicException("A chave do key ring não diz qual chave do cofre a protege.");
        var dados = servicos.GetRequiredService<IKeyEncryptionKeyResolver>().Resolve(kid)
            .UnwrapKey(CifradorDoCofre.Algoritmo, Bytes(encryptedElement, "chave"));
        try
        {
            var cifrado = Bytes(encryptedElement, "dados");
            var claro = new byte[cifrado.Length];
            using (var aes = new AesGcm(dados, AesGcm.TagByteSizes.MaxSize))
                aes.Decrypt(Bytes(encryptedElement, "nonce"), cifrado, Bytes(encryptedElement, "tag"), claro);
            try
            {
                return XElement.Parse(Encoding.UTF8.GetString(claro));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(claro);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dados);
        }
    }

    private static byte[] Bytes(XElement elemento, string nome)
        => Convert.FromBase64String((string?)elemento.Element(nome) ?? throw new CryptographicException($"A chave do key ring está sem '{nome}'."));
}
