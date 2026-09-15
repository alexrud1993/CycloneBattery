namespace CycloneBattery.Core.Services;

/// <summary>
/// Shows user notifications. Implemented in the App layer with a Windows mechanism that works
/// for an unpackaged WPF application.
/// </summary>
public interface INotificationService
{
    /// <summary>Whether notifications can actually be displayed right now.</summary>
    bool IsAvailable { get; }

    /// <summary>Shows the low-battery alert.</summary>
    bool ShowLowBattery(int batteryPercent, int thresholdPercent);

    /// <summary>Shows a confirmation notification, used by the "Test notification" action.</summary>
    bool ShowTestNotification();

    /// <summary>Shows a generic informational notification.</summary>
    bool ShowMessage(string title, string message);
}
