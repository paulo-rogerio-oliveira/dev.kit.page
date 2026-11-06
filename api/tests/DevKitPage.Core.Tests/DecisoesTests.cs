using System.Text;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

public sealed class DecisoesTests
{
    // RFC 6238, apêndice B, SHA-1. Seis dígitos são os últimos seis do vetor de oito.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void Totp_confere_os_vetores_da_rfc(long segundos, string esperado)
    {
        var chave = Totp.Base32(Encoding.ASCII.GetBytes("12345678901234567890"));
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", chave);
        Assert.Equal(esperado, Totp.Codigo(chave, segundos / 30));
    }

    [Fact]
    public void Totp_recusa_ausente_expirado_e_repetido()
    {
        var chave = Totp.NovaChave();
        var agora = DateTimeOffset.FromUnixTimeSeconds(1234567890);
        var passo = agora.ToUnixTimeSeconds() / 30;
        Assert.Equal(passo, Totp.Conferir(chave, Totp.Codigo(chave, passo), agora, -1));
        Assert.Null(Totp.Conferir(chave, Totp.Codigo(chave, passo), agora, passo));
        Assert.Null(Totp.Conferir(chave, Totp.Codigo(chave, passo - 4), agora, -1));
        Assert.Null(Totp.Conferir(chave, null, agora, -1));
    }
}
