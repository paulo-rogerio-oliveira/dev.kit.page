using Azure.Core;
using DevKitPage.Core;
using DevKitPage.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevKitPage.Provisao;

/// <summary>
/// A composição do worker: a mesma infraestrutura da API (a base e o <see cref="IMotorDeProvisao"/>), a credencial do Azure, o
/// provedor e a varredura. A credencial é a IDENTIDADE GERENCIADA em produção (a atribuída ao usuário pelo
/// <see cref="OpcoesDoWorker.IdentidadeClientId"/>, ou a do sistema); fora de produção, a credencial padrão (o <c>az login</c>
/// de quem roda). Nenhum client secret é lido daqui.
/// </summary>
public static class ComposicaoDoWorker
{
    public static IServiceCollection AdicionarWorkerDaProvisao(this IServiceCollection servicos, IConfiguration configuracao, IHostEnvironment ambiente)
    {
        var cfg = configuracao.GetSection(OpcoesDoWorker.Secao).Get<OpcoesDoWorker>() ?? new OpcoesDoWorker();
        Validar(cfg, ambiente);

        servicos.AdicionarInfraestrutura(configuracao);
        servicos.Configure<OpcoesDoWorker>(configuracao.GetSection(OpcoesDoWorker.Secao));

        if (Simulado(cfg))
        {
            servicos.AddSingleton<IProvedorDeNuvem, ProvedorSimulado>();
        }
        else
        {
            servicos.AddSingleton(Credencial(cfg, ambiente));
            servicos.AddHttpClient<IProvedorDeNuvem, ProvedorDoAzure>(ProvedorDoAzure.NomeDoCliente, cliente =>
                {
                    cliente.BaseAddress = new Uri(cfg.UrlDoArm.EndsWith('/') ? cfg.UrlDoArm : cfg.UrlDoArm + "/");
                    cliente.Timeout = TimeSpan.FromSeconds(100);
                })
                // Sem redirecionamento automático: o token do ARM não segue para outro host.
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        }

        servicos.AddSingleton<ExecutorDaProvisao>();
        servicos.AddHostedService(sp => sp.GetRequiredService<ExecutorDaProvisao>());
        return servicos;
    }

    /// <summary>
    /// O worker NÃO sobe mal configurado: o provedor desconhecido, o simulado em Production (ele diria "pronto" sem criar nada)
    /// e o do Azure sem a assinatura.
    /// </summary>
    public static void Validar(OpcoesDoWorker cfg, IHostEnvironment ambiente)
    {
        var provedor = (cfg.Provedor ?? string.Empty).Trim().ToLowerInvariant();
        if (provedor is not (OpcoesDoWorker.ProvedorAzure or OpcoesDoWorker.ProvedorSimulado))
            throw new InvalidOperationException($"Provisao:Provedor é '{OpcoesDoWorker.ProvedorAzure}' ou '{OpcoesDoWorker.ProvedorSimulado}', e não '{cfg.Provedor}'.");
        if (provedor == OpcoesDoWorker.ProvedorSimulado && ambiente.IsProduction())
            throw new InvalidOperationException("O provedor simulado não sobe em Production: ele marcaria a fila pronta sem criar nada no Azure.");
        if (provedor == OpcoesDoWorker.ProvedorAzure && string.IsNullOrWhiteSpace(cfg.AssinaturaId))
            throw new InvalidOperationException("Provisao:AssinaturaId não configurada: é a assinatura do dev.kit onde ficam os grupos dos usuários.");
    }

    /// <summary>A credencial do Azure: a identidade gerenciada em produção; a padrão (o az login) no desenvolvimento — a mesma regra da API.</summary>
    public static TokenCredential Credencial(OpcoesDoWorker cfg, IHostEnvironment ambiente)
        => CredencialDoAzure.Criar(cfg.IdentidadeClientId, ambiente.IsProduction());

    private static bool Simulado(OpcoesDoWorker cfg) => string.Equals(cfg.Provedor?.Trim(), OpcoesDoWorker.ProvedorSimulado, StringComparison.OrdinalIgnoreCase);
}
