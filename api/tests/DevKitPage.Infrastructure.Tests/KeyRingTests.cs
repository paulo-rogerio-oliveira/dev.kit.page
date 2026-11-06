using System.Net;
using System.Security.Cryptography;
using System.Xml.Linq;
using Azure.Core.Cryptography;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using DevKitPage.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace DevKitPage.Infrastructure.Tests;

/// <summary>
/// O key ring do DataProtection: com a configuração, persistido (na pasta, no blob) e protegido pelo cofre; sem ela, o padrão do
/// ASP.NET fora de Production e a recusa clara em Production.
/// </summary>
public sealed class KeyRingTests : IDisposable
{
    private const string ChaveDoCofre = "https://cofre.vault.azure.net/keys/devkit-keyring";
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"devkitpage-keyring-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true); } catch (IOException) { }
    }

    private static ServiceProvider Servicos(Dictionary<string, string?> config, IKeyEncryptionKeyResolver? cofre = null)
    {
        var servicos = new ServiceCollection().AddLogging();
        if (cofre is not null)
            servicos.AddSingleton(cofre);
        servicos.AdicionarInfraestrutura(new ConfigurationBuilder().AddInMemoryCollection(config).Build());
        return servicos.BuildServiceProvider();
    }

    [Fact]
    public void Com_a_pasta_configurada_o_key_ring_e_gravado_nela_e_outra_instancia_abre_o_protegido()
    {
        var config = new Dictionary<string, string?> { ["ProtecaoDeDados:Pasta"] = _pasta };
        string protegido;
        using (var primeira = Servicos(config))
            protegido = primeira.GetRequiredService<IDataProtectionProvider>().CreateProtector("teste").Protect("segredo do push");

        Assert.Single(Directory.GetFiles(_pasta, "key-*.xml"));
        using var segunda = Servicos(config);
        Assert.Equal("segredo do push", segunda.GetRequiredService<IDataProtectionProvider>().CreateProtector("teste").Unprotect(protegido));
    }

    [Fact]
    public void Com_o_blob_configurado_o_repositorio_e_o_do_blob()
    {
        using var servicos = Servicos(new() { ["ProtecaoDeDados:Blob"] = "https://conta.blob.core.windows.net/protecao/keys.xml" });

        var opcoes = servicos.GetRequiredService<IOptions<KeyManagementOptions>>().Value;

        Assert.IsType<RepositorioDeChavesNoBlob>(opcoes.XmlRepository);
        Assert.Null(opcoes.XmlEncryptor);
    }

    [Fact]
    public void Sem_configuracao_vale_o_padrao_do_aspnet_fora_de_production_e_production_e_recusada_com_mensagem_clara()
    {
        using var servicos = Servicos(new());
        var opcoes = servicos.GetRequiredService<IOptions<OpcoesDaProtecaoDeDados>>().Value;

        Assert.Null(servicos.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        KeyRing.Validar(opcoes, new HostingEnvironment { EnvironmentName = "Development" });
        var erro = Assert.Throws<InvalidOperationException>(() => KeyRing.Validar(opcoes, new HostingEnvironment { EnvironmentName = "Production" }));
        Assert.Contains("ProtecaoDeDados__Blob", erro.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_aceita_o_blob_ou_a_pasta_e_recusa_endereco_que_nao_e_https()
    {
        var producao = new HostingEnvironment { EnvironmentName = "Production" };

        KeyRing.Validar(new OpcoesDaProtecaoDeDados { Blob = "https://conta.blob.core.windows.net/protecao/keys.xml", ChaveDoCofre = ChaveDoCofre }, producao);
        KeyRing.Validar(new OpcoesDaProtecaoDeDados { Pasta = _pasta }, producao);
        Assert.Contains("ProtecaoDeDados:Blob", Assert.Throws<InvalidOperationException>(
            () => KeyRing.Validar(new OpcoesDaProtecaoDeDados { Blob = "http://conta/keys.xml" }, producao)).Message, StringComparison.Ordinal);
        Assert.Contains("ProtecaoDeDados:ChaveDoCofre", Assert.Throws<InvalidOperationException>(
            () => KeyRing.Validar(new OpcoesDaProtecaoDeDados { Pasta = _pasta, ChaveDoCofre = "cofre" }, producao)).Message, StringComparison.Ordinal);
        // O cofre protege o key ring de um repositório: sozinho, o DataProtection o recusaria só na primeira chave.
        Assert.Contains("exige o ProtecaoDeDados__Blob", Assert.Throws<InvalidOperationException>(
            () => KeyRing.Validar(new OpcoesDaProtecaoDeDados { ChaveDoCofre = ChaveDoCofre }, new HostingEnvironment { EnvironmentName = "Development" })).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Com_o_cofre_a_chave_gravada_sai_embrulhada_e_so_o_mesmo_cofre_a_abre()
    {
        var config = new Dictionary<string, string?> { ["ProtecaoDeDados:Pasta"] = _pasta, ["ProtecaoDeDados:ChaveDoCofre"] = ChaveDoCofre };
        var cofre = new CofreDeTeste();
        string protegido;
        using (var primeira = Servicos(config, cofre))
            protegido = primeira.GetRequiredService<IDataProtectionProvider>().CreateProtector("teste").Protect("segredo do push");

        var gravada = File.ReadAllText(Assert.Single(Directory.GetFiles(_pasta, "key-*.xml")));
        Assert.Contains("chaveNoCofre", gravada, StringComparison.Ordinal);
        Assert.Contains($"{ChaveDoCofre}/v1", gravada, StringComparison.Ordinal);
        Assert.DoesNotContain("<value>", gravada, StringComparison.Ordinal);
        Assert.Equal(1, cofre.Embrulhos);

        using (var mesmoCofre = Servicos(config, cofre))
            Assert.Equal("segredo do push", mesmoCofre.GetRequiredService<IDataProtectionProvider>().CreateProtector("teste").Unprotect(protegido));
        using var outroCofre = Servicos(config, new CofreDeTeste());
        Assert.ThrowsAny<CryptographicException>(() => outroCofre.GetRequiredService<IDataProtectionProvider>().CreateProtector("teste").Unprotect(protegido));
    }

    [Fact]
    public void O_blob_guarda_as_chaves_de_duas_instancias_e_quem_perde_a_corrida_rele_e_acrescenta()
    {
        var armazem = new BlobDeTeste();
        new RepositorioDeChavesNoBlob(armazem.Cliente()).StoreElement(new XElement("key", new XAttribute("id", "1")), "1");

        // A outra instância grava entre a leitura e a escrita desta: o 412 faz reler, e nenhuma das duas some.
        armazem.AntesDoProximoPut = () => armazem.Gravar("""<repository><key id="1" /><key id="2" /></repository>""");
        new RepositorioDeChavesNoBlob(armazem.Cliente()).StoreElement(new XElement("key", new XAttribute("id", "3")), "3");

        var ids = new RepositorioDeChavesNoBlob(armazem.Cliente()).GetAllElements().Select(e => (string?)e.Attribute("id"));
        Assert.Equal(new string?[] { "1", "2", "3" }, ids);
        Assert.Equal(3, armazem.Puts);
    }

    [Fact]
    public void O_blob_sem_conteiner_ou_sem_permissao_vira_erro_que_diz_o_que_conferir()
    {
        var armazem = new BlobDeTeste { StatusDoPut = HttpStatusCode.Forbidden };

        var erro = Assert.Throws<InvalidOperationException>(() => new RepositorioDeChavesNoBlob(armazem.Cliente()).StoreElement(new XElement("key"), "1"));

        Assert.Contains("Storage Blob Data Contributor", erro.Message, StringComparison.Ordinal);
    }

    /// <summary>Um "Key Vault" local: a chave RSA embrulha e desembrulha com OAEP-SHA256, como o RSA-OAEP-256 do cofre.</summary>
    private sealed class CofreDeTeste : IKeyEncryptionKeyResolver, IKeyEncryptionKey
    {
        private readonly RSA _rsa = RSA.Create(2048);

        public int Embrulhos { get; private set; }

        public string KeyId => $"{ChaveDoCofre}/v1";

        public IKeyEncryptionKey Resolve(string keyId, CancellationToken cancellationToken = default)
            => keyId.StartsWith(ChaveDoCofre, StringComparison.Ordinal) ? this : throw new InvalidOperationException(keyId);

        public Task<IKeyEncryptionKey> ResolveAsync(string keyId, CancellationToken cancellationToken = default) => Task.FromResult(Resolve(keyId, cancellationToken));

        public byte[] WrapKey(string algorithm, ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default)
        {
            Assert.Equal("RSA-OAEP-256", algorithm);
            Embrulhos++;
            return _rsa.Encrypt(key.ToArray(), RSAEncryptionPadding.OaepSHA256);
        }

        public Task<byte[]> WrapKeyAsync(string algorithm, ReadOnlyMemory<byte> key, CancellationToken cancellationToken = default)
            => Task.FromResult(WrapKey(algorithm, key, cancellationToken));

        public byte[] UnwrapKey(string algorithm, ReadOnlyMemory<byte> encryptedKey, CancellationToken cancellationToken = default)
            => _rsa.Decrypt(encryptedKey.ToArray(), RSAEncryptionPadding.OaepSHA256);

        public Task<byte[]> UnwrapKeyAsync(string algorithm, ReadOnlyMemory<byte> encryptedKey, CancellationToken cancellationToken = default)
            => Task.FromResult(UnwrapKey(algorithm, encryptedKey, cancellationToken));
    }

    /// <summary>Um blob de mentira atrás do transporte do SDK: o GET devolve o conteúdo e o ETag; o PUT confere If-Match/If-None-Match.</summary>
    private sealed class BlobDeTeste : HttpMessageHandler
    {
        private string? _conteudo;
        private int _versao;

        public Action? AntesDoProximoPut { get; set; }
        public HttpStatusCode? StatusDoPut { get; set; }
        public int Puts { get; private set; }

        private string Etag => $"\"0x{_versao:X8}\"";

        public void Gravar(string conteudo)
        {
            _conteudo = conteudo;
            _versao++;
        }

        public BlobClient Cliente()
        {
            var opcoes = new BlobClientOptions { Transport = new HttpClientTransport(new HttpClient(this)) };
            opcoes.Retry.MaxRetries = 0;
            return new BlobClient(new Uri("https://conta.blob.core.windows.net/protecao/keys.xml"), opcoes);
        }

        // O IXmlRepository do DataProtection é síncrono: o SDK chama o Send, e não o SendAsync.
        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => SendAsync(request, cancellationToken).GetAwaiter().GetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                if (_conteudo is null)
                    return Erro(HttpStatusCode.NotFound, "BlobNotFound");
                var resposta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_conteudo) };
                resposta.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(Etag);
                resposta.Headers.Add("x-ms-blob-type", "BlockBlob");
                resposta.Content.Headers.LastModified = DateTimeOffset.UtcNow;
                return resposta;
            }

            Puts++;
            if (StatusDoPut is { } status)
                return Erro(status, "AuthorizationPermissionMismatch");
            var antes = AntesDoProximoPut;
            AntesDoProximoPut = null;
            antes?.Invoke();
            var seCasar = request.Headers.IfMatch.FirstOrDefault()?.Tag;
            var seNenhum = request.Headers.IfNoneMatch.Any(t => t.Tag == "*") || request.Headers.TryGetValues("If-None-Match", out var v) && v.Contains("*");
            if ((seCasar is not null && seCasar != Etag) || (seNenhum && _conteudo is not null))
                return Erro(HttpStatusCode.PreconditionFailed, "ConditionNotMet");
            Gravar(await request.Content!.ReadAsStringAsync(cancellationToken));
            var criada = new HttpResponseMessage(HttpStatusCode.Created) { Content = new ByteArrayContent([]) };
            criada.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(Etag);
            criada.Content.Headers.LastModified = DateTimeOffset.UtcNow;
            return criada;
        }

        private static HttpResponseMessage Erro(HttpStatusCode status, string codigo)
        {
            var resposta = new HttpResponseMessage(status) { Content = new ByteArrayContent([]) };
            resposta.Headers.Add("x-ms-error-code", codigo);
            return resposta;
        }
    }
}
