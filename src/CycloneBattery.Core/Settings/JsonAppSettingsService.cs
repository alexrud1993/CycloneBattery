using System.Text.Json;
using System.Text.Json.Serialization;
using CycloneBattery.Core.Logging;

namespace CycloneBattery.Core.Settings;

public interface IAppSettingsService
{
    AppSettings Current { get; }
    AppSettings Load();
    void Save(AppSettings settings);
    string Location { get; }
    event EventHandler<AppSettings>? SettingsChanged;
}

public sealed class JsonAppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
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

    public string Location => _store.Location;

    public event EventHandler<AppSettings>? SettingsChanged;

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
            _log.Warning(nameof(JsonAppSettingsService), $"Settings file is corrupt, using defaults: {ex.Message}");
            return RememberDefaults();
        }
    }

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
