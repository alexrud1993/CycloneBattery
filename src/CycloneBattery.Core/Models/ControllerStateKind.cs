namespace CycloneBattery.Core.Models;

/// <summary>
/// Explicit controller state, used instead of a pile of booleans.
/// </summary>
public enum ControllerStateKind
{
    /// <summary>No usable active Cyclone 2 (dongle absent, controller off, or identity not present).</summary>
    Disconnected = 0,

    /// <summary>A matching interface was found and is being validated / is not streaming status yet.</summary>
    Connecting,

    /// <summary>A valid <c>0x12</c> status report was received; battery data is live.</summary>
    Connected,

    /// <summary>A Cyclone 2 is present but in a mode this application does not read battery from.</summary>
    UnsupportedMode,

    /// <summary>The device exists but cannot be opened, most likely another program owns it.</summary>
    Busy,

    /// <summary>Unexpected non-fatal transport or protocol problem. Recovery is automatic.</summary>
    Error,
}

/// <summary>Helpers for <see cref="ControllerStateKind"/>.</summary>
public static class ControllerStateKindExtensions
{
    /// <summary><see langword="true"/> when the state represents a live battery source.</summary>
    public static bool IsConnected(this ControllerStateKind kind) => kind == ControllerStateKind.Connected;

    /// <summary><see langword="true"/> when the application should keep retrying automatically.</summary>
    public static bool ShouldKeepScanning(this ControllerStateKind kind) =>
        kind is ControllerStateKind.Disconnected
            or ControllerStateKind.Connecting
            or ControllerStateKind.Busy
            or ControllerStateKind.Error
            or ControllerStateKind.UnsupportedMode;
}
