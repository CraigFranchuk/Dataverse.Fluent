using Microsoft.Extensions.Logging;

namespace Dataverse.Fluent.Tests;

/// <summary>
/// In-memory logger that captures log entries for test assertions.
/// </summary>
internal sealed class TestLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];
    private readonly object _lock = new();

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_lock) return [.. _entries]; }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_lock)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }
}

internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
