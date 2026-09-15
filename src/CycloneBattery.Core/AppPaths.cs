using System.IO;

namespace CycloneBattery.Core;

/// <summary>
/// Locations of the application's local data. Everything lives under
/// <c>%LOCALAPPDATA%\CycloneBattery\</c>; nothing is written to the registry beyond the optional
/// current-user Run value, and nothing is uploaded anywhere.
/// </summary>
public static class AppPaths
{
    /// <summary>Name of the application data folder.</summary>
    public const string AppFolderName = "CycloneBattery";

    /// <summary>Root of the application data folder.</summary>
    public static string RootDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    /// <summary>Path of <c>settings.json</c>.</summary>
    public static string SettingsFilePath => Path.Combine(RootDirectory, "settings.json");

    /// <summary>Folder that holds rolling daily log files and saved diagnostics.</summary>
    public static string LogDirectory => Path.Combine(RootDirectory, "logs");

    /// <summary>Builds a timestamped diagnostics file name inside <see cref="LogDirectory"/>.</summary>
    public static string DiagnosticFilePath(DateTimeOffset? timestamp = null)
    {
        DateTimeOffset at = timestamp ?? DateTimeOffset.Now;
        return Path.Combine(LogDirectory, $"diagnostic-{at:yyyyMMdd-HHmmss}.txt");
    }

    /// <summary>Creates the data folders if they are missing. Safe to call repeatedly.</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogDirectory);
    }
}
