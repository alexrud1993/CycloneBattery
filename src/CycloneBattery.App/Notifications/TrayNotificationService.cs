using CycloneBattery.App.Tray;
using CycloneBattery.Core.Services;

namespace CycloneBattery.App.Notifications;

/// <summary>
/// <see cref="INotificationService"/> backed by the tray icon's balloon notification.
/// </summary>
/// <remarks>
/// A balloon (rather than a registered toast) is used deliberately: it works reliably for an
/// unpackaged WPF executable without an AppUserModelID registration, which the technical spec
/// explicitly allows for the MVP.
/// </remarks>
public sealed class TrayNotificationService : INotificationService
{
    private readonly TrayIconController _tray;

    public TrayNotificationService(TrayIconController tray) =>
        _tray = tray ?? throw new ArgumentNullException(nameof(tray));

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public bool ShowLowBattery(int batteryPercent, int thresholdPercent) =>
        ShowMessage("Cyclone 2 battery low", $"{batteryPercent}% remaining — alert threshold is {thresholdPercent}%.");

    /// <inheritdoc />
    public bool ShowTestNotification() =>
        ShowMessage("Cyclone Battery", "Test notification: notifications are working.");

    /// <inheritdoc />
    public bool ShowMessage(string title, string message)
    {
        try
        {
            _tray.ShowBalloon(title, message);
            return true;
        }
        catch (Exception)
        {
            // A failed notification must never break the monitoring loop.
            return false;
        }
    }
}
