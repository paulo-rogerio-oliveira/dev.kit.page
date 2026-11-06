using System.Security.Cryptography;
using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

public sealed class PushTests
{
    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/exemplo", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/exemplo", true)]
    [InlineData("https://web.push.apple.com/exemplo", true)]
    [InlineData("https://wns.notify.windows.com/exemplo", true)]
    [InlineData("http://fcm.googleapis.com/exemplo", false)]
    [InlineData("https://fcm.googleapis.com:444/exemplo", false)]
    [InlineData("https://fcm.googleapis.com.evil.test/exemplo", false)]
    [InlineData("https://localhost/exemplo", false)]
    [InlineData("https://127.0.0.1/exemplo", false)]
    [InlineData("https://fcm.googleapis.com@localhost/exemplo", false)]
    [InlineData("https://fcm.googleapis.com/exemplo#fragmento", false)]
    public void Destino_nao_pode_acessar_rede_interna_ou_seguir_um_host_arbitrario(string endpoint, bool valido)
        => Assert.Equal(valido, RegrasDoPush.EndpointValido(endpoint));

    [Fact]
    public void Chaves_validas_sao_p256_e_auth_de_16_bytes_e_o_id_e_opaco()
    {
        using var chave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = chave.ExportParameters(false);
        var publica = Convert.ToBase64String([4, .. p.Q.X!, .. p.Q.Y!]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var auth = Convert.ToBase64String(new byte[16]).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Null(RegrasDoPush.Validar(new("https://fcm.googleapis.com/teste", new(publica, auth))));
        Assert.NotNull(RegrasDoPush.Validar(new("https://fcm.googleapis.com/teste", new(publica, "a"))));
        Assert.NotNull(RegrasDoPush.Validar(new("https://fcm.googleapis.com/teste", new(new string('A', 87), auth))));
        Assert.False(RegrasDoPush.IdValido("../board"));
        Assert.True(RegrasDoPush.IdValido(Guid.NewGuid().ToString("N")));
        Assert.False(new OpcoesDoPush().Disponivel);
    }
}
