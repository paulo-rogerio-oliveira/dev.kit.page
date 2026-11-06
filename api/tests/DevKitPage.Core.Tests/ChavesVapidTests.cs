using System.Security.Cryptography;

namespace DevKitPage.Core.Tests;

public sealed class ChavesVapidTests
{
    [Fact]
    public void O_par_gerado_e_aceito_pelo_push_e_a_privada_e_a_da_publica()
    {
        var par = ChavesVapid.Gerar();

        Assert.True(new OpcoesDoPush { ChavePublica = par.Publica, ChavePrivada = par.Privada, Assunto = "mailto:suporte@devkit.page" }.Disponivel);
        // O que a privada assina, a pública confere: o par não está trocado nem misturado.
        var publica = RegrasDoPush.Bytes(par.Publica)!;
        var ponto = new ECPoint { X = publica[1..33], Y = publica[33..65] };
        using var privada = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = ponto, D = RegrasDoPush.Bytes(par.Privada) });
        using var conferente = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = ponto });
        byte[] mensagem = [1, 2, 3];
        Assert.True(conferente.VerifyData(mensagem, privada.SignData(mensagem, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Cada_geracao_e_um_par_novo_em_base64url_sem_preenchimento()
    {
        var primeiro = ChavesVapid.Gerar();
        var segundo = ChavesVapid.Gerar();

        Assert.NotEqual(primeiro.Privada, segundo.Privada);
        Assert.Equal(87, primeiro.Publica.Length);
        Assert.Equal(43, primeiro.Privada.Length);
        Assert.All(new[] { primeiro.Publica, primeiro.Privada }, texto => Assert.DoesNotContain(texto, c => c is '=' or '+' or '/'));
    }

    [Fact]
    public void As_instrucoes_trazem_as_tres_app_settings_e_nao_repetem_a_privada_no_comando()
    {
        var par = ChavesVapid.Gerar();

        var texto = ChavesVapid.Instrucoes(par, "https://devkit.page");

        Assert.Contains($"Push__ChavePublica={par.Publica}", texto, StringComparison.Ordinal);
        Assert.Contains($"Push__ChavePrivada={par.Privada}", texto, StringComparison.Ordinal);
        Assert.Contains("Push__Assunto=https://devkit.page", texto, StringComparison.Ordinal);
        Assert.Equal(1, texto.Split(par.Privada).Length - 1);
    }
}
