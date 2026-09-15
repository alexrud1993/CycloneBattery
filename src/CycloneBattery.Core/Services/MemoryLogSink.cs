using System.Collections.Concurrent;
using CycloneBattery.Core.Logging;

namespace CycloneBattery.Core.Services;

/// <summary>Keeps log entries in memory so tests and diagnostics can inspect them.</summary>
public sealed class MemoryLogSink : ILogSink
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>Maximum number of entries retained.</summary>
    public int Capacity { get; init; } = 500;

    /// <inheritdoc />
    public LogLevel MinimumLevel => LogLevel.Debug;

    /// <summary>All retained entries, oldest first.</summary>
    public IReadOnlyList<string> Entries => _entries.ToArray();

    /// <inheritdoc />
    public void Write(LogLevel level, string scope, string message, Exception? exception = null)
    {
        _entries.Enqueue(LogSinkExtensions.FormatEntry(level, scope, message, DateTimeOffset.UtcNow, exception));
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
            // Drop the oldest entry.
        }
    }
}
