namespace CycloneBattery.Core.Logging;

/// <summary>Severity of a log entry. The default level in normal use is <see cref="Info"/>.</summary>
public enum LogLevel
{
    /// <summary>Detailed tracing. Off by default; never used to dump every input report.</summary>
    Debug = 0,

    /// <summary>Normal lifecycle events: start, discovery, connect, battery changes.</summary>
    Info = 1,

    /// <summary>Something unexpected but recoverable, e.g. a rejected frame or corrupt settings.</summary>
    Warning = 2,

    /// <summary>An exception was handled.</summary>
    Error = 3,
}

/// <summary>Sink for application log entries.</summary>
public interface ILogSink
{
    /// <summary>Minimum level that will actually be written.</summary>
    LogLevel MinimumLevel { get; }

    /// <summary>Writes one entry. Implementations must be safe to call from any thread.</summary>
    void Write(LogLevel level, string scope, string message, Exception? exception = null);
}

/// <summary>Extension helpers so call sites stay short.</summary>
public static class LogSinkExtensions
{
    public static void Debug(this ILogSink sink, string scope, string message) => sink.Write(LogLevel.Debug, scope, message);

    public static void Info(this ILogSink sink, string scope, string message) => sink.Write(LogLevel.Info, scope, message);

    public static void Warning(this ILogSink sink, string scope, string message) => sink.Write(LogLevel.Warning, scope, message);

    public static void Error(this ILogSink sink, string scope, string message, Exception? exception = null) =>
        sink.Write(LogLevel.Error, scope, message, exception);

    /// <summary>Formats one entry the same way every sink renders it.</summary>
    public static string FormatEntry(LogLevel level, string scope, string message, DateTimeOffset timestamp, Exception? exception = null)
    {
        string line = $"{timestamp:yyyy-MM-dd HH:mm:ss.fff} [{level,-7}] [{scope}] {message}";
        return exception is null ? line : line + Environment.NewLine + exception;
    }
}

/// <summary>A sink that discards everything. Used by tests and by <c>--diagnostics</c>.</summary>
public sealed class NullLogSink : ILogSink
{
    /// <summary>Shared instance.</summary>
    public static NullLogSink Instance { get; } = new();

    /// <inheritdoc />
    public LogLevel MinimumLevel => LogLevel.Error;

    /// <inheritdoc />
    public void Write(LogLevel level, string scope, string message, Exception? exception = null)
    {
        // Intentionally empty.
    }
}
