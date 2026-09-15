namespace CycloneBattery.Core.Settings;

/// <summary>
/// Raw text access to the settings file, split out so persistence can be tested in memory.
/// </summary>
public interface ISettingsStore
{
    /// <summary>The full settings text, or <see langword="null"/> when no settings file exists.</summary>
    string? ReadAllText();

    /// <summary>Writes the full settings text, replacing whatever was there.</summary>
    void WriteAllText(string content);

    /// <summary>Path (or description) of the backing store, used in diagnostics.</summary>
    string Location { get; }
}

/// <summary><see cref="ISettingsStore"/> over a JSON file on disk.</summary>
public sealed class FileSettingsStore : ISettingsStore
{
    private readonly string _filePath;

    public FileSettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public string Location => _filePath;

    /// <inheritdoc />
    public string? ReadAllText()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        return File.ReadAllText(_filePath);
    }

    /// <inheritdoc />
    public void WriteAllText(string content)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temp file first so a crash mid-write cannot leave a truncated settings file.
        string tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, content);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
