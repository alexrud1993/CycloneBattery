using System.Text.Json.Serialization;

namespace CycloneBattery.Core.Settings;

/// <summary>
/// Every user-settable option. Deliberately small: the technical spec asks for no advanced
/// protocol timings to be exposed.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Thresholds offered in the UI for the low-battery alert.</summary>
    public static readonly int[] AllowedLowBatteryThresholds = [10, 15, 20, 25, 30];

    /// <summary>Default low-battery threshold, in percent.</summary>
    public const int DefaultLowBatteryThresholdPercent = 20;

    /// <summary>
    /// How many percentage points above the threshold the battery must reach before a new alert
    /// may fire. With the default threshold this re-arms at 25%.
    /// </summary>
    public const int LowBatteryHysteresisPercent = 5;

    /// <summary>Start the application when the user signs in to Windows.</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>Show the mini widget when the application starts.</summary>
    public bool ShowWidgetOnStartup { get; set; } = true;

    /// <summary>Keep the mini widget above other windows.</summary>
    public bool WidgetAlwaysOnTop { get; set; }

    /// <summary>Automatically hide the widget while the controller is not connected.</summary>
    public bool HideWidgetWhenDisconnected { get; set; } = true;

    /// <summary>Whether the widget was visible the last time the application ran.</summary>
    public bool WidgetVisible { get; set; }

    /// <summary>Persisted widget position. <see cref="double.NaN"/> means "not positioned yet".</summary>
    public double WidgetLeft { get; set; } = double.NaN;

    /// <summary>Persisted widget position. <see cref="double.NaN"/> means "not positioned yet".</summary>
    public double WidgetTop { get; set; } = double.NaN;

    /// <summary>Whether the low-battery notification is enabled.</summary>
    public bool LowBatteryAlertEnabled { get; set; } = true;

    /// <summary>Low-battery threshold in percent. Always one of <see cref="AllowedLowBatteryThresholds"/>.</summary>
    public int LowBatteryThresholdPercent { get; set; } = DefaultLowBatteryThresholdPercent;

    /// <summary>Creates a fresh instance with the documented defaults.</summary>
    public static AppSettings CreateDefault() => new();

    /// <summary>Returns an independent copy.</summary>
    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>
    /// Clamps values that could have come from a hand-edited or older settings file into range.
    /// Called on every load so the UI can trust what it reads.
    /// </summary>
    public void Normalize()
    {
        if (!AllowedLowBatteryThresholds.Contains(LowBatteryThresholdPercent))
        {
            LowBatteryThresholdPercent = DefaultLowBatteryThresholdPercent;
        }

        if (!double.IsFinite(WidgetLeft))
        {
            WidgetLeft = double.NaN;
        }

        if (!double.IsFinite(WidgetTop))
        {
            WidgetTop = double.NaN;
        }
    }

    /// <summary><see langword="true"/> when the widget has a saved position.</summary>
    [JsonIgnore]
    public bool HasSavedWidgetPosition => !double.IsNaN(WidgetLeft) && !double.IsNaN(WidgetTop);
}
