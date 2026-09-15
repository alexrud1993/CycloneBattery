using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;

namespace CycloneBattery.Core.Services;

/// <summary>
/// Owns the background loop that keeps controller state current.
/// </summary>
/// <remarks>
/// Everything here runs off the UI thread. UI code only subscribes to
/// <see cref="StateChanged"/> / <see cref="ReadingUpdated"/> and marshals to the dispatcher.
/// </remarks>
public interface IControllerStateService : IAsyncDisposable
{
    /// <summary>The current state. Never <see langword="null"/>.</summary>
    ControllerState State { get; }

    /// <summary>The most recent valid reading, if any was ever received.</summary>
    BatteryReading? LastKnownReading { get; }

    /// <summary>Raised on a background thread whenever the state changes.</summary>
    event EventHandler<ControllerState>? StateChanged;

    /// <summary>Raised on a background thread for every valid battery reading.</summary>
    event EventHandler<BatteryReading>? ReadingUpdated;

    /// <summary>Starts the background loop.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the background loop and releases the HID interface.</summary>
    Task StopAsync();

    /// <summary>Drops the current interface and forces an immediate rediscovery.</summary>
    Task ForceReconnectAsync();

    /// <summary>
    /// Runs exactly one discovery/validation pass (or one connected read pass) and returns the
    /// resulting state. Used by the tray "Refresh" action, by <c>--diagnostics</c> and by tests.
    /// </summary>
    Task<ControllerState> RefreshNowAsync(CancellationToken cancellationToken = default);
}
