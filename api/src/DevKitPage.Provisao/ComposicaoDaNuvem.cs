using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using DevKitPage.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DevKitPage.Provisao;

/// <summary>
/// O registro do provedor de nuvem do trabalhador: o Azure com a identidade gerenciada dele ou, fora de Production, o simulado
/// (a demonstração e o e2e, como o checkout simulado dos pagamentos).
/// </summary>
public static class ComposicaoDaNuvem
{
    public static IServiceCollection AdicionarNuvem(this IServiceCollection servicos, IConfiguration configuracao, IHostEnvironment ambiente)
    {
        servicos.Configure<OpcoesDoAzure>(configuracao.GetSection(OpcoesDoAzure.Secao));
        var provedor = configuracao[$"{OpcoesDoAzure.Secao}:{nameof(OpcoesDoAzure.Provedor)}"] ?? OpcoesDoAzure.ProvedorAzure;

        if (string.Equals(provedor.Trim(), OpcoesDoAzure.ProvedorSimulado, StringComparison.OrdinalIgnoreCase))
        {
            if (ambiente.IsProduction())
                throw new InvalidOperationException("O provedor simulado da provisão não roda em Production: use Provisao:Azure:Provedor=azure.");
            servicos.TryAddSingleton<IProvedorDeNuvem, ProvedorSimulado>();
            return servicos;
        }

        servicos.TryAddSingleton<TokenCredential>(sp => Credencial(sp.GetRequiredService<IOptions<OpcoesDoAzure>>().Value));
        servicos.AddHttpClient<IProvedorDeNuvem, ProvedorAzure>(ProvedorAzure.NomeDoCliente, cliente => cliente.Timeout = TimeSpan.FromSeconds(60));
        return servicos;
    }

    /// <summary>
    /// A credencial do trabalhador: a identidade gerenciada ATRIBUÍDA a ele, pelo client id; sem ele, a do ambiente (o
    /// <c>az login</c> de quem desenvolve) — nunca um segredo de aplicação na configuração.
    /// </summary>
    public static TokenCredential Credencial(OpcoesDoAzure opcoes)
        => string.IsNullOrWhiteSpace(opcoes.IdentidadeClientId)
            ? new DefaultAzureCredential(new DefaultAzureCredentialOptions { ExcludeInteractiveBrowserCredential = true })
            : new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(opcoes.IdentidadeClientId.Trim()));
}

/// <summary>
/// O Azure de mentira para a demonstração e o e2e: a primeira volta de cada recurso responde "em andamento" e a segunda,
/// pronto — o mesmo caminho assíncrono do Azure, sem criar nada pago. A região <c>falha-simulada</c> recusa de vez, para a
/// tela de falha e o repetir serem demonstráveis. Só fora de Production (<see cref="ComposicaoDaNuvem.AdicionarNuvem"/>).
/// </summary>
public sealed class ProvedorSimulado : IProvedorDeNuvem
{
    /// <summary>A região que o simulado sempre recusa.</summary>
    public const string RegiaoQueFalha = "falha-simulada";

    private readonly ConcurrentDictionary<string, int> _voltas = new(StringComparer.Ordinal);

    public Task<ResultadoNaNuvem> GarantirFilaAsync(AlvoNaNuvem alvo, CancellationToken ct)
    {
        if (alvo.Regiao == RegiaoQueFalha)
            return Task.FromResult(new ResultadoNaNuvem(SituacaoNaNuvem.FalhaDefinitiva, "Região recusada pelo provedor simulado."));
        return Task.FromResult(_voltas.AddOrUpdate("criar:" + alvo.Conta, 1, (_, n) => n + 1) == 1
            ? new ResultadoNaNuvem(SituacaoNaNuvem.EmAndamento, "Simulado: a conta está sendo criada.")
            : new ResultadoNaNuvem(SituacaoNaNuvem.Pronto, "Simulado: grupo, conta e fila prontos."));
    }

    public Task<ResultadoNaNuvem> RemoverFilaAsync(AlvoNaNuvem alvo, CancellationToken ct)
        => Task.FromResult(_voltas.AddOrUpdate("remover:" + alvo.Conta, 1, (_, n) => n + 1) == 1
            ? new ResultadoNaNuvem(SituacaoNaNuvem.EmAndamento, "Simulado: o grupo está sendo removido.")
            : new ResultadoNaNuvem(SituacaoNaNuvem.Pronto, "Simulado: fila e grupo removidos."));
}
