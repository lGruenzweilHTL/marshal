using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Marshal.Testing;

/// <summary>
/// Logger that keeps every entry as <c>Level: message</c>, for asserting what was (or was not) logged.
/// </summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public ConcurrentQueue<string> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue($"{logLevel}: {formatter(state, exception)}");
}
