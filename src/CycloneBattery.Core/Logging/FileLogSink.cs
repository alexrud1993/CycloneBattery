namespace CycloneBattery.Core.Logging;

/// <summary>
/// Writes one rolling log file per day into the application data folder.
/// </summary>
/// <remarks>
/// Entries are appended under a lock and the file is opened per write, which keeps the
/// implementation trivial and safe when the app is closed abruptly. Log volume is low by design:
/// battery changes and state transitions, never a per-report dump.
/// </remarks>
public sealed class FileLogSink : ILogSink
{
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;

    public FileLogSink(string directory, LogLevel minimumLevel = LogLevel.Info, Func<DateTimeOffset>? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        MinimumLevel = minimumLevel;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <inheritdoc />
    public LogLevel MinimumLevel { get; }

    /// <summary>The folder log files are written to.</summary>
    public string Directory => _directory;

    /// <inheritdoc />
    public void Write(LogLevel level, string scope, string message, Exception? exception = null)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        DateTimeOffset now = _now();
        string entry = LogSinkExtensions.FormatEntry(level, scope, message, now, exception);

        lock (_gate)
        {
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.AppendAllText(Path.Combine(_directory, $"app-{now:yyyyMMdd}.log"), entry + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take the application down.
            }
            catch (UnauthorizedAccessException)
            {
                // Same: a locked or read-only folder is not fatal.
            }
        }
    }
}
