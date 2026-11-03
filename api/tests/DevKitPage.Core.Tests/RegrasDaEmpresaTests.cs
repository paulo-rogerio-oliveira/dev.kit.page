using DevKitPage.Contracts.V1;
using DevKitPage.Core;

namespace DevKitPage.Core.Tests;

/// <summary>A edição da empresa pelo admin (US #687): o CNPJ pelos dígitos verificadores, a UF, o site e os assentos em uso.</summary>
public sealed class RegrasDaEmpresaTests
{
    private static EmpresaEditadaV1 Edicao(string? cnpj = null, string? uf = null, string? site = null, int assentos = 30)
        => new("Acme Software", "Empresarial", assentos, cnpj, "Software / ERP", "São Paulo", uf, "gestor@acme.com.br", null, site, new DateOnly(2027, 3, 16));

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("11.222.333/0001-82", false)]
    [InlineData("11.111.111/1111-11", false)]
    [InlineData("1122233300018", false)]
    public void Cnpj_pelos_digitos_verificadores(string texto, bool valido)
        => Assert.Equal(valido, RegrasDaEmpresa.CnpjValido(RegrasDaEmpresa.Cnpj(texto)));

    /// <summary>A regra única também vale para o CNPJ alfanumérico da Receita (2026), que o detector do controle remoto usa.</summary>
    [Theory]
    [InlineData("12ABC34501DE35", true)]
    [InlineData("12ABC34501DE36", false)]
    [InlineData("12abc34501de35", false)]
    [InlineData("12ABC34501DEA5", false)]
    public void Cnpj_alfanumerico_pelos_digitos_verificadores(string cnpj, bool valido)
        => Assert.Equal(valido, RegrasDaEmpresa.CnpjValido(cnpj));

    [Fact]
    public void Cnpj_formatado_para_a_tela()
        => Assert.Equal("11.222.333/0001-81", RegrasDaEmpresa.FormatarCnpj("11222333000181"));

    [Fact]
    public void Edicao_valida_e_os_erros_por_campo()
    {
        Assert.Empty(RegrasDaEmpresa.Validar(Edicao("11.222.333/0001-81", "SP", "https://acme.com.br")));
        Assert.Empty(RegrasDaEmpresa.Validar(Edicao()));

        var erros = RegrasDaEmpresa.Validar(Edicao("11.222.333/0001-82", "XX", "acme.com.br", assentos: 0));
        Assert.Equal(new[] { "assentos", "cnpj", "site", "uf" }, erros.Keys.Order());
        Assert.Contains("obrigatório", RegrasDaEmpresa.Validar(null)["empresa"][0]);
    }

    [Fact]
    public void Os_assentos_nao_descem_abaixo_dos_em_uso()
    {
        Assert.Empty(RegrasDaEmpresa.ValidarAssentos(2, 2));
        Assert.Contains("o mínimo é 2", RegrasDaEmpresa.ValidarAssentos(1, 2));
    }
}
