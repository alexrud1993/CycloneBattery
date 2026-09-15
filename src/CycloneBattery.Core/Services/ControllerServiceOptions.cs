using CycloneBattery.Core.Hid;

namespace CycloneBattery.Core.Services;

/// <summary>Timing configuration for <see cref="ControllerStateService"/>.</summary>
/// <remarks>
/// Defaults follow the technical spec: immediate scan at startup, roughly 2 s rescans while
/// disconnected, a ~1 s heartbeat while connected, and no busy polling anywhere.
/// </remarks>
public sealed record ControllerServiceOptions
{
    /// <summary>How often the <c>0x0F / 0xF2</c> heartbeat is sent while connected.</summary>
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Delay between discovery passes while no controller is present.</summary>
    public TimeSpan DisconnectedScanInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Delay between attempts while another program owns the interface.</summary>
    public TimeSpan BusyRetryInterval { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Delay after a transport error before retrying.</summary>
    public TimeSpan ErrorRetryInterval { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>Delay between attempts while an unsupported mode is attached.</summary>
    public TimeSpan UnsupportedModeRetryInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Delay between attempts while a matching interface is still being validated.</summary>
    public TimeSpan ConnectingRetryInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>A connected reading older than this expires into disconnected.</summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(12);

    /// <summary>Blocking slice used while waiting for an input report.</summary>
    public int ReadSliceMilliseconds { get; init; } = 25;

    /// <summary>Timings used while validating candidate interfaces.</summary>
    public ProbeOptions Probe { get; init; } = ProbeOptions.Default;

    /// <summary>Defaults used by the application.</summary>
    public static ControllerServiceOptions Default { get; } = new();
}
