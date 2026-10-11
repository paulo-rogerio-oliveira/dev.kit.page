using System.Text;
using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>
/// A licença assinada (US #425, fase 4) — válida, adulterada, formato antigo, chave errada —, a janela e as recusas
/// da renovação; a assinatura do aviso do gateway (fase 5) e a adesão por código e e-mail (fase 3).
/// </summary>
public sealed class LicencasTests
{
    private static readonly byte[] Semente = Enumerable.Range(10, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] Publica = Ed25519.ChavePublica(Semente);
    private static readonly DateTimeOffset Agora = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static ConteudoDaLicenca Conteudo(string tipo = TiposDeLicenca.Assinatura, DateTimeOffset? emitidaEm = null)
        => new(LicencaAssinada.VersaoDoFormato, tipo, "maquina-1", "ana@empresa.com", emitidaEm ?? Agora, (emitidaEm ?? Agora).AddDays(30));

    [Fact]
    public void Licenca_valida_volta_com_o_conteudo()
    {
        var texto = LicencaAssinada.Emitir(Conteudo(), Semente);

        Assert.StartsWith("DKL2.", texto);
        Assert.Equal(Conteudo(), LicencaAssinada.Ler(texto, Publica));
    }

    [Fact]
    public void Licenca_adulterada_com_chave_errada_ou_no_formato_antigo_e_recusada()
    {
        var texto = LicencaAssinada.Emitir(Conteudo(), Semente);
        var partes = texto.Split('.');
        // Troca a validade no payload, mantendo a assinatura original.
        var payload = Encoding.UTF8.GetString(LicencaAssinada.DeBase64Url(partes[1])).Replace("2026-11-09", "2027-11-09", StringComparison.Ordinal);
        var adulterada = $"{partes[0]}.{LicencaAssinada.Base64Url(Encoding.UTF8.GetBytes(payload))}.{partes[2]}";

        Assert.Null(LicencaAssinada.Ler(adulterada, Publica));
        Assert.Null(LicencaAssinada.Ler(texto, Ed25519.ChavePublica(new byte[32])));
        Assert.Null(LicencaAssinada.Ler("DKL1" + texto[4..], Publica));
        Assert.Null(LicencaAssinada.Ler("DKL2.nao-e-base64!.x", Publica));
        Assert.Null(LicencaAssinada.Ler(null, Publica));
    }

    [Fact]
    public void Janela_de_renovacao_abre_no_setimo_dia()
    {
        Assert.False(RegrasDeLicenca.NaJanela(Agora, Agora.AddDays(6.9), 7));
        Assert.True(RegrasDeLicenca.NaJanela(Agora, Agora.AddDays(7), 7));
    }

    [Fact]
    public void Cada_recusa_da_renovacao()
    {
        var empresa = new Empresa { Id = 1, Nome = "A" };
        var dev = new Usuario { Login = "ana", Papel = Papeis.Dev, EmpresaId = 1 };

        Assert.Equal(RecusaDaRenovacao.Nenhuma, RegrasDeLicenca.Avaliar(dev, empresa, null, Agora, 7));
        Assert.Equal(RecusaDaRenovacao.UsuarioBloqueado, RegrasDeLicenca.Avaliar(new Usuario { BloqueadoPeloAdmin = true }, empresa, null, Agora, 7));
        Assert.Equal(RecusaDaRenovacao.SemEmpresa, RegrasDeLicenca.Avaliar(dev, null, null, Agora, 7));
        Assert.Equal(RecusaDaRenovacao.AssinaturaCancelada, RegrasDeLicenca.Avaliar(dev, new Empresa { AssinaturaCanceladaEmUtc = Agora.UtcDateTime }, null, Agora, 7));
        Assert.Equal(RecusaDaRenovacao.CedoDemais, RegrasDeLicenca.Avaliar(dev, empresa, Conteudo(emitidaEm: Agora.AddDays(-3)), Agora, 7));
        Assert.Equal(RecusaDaRenovacao.Nenhuma, RegrasDeLicenca.Avaliar(dev, empresa, Conteudo(emitidaEm: Agora.AddDays(-8)), Agora, 7));
        // O trial não espera janela: é a primeira licença de quem acabou de entrar.
        Assert.Equal(RecusaDaRenovacao.Nenhuma, RegrasDeLicenca.Avaliar(dev, empresa, Conteudo(TiposDeLicenca.Trial), Agora, 7));
    }

    [Theory]
    [InlineData("ABCDEF0123456789ABCDEF0123456789", "abcdef0123456789abcdef0123456789")]
    [InlineData("curta", null)]
    [InlineData("não é hex 0123456789abcdef", null)]
    [InlineData(null, null)]
    public void Impressao_aceita_so_hash_hex(string? texto, string? esperada)
        => Assert.Equal(esperada, RegrasDeLicenca.Impressao(texto));

    [Fact]
    public void Aviso_do_gateway_confere_a_assinatura_o_corpo_e_o_instante()
    {
        const string corpo = """{"id":"evt_1","tipo":"pagamento.aprovado"}""";
        var cabecalho = RegrasDoGateway.Assinar("segredo", corpo, Agora);

        Assert.True(RegrasDoGateway.Valida(cabecalho, "segredo", corpo, Agora.AddSeconds(30), 300));
        Assert.False(RegrasDoGateway.Valida(cabecalho, "outro-segredo", corpo, Agora, 300));
        Assert.False(RegrasDoGateway.Valida(cabecalho, "segredo", corpo + " ", Agora, 300));
        Assert.False(RegrasDoGateway.Valida(cabecalho, "segredo", corpo, Agora.AddMinutes(10), 300)); // repetição de um aviso velho
        Assert.False(RegrasDoGateway.Valida(cabecalho, string.Empty, corpo, Agora, 300)); // sem segredo, nada confere
        Assert.False(RegrasDoGateway.Valida("t=abc,v1=00", "segredo", corpo, Agora, 300));
        Assert.False(RegrasDoGateway.Valida(null, "segredo", corpo, Agora, 300));
    }

    [Fact]
    public void Adesao_exige_codigo_e_email_valido_e_o_link_leva_o_token()
    {
        Assert.Empty(RegrasDaConta.Validar(new AdesaoV1("dk-7qh4-m2xa", " Ana@Empresa.com ", "Ana")));
        Assert.Equal(new[] { "codigo", "email" }, RegrasDaConta.Validar(new AdesaoV1(" ", "ana@", null)).Keys.Order());
        Assert.Equal("ana@empresa.com", RegrasDaConta.Email(" Ana@Empresa.COM "));
        Assert.Equal("https://site.test/definir-senha?token=a%2Bb", RegrasDaConta.Link("https://site.test/", "a+b"));
        Assert.True(RegrasDaConta.LoginObrigatorio(true, false));
        Assert.False(RegrasDaConta.LoginObrigatorio(false, true));
        Assert.True(RegrasDaConta.LoginObrigatorio(null, true));
    }
}
