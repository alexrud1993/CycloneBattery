using System.Text.Json;
using System.Text.Json.Serialization;
using CycloneBattery.Core.Logging;

namespace CycloneBattery.Core.Settings;

/// <summary>Loads and persists <see cref="AppSettings"/>.</summary>
public interface IAppSettingsService
{
    /// <summary>The settings currently in effect. Never <see langword="null"/>.</summary>
    AppSettings Current { get; }

    /// <summary>
    /// Reads settings from the store. A missing or corrupt file yields the defaults instead of
    /// throwing.
    /// </summary>
    AppSettings Load();

    /// <summary>Persists <paramref name="settings"/> and makes them <see cref="Current"/>.</summary>
    void Save(AppSettings settings);

    /// <summary>Where the settings are stored. Used by diagnostics.</summary>
    string Location { get; }

    /// <summary>Raised after a successful <see cref="Save"/>.</summary>
    event EventHandler<AppSettings>? SettingsChanged;
}

/// <summary>JSON-backed <see cref="IAppSettingsService"/>.</summary>
public sealed class JsonAppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly ISettingsStore _store;
    private readonly ILogSink _log;
    private readonly object _gate = new();
    private AppSettings _current;

    public JsonAppSettingsService(ISettingsStore store, ILogSink? log = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _log = log ?? NullLogSink.Instance;
        _current = Load();
    }

    /// <inheritdoc />
    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public string Location => _store.Location;

    /// <inheritdoc />
    public event EventHandler<AppSettings>? SettingsChanged;

    /// <inheritdoc />
    public AppSettings Load()
    {
        string? text;
        try
        {
            text = _store.ReadAllText();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            _log.Warning(nameof(JsonAppSettingsService), $"Could not read settings, using defaults: {ex.Message}");
            return RememberDefaults();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return RememberDefaults();
        }

        try
        {
            AppSettings? parsed = JsonSerializer.Deserialize<AppSettings>(text, SerializerOptions);
            if (parsed is null)
            {
                _log.Warning(nameof(JsonAppSettingsService), "Settings file was empty JSON, using defaults");
                return RememberDefaults();
            }

            parsed.Normalize();
            lock (_gate)
            {
                _current = parsed;
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            // Corrupt or hand-edited file: fall back safely instead of refusing to start.
            _log.Warning(nameof(JsonAppSettingsService), $"Settings file is corrupt, using defaults: {ex.Message}");
            return RememberDefaults();
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        _store.WriteAllText(json);

        lock (_gate)
        {
            _current = settings;
        }

        _log.Info(nameof(JsonAppSettingsService), "Settings saved");
        SettingsChanged?.Invoke(this, settings);
    }

    private AppSettings RememberDefaults()
    {
        AppSettings defaults = AppSettings.CreateDefault();
        lock (_gate)
        {
            _current = defaults;
        }

        return defaults;
    }
}
