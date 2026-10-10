using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// O Ed25519 puro (US #425, licença assinada) contra os vetores de teste do RFC 8032 (seção 7.1): a chave
/// pública, a assinatura byte a byte e a verificação — e a recusa da assinatura adulterada, da chave errada e
/// da entrada malformada.
/// </summary>
public sealed class Ed25519Tests
{
    public static TheoryData<string, string, string, string> Vetores => new()
    {
        {
            "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60",
            "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a",
            "",
            "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b"
        },
        {
            "4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb",
            "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c",
            "72",
            "92a009a9f0d4cab8720e820b5f642540a2b27b5416503f8fb3762223ebdb69da085ac1e43e15996e458f3613d0f11d8c387b2eaeb4302aeeb00d291612bb0c00"
        },
        {
            "c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7",
            "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025",
            "af82",
            "6291d657deec24024827e69c3abe01a30ce548a284743a445e3680d7db5ac3ac18ff9b538d16f290ae67f760984dc6594a7c15e9716ed28dc027beceea1ec40a"
        },
    };

    [Theory]
    [MemberData(nameof(Vetores))]
    public void Confere_os_vetores_do_rfc_8032(string semente, string publica, string mensagem, string assinatura)
    {
        var chave = Convert.FromHexString(semente);
        var texto = Convert.FromHexString(mensagem);

        Assert.Equal(publica, Convert.ToHexStringLower(Ed25519.ChavePublica(chave)));
        Assert.Equal(assinatura, Convert.ToHexStringLower(Ed25519.Assinar(chave, texto)));
        Assert.True(Ed25519.Verificar(Convert.FromHexString(publica), texto, Convert.FromHexString(assinatura)));
    }

    [Fact]
    public void Recusa_assinatura_adulterada_mensagem_trocada_chave_errada_e_tamanho_errado()
    {
        var (semente, outra) = (Enumerable.Range(1, 32).Select(i => (byte)i).ToArray(), Enumerable.Range(2, 32).Select(i => (byte)i).ToArray());
        var publica = Ed25519.ChavePublica(semente);
        var mensagem = "licença"u8.ToArray();
        var assinatura = Ed25519.Assinar(semente, mensagem);
        var adulterada = (byte[])assinatura.Clone();
        adulterada[10] ^= 0x01;

        Assert.True(Ed25519.Verificar(publica, mensagem, assinatura));
        Assert.False(Ed25519.Verificar(publica, mensagem, adulterada));
        Assert.False(Ed25519.Verificar(publica, "licenca"u8, assinatura));
        Assert.False(Ed25519.Verificar(Ed25519.ChavePublica(outra), mensagem, assinatura));
        Assert.False(Ed25519.Verificar(publica[..31], mensagem, assinatura));
        Assert.False(Ed25519.Verificar(publica, mensagem, assinatura[..63]));
    }
}
