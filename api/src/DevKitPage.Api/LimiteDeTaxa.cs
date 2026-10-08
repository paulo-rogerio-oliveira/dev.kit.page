using System.Globalization;
using System.Threading.RateLimiting;
using DevKitPage.Core;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DevKitPage.Api;

/// <summary>
/// A proteção da rota PÚBLICA do formulário: o limite de pedidos por IP do cliente. Atrás do proxy
/// do Azure Container Apps todo visitante chega com o IP do proxy — dividiriam uma cota só —, então o
/// <c>X-Forwarded-For</c> é aplicado ANTES do limitador, aceito só dos proxies de
/// <see cref="OpcoesDoProxy.RedesConfiaveis"/> (de qualquer outro, o cabeçalho é ignorado: um
/// visitante não escolhe o próprio IP para fugir da cota).
/// </summary>
public static class LimiteDeTaxa
{
    /// <summary>A política do formulário de demonstração.</summary>
    public const string PoliticaDoFormulario = "Formulario";

    /// <summary>
    /// A política das rotas que COLETAM ou cadastram (US #381): a exportação dos colaboradores e do
    /// grupo de erro, e a criação de empresa e de gestor. Por USUÁRIO do token (todos chegam
    /// autenticados), com <see cref="OpcoesDoPainel.ExportacoesPorMinuto"/>.
    /// </summary>
    public const string PoliticaDaExportacao = "Exportacao";

    public static IServiceCollection AdicionarLimiteDeTaxa(this IServiceCollection servicos, IConfiguration configuracao)
    {
        servicos.Configure<OpcoesDoProxy>(configuracao.GetSection(OpcoesDoProxy.Secao));
        servicos.Configure<OpcoesDoPainel>(configuracao.GetSection(OpcoesDoPainel.Secao));
        servicos.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<OpcoesDoProxy>>((encaminhados, proxy) =>
        {
            encaminhados.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var rede in proxy.Value.RedesConfiaveis.Where(r => !string.IsNullOrWhiteSpace(r)))
                encaminhados.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(rede.Trim()));
        });

        servicos.AddRateLimiter(limites =>
        {
            limites.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limites.AddPolicy(PoliticaDoFormulario, http =>
            {
                var porMinuto = Math.Max(1, http.RequestServices.GetRequiredService<IOptions<OpcoesDeDemonstracao>>().Value.LimitePorMinuto);
                return RateLimitPartition.GetFixedWindowLimiter(IpDoCliente(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = porMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            limites.AddPolicy(PoliticaDaExportacao, http =>
            {
                var porMinuto = Math.Max(1, http.RequestServices.GetRequiredService<IOptions<OpcoesDoPainel>>().Value.ExportacoesPorMinuto);
                var usuario = Seguranca.UsuarioId(http.User)?.ToString(CultureInfo.InvariantCulture) ?? IpDoCliente(http);
                return RateLimitPartition.GetFixedWindowLimiter("usuario:" + usuario, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = porMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
            limites.OnRejected = async (contexto, ct) =>
            {
                if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                    contexto.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(espera.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                await Results.Problem("Muitos pedidos deste endereço. Tente de novo em um minuto.", statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(contexto.HttpContext);
            };
        });
        return servicos;
    }

    /// <summary>O IP do cliente — o do <c>X-Forwarded-For</c> quando o proxy é confiável (o <c>UseForwardedHeaders</c> já o aplicou).</summary>
    public static string IpDoCliente(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";
}
