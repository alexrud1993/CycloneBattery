using CycloneBattery.Core.Models;

namespace CycloneBattery.Core.Hid;

/// <summary>Opens a previously enumerated candidate as an <see cref="ICycloneHidTransport"/>.</summary>
public interface ICycloneHidTransportFactory
{
    /// <summary>
    /// Attempts to open <paramref name="candidate"/>.
    /// </summary>
    /// <param name="candidate">The interface to open.</param>
    /// <param name="transport">The open transport, or <see langword="null"/> when opening failed.</param>
    /// <returns>The outcome, including a sanitized error description on failure.</returns>
    TransportOpenResult TryOpen(HidDeviceCandidate candidate, out ICycloneHidTransport? transport);
}

/// <summary>Outcome of a single open attempt.</summary>
/// <param name="Success">Whether the interface was opened.</param>
/// <param name="Error">Short, sanitized error text when <paramref name="Success"/> is <see langword="false"/>.</param>
/// <param name="LooksBusy">
/// <see langword="true"/> when the failure indicates another process holds the interface.
/// </param>
public sealed record TransportOpenResult(bool Success, string? Error, bool LooksBusy)
{
    /// <summary>Successful open.</summary>
    public static TransportOpenResult Opened() => new(true, null, false);

    /// <summary>Failed open.</summary>
    public static TransportOpenResult Failed(string error, bool looksBusy) => new(false, error, looksBusy);
}
