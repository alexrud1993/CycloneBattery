namespace CycloneBattery.Core.Services;

/// <summary>One autostart registration: where it lives and what it contains.</summary>
/// <param name="RegistryKeyPath">Registry key, relative to <c>HKEY_CURRENT_USER</c>.</param>
/// <param name="ValueName">Name of the Run value.</param>
/// <param name="ValueData">Quoted executable path plus optional arguments.</param>
public sealed record AutostartEntry(string RegistryKeyPath, string ValueName, string ValueData);

/// <summary>
/// Pure helpers that turn an executable path into a correctly quoted Run value.
/// </summary>
/// <remarks>
/// Split out from the Windows registry code so the quoting rules are covered by unit tests
/// without touching the registry.
/// </remarks>
public static class AutostartCommandBuilder
{
    /// <summary>Current-user Run key. Never the machine-wide one.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Name of the Run value created by this application.</summary>
    public const string ValueName = "CycloneBattery";

    /// <summary>Argument appended so an autostarted instance does not pop the widget up.</summary>
    public const string SilentStartArgument = "--minimized";

    /// <summary>
    /// Builds the entry for <paramref name="executablePath"/>.
    /// </summary>
    /// <param name="executablePath">Full path to <c>CycloneBattery.exe</c>.</param>
    /// <param name="startSilently">Whether to append <see cref="SilentStartArgument"/>.</param>
    public static AutostartEntry Build(string executablePath, bool startSilently = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        string quoted = QuotePath(executablePath);
        string data = startSilently ? quoted + " " + SilentStartArgument : quoted;
        return new AutostartEntry(RunKeyPath, ValueName, data);
    }

    /// <summary>
    /// Wraps a path in double quotes, escaping any embedded quote, so paths containing spaces
    /// survive the <c>Run</c> value round trip.
    /// </summary>
    public static string QuotePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return "\"" + path.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// Extracts the executable path back out of a Run value. Returns <see langword="null"/> when
    /// the value is not quoted, which lets the caller detect an externally edited entry.
    /// </summary>
    public static string? TryParseExecutablePath(string? valueData)
    {
        if (string.IsNullOrWhiteSpace(valueData))
        {
            return null;
        }

        string text = valueData.Trim();
        if (!text.StartsWith('"'))
        {
            return null;
        }

        for (int i = 1; i < text.Length; i++)
        {
            if (text[i] == '"' && text[i - 1] != '\\')
            {
                return text[1..i].Replace("\\\"", "\"", StringComparison.Ordinal);
            }
        }

        return null;
    }
}
