using Microsoft.Extensions.Logging;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>Keeps every entry written to it: level, rendered message and structured fields.</summary>
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public sealed record Entry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Fields
    );

    public List<Entry> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        var fields = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value)
            : [];
        Entries.Add(new Entry(logLevel, formatter(state, exception), fields));
    }
}
