using DevKitPage.Contracts.V1;

namespace DevKitPage.Core.Tests;

/// <summary>
/// As regras puras dos relatos (US #602): a validação do envio, a captura com teto e assinatura de imagem, o estado e a
/// resposta, e a máscara de defesa em profundidade — os segredos (token, senha, connection string) somem do texto e do
/// contexto mesmo que um dev.kit com defeito os mande.
/// </summary>
public sealed class RegrasDosRelatosTests
{
    /// <summary>Um PNG mínimo: a assinatura de 8 bytes e um pouco de corpo.</summary>
    public static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52];

    [Theory]
    [InlineData("defeito")]
    [InlineData(" Sugestao ")]
    [InlineData("DUVIDA")]
    public void O_relato_com_categoria_conhecida_e_texto_vale(string categoria)
        => Assert.Empty(RegrasDosRelatos.Validar(new RelatoNovoV1(categoria, "A PR foi aberta no repositório errado.")));

    [Fact]
    public void O_relato_sem_texto_sem_categoria_ou_grande_demais_e_recusado()
    {
        Assert.NotEmpty(RegrasDosRelatos.Validar(null));
        Assert.NotEmpty(RegrasDosRelatos.Validar(new RelatoNovoV1("defeito", "  ")));
        Assert.NotEmpty(RegrasDosRelatos.Validar(new RelatoNovoV1("elogio", "Gostei")));
        Assert.NotEmpty(RegrasDosRelatos.Validar(new RelatoNovoV1("defeito", new string('a', RelatosV1.TamanhoMaximoDoTexto + 1))));
        Assert.NotEmpty(RegrasDosRelatos.Validar(new RelatoNovoV1("defeito", "x", new string('c', RelatosV1.TamanhoMaximoDoContexto + 1))));
        Assert.NotEmpty(RegrasDosRelatos.Validar(new RelatoNovoV1("defeito", "x", ChaveDoCliente: new string('k', RelatosV1.TamanhoMaximoDaChave + 1))));
    }

    [Fact]
    public void A_captura_png_ou_jpeg_dentro_do_teto_e_lida()
    {
        Assert.Empty(RegrasDosRelatos.LerCaptura(Convert.ToBase64String(Png), out var png));
        Assert.Equal(Png, png);
        Assert.Empty(RegrasDosRelatos.LerCaptura(Convert.ToBase64String([0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10]), out var jpeg));
        Assert.NotNull(jpeg);
        Assert.Empty(RegrasDosRelatos.LerCaptura(null, out var nenhuma));
        Assert.Null(nenhuma);
    }

    [Fact]
    public void A_captura_acima_do_teto_que_nao_e_imagem_ou_base64_invalido_e_recusada()
    {
        var grande = new byte[RelatosV1.TamanhoMaximoDaCaptura + 1];
        Png.CopyTo(grande, 0);
        Assert.Contains("MB", RegrasDosRelatos.LerCaptura(Convert.ToBase64String(grande), out _));
        Assert.Contains("PNG ou JPEG", RegrasDosRelatos.LerCaptura(Convert.ToBase64String("não é imagem"u8.ToArray()), out _));
        Assert.Contains("base64", RegrasDosRelatos.LerCaptura("@@@ não é base64 @@@", out _));
        Assert.NotEmpty(RegrasDosRelatos.Validar(new RelatoNovoV1("defeito", "x", Captura: Convert.ToBase64String(grande))));
    }

    [Theory]
    [InlineData("Server=sql.acme.local;Database=Vendas;User Id=sa;Password=Sup3r!;", "<connection-string>")]
    [InlineData("conexão: Data Source=srv01;Initial Catalog=UAU;Integrated Security=True", "<connection-string>")]
    [InlineData("DefaultEndpointsProtocol=https;AccountName=acme;AccountKey=abc123==;EndpointSuffix=core.windows.net", "<connection-string>")]
    [InlineData("password=Sup3r!", "password=<segredo>")]
    [InlineData("a senha: minhaSenha123 não funciona", "senha: <segredo>")]
    [InlineData("{\"apiKey\": \"k-123456\"}", "\"apiKey\": \"<segredo>\"")]
    [InlineData("token=abc.def", "token=<segredo>")]
    [InlineData("Authorization: Bearer abcDEF123456789.xyz", "Bearer <segredo>")]
    [InlineData("o PAT ghp_1234567890abcdefghijABCDEFGHIJ1234 vazou", "o PAT <segredo> vazou")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJhLWRvLXRva2Vu", "<segredo>")]
    [InlineData("pat do azure: 4xq7wz3m2k5n6p8r9s0t1u2v3w4x5y6z7a8b9c0d1e2f3g4h5i6j", "<segredo>")]
    public void Os_segredos_sao_mascarados(string texto, string esperado)
    {
        var mascarado = RegrasDosRelatos.MascararSegredos(texto);

        Assert.Contains(esperado, mascarado);
        Assert.DoesNotContain("Sup3r!", mascarado);
        Assert.DoesNotContain("minhaSenha123", mascarado);
        Assert.DoesNotContain("ghp_1234567890", mascarado);
        Assert.DoesNotContain("abc123==", mascarado);
    }

    [Fact]
    public void O_texto_sem_segredo_nao_muda()
    {
        const string texto = "A PR da task multirepo foi criada no repositório errado: era o dev.kit.page, e não o git.kit.";

        Assert.Equal(texto, RegrasDosRelatos.MascararSegredos(texto));
    }

    [Fact]
    public void A_mascara_do_relato_tira_o_segredo_e_depois_o_caminho_e_o_email()
    {
        var mascarado = RegrasDosRelatos.Mascarar(@"Falhou em C:\Users\ana\repo com password=xyz; me avise em ana@acme.com", RelatosV1.TamanhoMaximoDoTexto);

        Assert.DoesNotContain("xyz", mascarado);
        Assert.DoesNotContain(@"C:\Users\ana", mascarado);
        Assert.DoesNotContain("ana@acme.com", mascarado);
        Assert.Contains("<caminho>", mascarado);
        Assert.Contains("<email>", mascarado);
    }

    [Fact]
    public void O_estado_e_a_resposta_sao_validados()
    {
        Assert.Empty(RegrasDosRelatos.ValidarEstado(new AlterarEstadoDoRelatoV1("em-analise")));
        Assert.Empty(RegrasDosRelatos.ValidarEstado(new AlterarEstadoDoRelatoV1(" Resolvido ")));
        Assert.NotEmpty(RegrasDosRelatos.ValidarEstado(new AlterarEstadoDoRelatoV1("fechado")));
        Assert.NotEmpty(RegrasDosRelatos.ValidarEstado(null));
        Assert.Empty(RegrasDosRelatos.ValidarResposta(new RespostaAoRelatoV1("Corrigido na versão 209.")));
        Assert.NotEmpty(RegrasDosRelatos.ValidarResposta(new RespostaAoRelatoV1(" ")));
        Assert.NotEmpty(RegrasDosRelatos.ValidarResposta(new RespostaAoRelatoV1(new string('r', RelatosV1.TamanhoMaximoDaResposta + 1))));
    }

    [Fact]
    public void O_resumo_junta_as_linhas_e_corta_no_tamanho_da_lista()
    {
        Assert.Equal("linha 1 linha 2", RegrasDosRelatos.Resumo("linha 1\r\n  linha 2"));
        var longo = RegrasDosRelatos.Resumo(new string('a', 300));
        Assert.Equal(RegrasDosRelatos.TamanhoDoResumo + 1, longo.Length);
        Assert.EndsWith("…", longo);
    }
}
