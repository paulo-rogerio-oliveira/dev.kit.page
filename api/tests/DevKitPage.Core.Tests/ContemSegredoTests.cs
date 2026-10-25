namespace DevKitPage.Core.Tests;

/// <summary>
/// US #663 — o SEGREDO barrado no contexto (<see cref="RegrasDosRelatos.ContemSegredo"/>): os mesmos padrões da máscara
/// dos relatos, sem uma segunda lista. Reconhece o que vazaria (PAT, Bearer, JWT, senha, connection string, chave
/// privada) e não barra o texto comum de contexto (URL, nome de branch, GUID).
/// </summary>
public sealed class ContemSegredoTests
{
    [Theory]
    [InlineData("Use o PAT ghp_abcdefghijklmnopqrstuvwxyz0123456789 para clonar.")]
    [InlineData("Authorization: Bearer abc.def-ghi_jkl")]
    [InlineData("token: eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NSJ9.c2lnbmF0dXJlLXRlc3Rl")]
    [InlineData("A senha do banco: password=Sup3rS3cret")]
    [InlineData("Server=sql.local;Database=uau;User Id=sa;Password=123;")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA\n-----END RSA PRIVATE KEY-----")]
    [InlineData("O PAT do Azure: abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnop")]
    public void Reconhece_o_segredo(string texto) => Assert.True(RegrasDosRelatos.ContemSegredo(texto));

    [Theory]
    [InlineData("O repositório é https://github.com/empresa/uau-web e o build roda no Azure Pipelines.")]
    [InlineData("O branch da task segue feature/{equipe}/us-{US}; o hotfix sai de main.")]
    [InlineData("O id do tenant de testes é 3f2504e0-4f89-11d3-9a0c-0305e82c3301.")]
    [InlineData("Use .NET 10, xUnit e o padrão de token de cancelamento em todo método assíncrono.")]
    [InlineData("")]
    [InlineData(null)]
    public void Nao_barra_o_texto_comum_do_contexto(string? texto) => Assert.False(RegrasDosRelatos.ContemSegredo(texto));

    [Fact]
    public void A_chave_privada_tambem_sai_da_mascara_dos_relatos()
    {
        var mascarado = RegrasDosRelatos.MascararSegredos("antes -----BEGIN PRIVATE KEY-----\nAAAA\n-----END PRIVATE KEY----- depois");

        Assert.Equal("antes " + RegrasDosRelatos.Segredo + " depois", mascarado);
    }
}
