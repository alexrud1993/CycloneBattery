using CycloneBattery.Core.Protocol;
using CycloneBattery.Core.Services;
using CycloneBattery.Core.Settings;

namespace CycloneBattery.Core.State;

/// <summary>What <see cref="BatteryAlertMonitor.Process"/> decided to do.</summary>
public enum LowBatteryAlertDecision
{
    /// <summary>No notification.</summary>
    None = 0,

    /// <summary>The battery just crossed the threshold; notify now.</summary>
    Fire,
}

/// <summary>
/// Decides when a low-battery notification should fire, with hysteresis so the user is never
/// spammed while the charge hovers around the threshold.
/// </summary>
/// <remarks>
/// Rules implemented:
/// <list type="bullet">
///   <item><description>one alert per discharge event;</description></item>
///   <item><description>re-arm only after the battery rises above threshold + hysteresis, or
///   charging starts;</description></item>
///   <item><description>never alert while the cable is connected;</description></item>
///   <item><description>never alert when the battery is unknown (there is no reading at
///   all).</description></item>
/// </list>
/// </remarks>
public sealed class BatteryAlertMonitor
{
    private readonly IClock _clock;
    private readonly object _gate = new();

    private bool _armed = true;
    private bool _enabled = true;
    private int _thresholdPercent = AppSettings.DefaultLowBatteryThresholdPercent;
    private int _hysteresisPercent = AppSettings.LowBatteryHysteresisPercent;
    private DateTimeOffset? _lastFiredAt;

    public BatteryAlertMonitor(IClock? clock = null) => _clock = clock ?? new SystemClock();

    /// <summary>Raised immediately before an alert is returned as <see cref="LowBatteryAlertDecision.Fire"/>.</summary>
    public event EventHandler<LowBatteryAlertEventArgs>? AlertRaised;

    /// <summary>Applies the user's settings.</summary>
    public void Configure(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Configure(settings.LowBatteryThresholdPercent, AppSettings.LowBatteryHysteresisPercent, settings.LowBatteryAlertEnabled);
    }

    /// <summary>Applies individual alert settings.</summary>
    public void Configure(int thresholdPercent, int hysteresisPercent, bool enabled)
    {
        lock (_gate)
        {
            _thresholdPercent = Math.Clamp(thresholdPercent, 1, 100);
            _hysteresisPercent = Math.Clamp(hysteresisPercent, 0, 99);
            _enabled = enabled;

            if (!enabled)
            {
                // While disabled nothing can fire, so stay ready for the next real crossing.
                _armed = true;
            }
        }
    }

    /// <summary>Feeds one validated reading in and returns whether a notification is due.</summary>
    public LowBatteryAlertDecision Process(BatteryReading reading)
    {
        EventHandler<LowBatteryAlertEventArgs>? handler;
        LowBatteryAlertDecision decision;
        int threshold;

        lock (_gate)
        {
            threshold = _thresholdPercent;

            if (!_enabled)
            {
                _armed = true;
                return LowBatteryAlertDecision.None;
            }

            if (reading.CableConnected)
            {
                // Charging: never notify, and get ready for the next discharge cycle.
                _armed = true;
                return LowBatteryAlertDecision.None;
            }

            if (reading.BatteryPercent <= _thresholdPercent)
            {
                if (!_armed)
                {
                    return LowBatteryAlertDecision.None;
                }

                _armed = false;
                _lastFiredAt = _clock.UtcNow;
                decision = LowBatteryAlertDecision.Fire;
            }
            else if (reading.BatteryPercent > _thresholdPercent + _hysteresisPercent)
            {
                _armed = true;
                return LowBatteryAlertDecision.None;
            }
            else
            {
                return LowBatteryAlertDecision.None;
            }
        }

        handler = AlertRaised;
        handler?.Invoke(this, new LowBatteryAlertEventArgs(reading.BatteryPercent, threshold));
        return decision;
    }

    /// <summary>Forgets any pending discharge event, e.g. after a forced reconnect.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _armed = true;
        }
    }

    /// <summary>When the last alert fired, for diagnostics.</summary>
    public DateTimeOffset? LastFiredAt
    {
        get
        {
            lock (_gate)
            {
                return _lastFiredAt;
            }
        }
    }
}

/// <summary>Details of a low-battery alert.</summary>
/// <param name="BatteryPercent">Battery level that triggered the alert.</param>
/// <param name="ThresholdPercent">Threshold that was crossed.</param>
public sealed record LowBatteryAlertEventArgs(int BatteryPercent, int ThresholdPercent);
