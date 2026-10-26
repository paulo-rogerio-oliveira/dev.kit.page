using System.Data.Common;

namespace DevKitPage.Api.Tests;

/// <summary>
/// O 500 com mensagem útil (bug do painel Uso, 26/10): o admin vê a causa — o que falta no esquema, a mensagem do banco ou
/// o tipo da exceção —, e quem não administra vê a rota e o código do log, sem a mensagem do banco.
/// </summary>
public sealed class ErrosDaApiTests
{
    private sealed class ErroDoBanco(string mensagem) : DbException(mensagem);

    private const string Rota = "GET /api/dashboard/utilidade";

    [Fact]
    public void O_admin_ve_o_que_falta_no_esquema_e_o_script()
    {
        var detalhe = ErroUtilDaApi.Detalhe(new ErroDoBanco("Invalid object name 'FotosDeEstado'."), Rota, "abc", "O banco não tem a tabela FotosDeEstado (Us687.sql).", admin: true);

        Assert.Equal("GET /api/dashboard/utilidade falhou: O banco não tem a tabela FotosDeEstado (Us687.sql). (código abc)", detalhe);
    }

    [Fact]
    public void O_admin_ve_a_mensagem_do_banco_quando_a_verificacao_nao_achou_falta()
    {
        var detalhe = ErroUtilDaApi.Detalhe(new InvalidOperationException("envolve", new ErroDoBanco("Invalid column name 'Valor'.\nlinha 2")), Rota, "abc", "", admin: true);

        Assert.StartsWith("GET /api/dashboard/utilidade falhou no banco: Invalid column name 'Valor'. Se a API acabou de ser publicada", detalhe);
        Assert.DoesNotContain("linha 2", detalhe);
    }

    [Fact]
    public void O_admin_ve_o_tipo_da_excecao_e_os_outros_so_a_rota_e_o_codigo()
    {
        var erro = new InvalidOperationException("The LINQ expression could not be translated.");

        Assert.Equal("GET /api/dashboard/utilidade falhou (InvalidOperationException: The LINQ expression could not be translated.) O detalhe está no log da API (código abc).",
            ErroUtilDaApi.Detalhe(erro, Rota, "abc", "", admin: true));
        var doGestor = ErroUtilDaApi.Detalhe(new ErroDoBanco("Invalid object name 'FotosDeEstado'."), Rota, "abc", "O banco não tem a tabela FotosDeEstado.", admin: false);
        Assert.Equal("A API falhou ao processar GET /api/dashboard/utilidade. Informe o código abc ao administrador: o detalhe está no log da API.", doGestor);
    }
}
