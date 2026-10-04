using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>A validação do pedido de demonstração: obrigatórios, limites de tamanho, e-mail e consentimento (LGPD).</summary>
public sealed class ValidadorDeDemonstracaoTests
{
    private static PedidoDeDemonstracaoV1 Pedido(
        string? nome = "Ana Souza", string? email = "ana@empresa.com.br", string? empresa = "Empresa", string? mensagem = "Quero ver o fluxo.", bool consentimento = true)
        => new(nome, email, empresa, mensagem, consentimento);

    [Fact]
    public void Pedido_completo_passa()
        => Assert.Empty(ValidadorDeDemonstracao.Validar(Pedido()));

    [Fact]
    public void Empresa_e_mensagem_sao_opcionais()
        => Assert.Empty(ValidadorDeDemonstracao.Validar(Pedido(empresa: null, mensagem: "  ")));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nome_e_obrigatorio(string? nome)
        => Assert.Equal(["nome"], ValidadorDeDemonstracao.Validar(Pedido(nome: nome)).Keys);

    [Theory]
    [InlineData(null)]
    [InlineData("sem-arroba")]
    [InlineData("a@b")]
    [InlineData("a@@b.com")]
    [InlineData("a b@c.com")]
    [InlineData("@empresa.com")]
    [InlineData("ana@empresa.")]
    public void Email_vazio_ou_invalido_e_recusado(string? email)
        => Assert.Equal(["email"], ValidadorDeDemonstracao.Validar(Pedido(email: email)).Keys);

    [Fact]
    public void Sem_consentimento_e_recusado()
        => Assert.Equal(["consentimento"], ValidadorDeDemonstracao.Validar(Pedido(consentimento: false)).Keys);

    [Fact]
    public void Textos_acima_do_limite_sao_recusados_por_campo()
    {
        var erros = ValidadorDeDemonstracao.Validar(Pedido(
            nome: new string('n', ValidadorDeDemonstracao.TamanhoMaximoDoNome + 1),
            email: new string('e', ValidadorDeDemonstracao.TamanhoMaximoDoEmail) + "@x.com",
            empresa: new string('x', ValidadorDeDemonstracao.TamanhoMaximoDaEmpresa + 1),
            mensagem: new string('m', ValidadorDeDemonstracao.TamanhoMaximoDaMensagem + 1)));

        Assert.Equal(["email", "empresa", "mensagem", "nome"], erros.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Textos_no_limite_passam()
        => Assert.Empty(ValidadorDeDemonstracao.Validar(Pedido(
            nome: new string('n', ValidadorDeDemonstracao.TamanhoMaximoDoNome),
            mensagem: new string('m', ValidadorDeDemonstracao.TamanhoMaximoDaMensagem))));

    [Fact]
    public void Corpo_ausente_e_recusado()
        => Assert.NotEmpty(ValidadorDeDemonstracao.Validar(null));
}
