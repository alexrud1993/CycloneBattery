using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CycloneBattery.Core.Models;

namespace CycloneBattery.App.UI;

/// <summary>
/// Presentation state for the mini widget.
/// </summary>
/// <remarks>
/// Intentionally tiny: it exposes formatted strings and a brush, so the XAML stays declarative
/// and no HID or protocol detail leaks into the view.
/// </remarks>
public sealed class WidgetViewModel : INotifyPropertyChanged
{
    private static readonly Brush HealthyBrush = Freeze(new SolidColorBrush(Color.FromRgb(76, 175, 80)));
    private static readonly Brush MediumBrush = Freeze(new SolidColorBrush(Color.FromRgb(255, 193, 7)));
    private static readonly Brush LowBrush = Freeze(new SolidColorBrush(Color.FromRgb(244, 67, 54)));
    private static readonly Brush ChargingBrush = Freeze(new SolidColorBrush(Color.FromRgb(33, 150, 243)));
    private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(120, 120, 120)));

    private ControllerState _state = ControllerState.Disconnected("Starting");
    private int _lowBatteryThresholdPercent = 20;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Battery text, or an em dash when the value is unknown.</summary>
    public string BatteryText => _state.HasBattery ? _state.BatteryPercent + "%" : "—";

    /// <summary>Progress bar value.</summary>
    public double ProgressValue => _state.BatteryPercent ?? 0;

    /// <summary>Short status line under the battery bar.</summary>
    public string StatusText
    {
        get
        {
            if (!_state.HasBattery)
            {
                return _state.Kind switch
                {
                    ControllerStateKind.Connecting => "Connecting",
                    ControllerStateKind.Busy => "Interface busy",
                    ControllerStateKind.UnsupportedMode => "Unsupported mode",
                    ControllerStateKind.Error => "Error",
                    _ => "Disconnected",
                };
            }

            return _state.CableConnected == true ? "Charging" : "On battery";
        }
    }

    /// <summary>Whether the charging indicator is visible.</summary>
    public bool ShowCharging => _state.CableConnected == true && _state.HasBattery;

    /// <summary>Progress bar colour, following the same bands as the tray icon.</summary>
    public Brush ProgressBrush
    {
        get
        {
            if (!_state.HasBattery)
            {
                return MutedBrush;
            }

            if (_state.CableConnected == true)
            {
                return ChargingBrush;
            }

            int percent = _state.BatteryPercent!.Value;
            if (percent <= _lowBatteryThresholdPercent)
            {
                return LowBrush;
            }

            return percent <= 40 ? MediumBrush : HealthyBrush;
        }
    }

    /// <summary>The underlying state, for tests and diagnostics.</summary>
    public ControllerState State => _state;

    /// <summary>Replaces the state and raises every dependent change notification.</summary>
    public void Update(ControllerState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowCharging));
        OnPropertyChanged(nameof(ProgressBrush));
    }

    /// <summary>Applies the low-battery threshold so colours match the alert setting.</summary>
    public void SetLowBatteryThreshold(int thresholdPercent)
    {
        _lowBatteryThresholdPercent = thresholdPercent;
        OnPropertyChanged(nameof(ProgressBrush));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
