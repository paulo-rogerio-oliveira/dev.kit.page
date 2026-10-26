using System.Data.Common;
using System.Diagnostics;
using DevKitPage.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DevKitPage.Api;

/// <summary>
/// O erro NÃO tratado como mensagem útil (bug do painel Uso, 26/10): no lugar do "An error occurred while processing your
/// request" do ProblemDetails padrão, a rota que falhou, o porquê e um código que acha a exceção no log. O admin vê a causa
/// (o que falta no esquema do banco, a mensagem do banco, o tipo da exceção); os outros, a rota e o código — a mensagem do
/// banco não sai para quem não administra o site.
/// </summary>
public sealed class ErroUtilDaApi(IProblemDetailsService problemas, EsquemaDoBanco esquema, ILogger<ErroUtilDaApi> log) : IExceptionHandler
{
    /// <summary>O título do 500 (o <c>detail</c> é o que a tela mostra).</summary>
    public const string Titulo = "A API falhou ao processar o pedido.";

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception erro, CancellationToken ct)
    {
        var rota = $"{http.Request.Method} {http.Request.Path}";
        var codigo = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;
        log.LogError(erro, "Falha em {Rota} (código {Codigo}).", rota, codigo);

        http.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var problema = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = Titulo,
            Detail = Detalhe(erro, rota, codigo, esquema.Descrever(), http.User.IsInRole("admin")),
        };
        problema.Extensions["codigo"] = codigo;
        return await problemas.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, Exception = erro, ProblemDetails = problema });
    }

    /// <summary>A mensagem do erro para a tela.</summary>
    /// <param name="faltaNoEsquema">O que a verificação da subida achou faltando no banco (<see cref="EsquemaDoBanco.Descrever"/>).</param>
    /// <param name="admin">Quem pediu administra o site: vê a causa.</param>
    public static string Detalhe(Exception erro, string rota, string codigo, string faltaNoEsquema, bool admin)
    {
        if (!admin)
            return $"A API falhou ao processar {rota}. Informe o código {codigo} ao administrador: o detalhe está no log da API.";

        var doBanco = erro as DbException ?? erro.InnerException as DbException;
        if (doBanco is not null && faltaNoEsquema.Length > 0)
            return $"{rota} falhou: {faltaNoEsquema} (código {codigo})";
        if (doBanco is not null)
            return $"{rota} falhou no banco: {Resumo(doBanco.Message)} Se a API acabou de ser publicada, confira se os scripts de "
                   + $"api/scripts/sqlserver foram aplicados. (código {codigo})";
        return $"{rota} falhou ({erro.GetType().Name}: {Resumo(erro.Message)}) O detalhe está no log da API (código {codigo}).";
    }

    /// <summary>A primeira linha da mensagem, até 300 caracteres.</summary>
    private static string Resumo(string mensagem)
    {
        var linha = mensagem.Split('\n', 2)[0].Trim();
        return linha.Length <= 300 ? linha : linha[..300] + "…";
    }
}

/// <summary>O <c>/health</c> com o esquema: o banco sem uma tabela ou coluna do modelo deixa a API "Degraded", com o que falta.</summary>
public sealed class EsquemaDoBancoCheck(EsquemaDoBanco esquema) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(esquema.Faltas.Count == 0 ? HealthCheckResult.Healthy() : HealthCheckResult.Degraded(esquema.Descrever()));
}
