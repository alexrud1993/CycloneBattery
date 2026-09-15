using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CycloneBattery.Core.Models;
using MediaColor = System.Windows.Media.Color;

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
    private static readonly Brush HealthyBrush = Freeze(new SolidColorBrush(MediaColor.FromRgb(76, 175, 80)));
    private static readonly Brush MediumBrush = Freeze(new SolidColorBrush(MediaColor.FromRgb(255, 193, 7)));
    private static readonly Brush LowBrush = Freeze(new SolidColorBrush(MediaColor.FromRgb(244, 67, 54)));
    private static readonly Brush ChargingBrush = Freeze(new SolidColorBrush(MediaColor.FromRgb(33, 150, 243)));
    private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(MediaColor.FromRgb(120, 120, 120)));

    private ControllerState _state = ControllerState.Disconnected("Starting");
    private int _lowBatteryThresholdPercent = 20;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string BatteryText => _state.HasBattery ? _state.BatteryPercent + "%" : "—";
    public double ProgressValue => _state.BatteryPercent ?? 0;

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

    public bool ShowCharging => _state.CableConnected == true && _state.HasBattery;

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

    public ControllerState State => _state;

    public void Update(ControllerState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        OnPropertyChanged(nameof(BatteryText));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ShowCharging));
        OnPropertyChanged(nameof(ProgressBrush));
    }

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
