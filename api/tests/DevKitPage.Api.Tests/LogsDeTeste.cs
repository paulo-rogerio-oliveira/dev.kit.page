using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DevKitPage.Api.Tests;

/// <summary>Um provedor de log que guarda cada mensagem formatada (com a exceção) — o teste procura o que não pode estar lá.</summary>
public sealed class LogsDeTeste : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _mensagens = new();

    public IReadOnlyCollection<string> Mensagens => _mensagens;

    public ILogger CreateLogger(string categoryName) => new Registrador(_mensagens);

    public void Dispose()
    {
    }

    private sealed class Registrador(ConcurrentQueue<string> mensagens) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => mensagens.Enqueue(formatter(state, exception) + exception);
    }
}
