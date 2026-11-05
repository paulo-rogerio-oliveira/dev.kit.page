using System.Collections.Concurrent;
using DevKitPage.Core;

namespace DevKitPage.Api.Tests;

/// <summary>
/// A fila do controle remoto nos testes da API: guarda as mensagens que a API pôs (com o TTL) e falha quando o teste manda —
/// nenhum teste fala com o Azure.
/// </summary>
public sealed class FilaDeTeste : IFilaDeComandos
{
    public ConcurrentQueue<(FilaDoUsuario Fila, string Mensagem, TimeSpan Ttl)> Enviadas { get; } = new();
    public bool Falhar { get; set; }

    public Task EnviarAsync(FilaDoUsuario fila, string mensagem, TimeSpan ttl, CancellationToken ct)
    {
        if (Falhar)
            throw new FalhaNaFila("A fila recusou o comando (503 ServerBusy).");
        Enviadas.Enqueue((fila, mensagem, ttl));
        return Task.CompletedTask;
    }

    public Task<AcessoAFila> EmitirSasAsync(FilaDoUsuario fila, DateTimeOffset ate, CancellationToken ct)
        => Task.FromResult(new AcessoAFila($"https://{fila.Conta}.queue.core.windows.net/{fila.Fila}", "sv=2021-12-02&sp=p&spr=https&sig=teste", ate));
}
