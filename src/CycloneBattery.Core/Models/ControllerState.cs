using CycloneBattery.Core.Protocol;

namespace CycloneBattery.Core.Models;

/// <summary>
/// An immutable snapshot of everything the UI needs to know about the controller.
/// </summary>
public sealed record ControllerState
{
    /// <summary>The state itself.</summary>
    public required ControllerStateKind Kind { get; init; }

    /// <summary>Battery percentage (<c>0..100</c>), or <see langword="null"/> when unknown.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>Cable / external power state, or <see langword="null"/> when unknown.</summary>
    public bool? CableConnected { get; init; }

    /// <summary>When the battery value was last refreshed from a valid <c>0x12</c> report.</summary>
    public DateTimeOffset? LastUpdated { get; init; }

    /// <summary>Sanitized identifier of the interface currently in use. Internal/log use only.</summary>
    public string? DevicePathId { get; init; }

    /// <summary>Optional human readable detail (never a raw device path).</summary>
    public string? Message { get; init; }

    /// <summary><see langword="true"/> when <see cref="BatteryPercent"/> holds a usable value.</summary>
    public bool HasBattery => Kind.IsConnected() && BatteryPercent is >= 0 and <= 100;

    /// <summary><see langword="true"/> when the controller is running on its own battery.</summary>
    public bool IsOnBattery => HasBattery && CableConnected == false;

    /// <summary>Builds the <see cref="ControllerStateKind.Disconnected"/> state.</summary>
    public static ControllerState Disconnected(string? message = null) =>
        new() { Kind = ControllerStateKind.Disconnected, Message = message };

    /// <summary>Builds the <see cref="ControllerStateKind.Connecting"/> state.</summary>
    public static ControllerState Connecting(string? message = null, string? devicePathId = null) =>
        new() { Kind = ControllerStateKind.Connecting, Message = message, DevicePathId = devicePathId };

    /// <summary>Builds the <see cref="ControllerStateKind.Connected"/> state from a validated reading.</summary>
    public static ControllerState Connected(BatteryReading reading, string? devicePathId = null) => new()
    {
        Kind = ControllerStateKind.Connected,
        BatteryPercent = reading.BatteryPercent,
        CableConnected = reading.CableConnected,
        LastUpdated = reading.ReceivedAtUtc,
        DevicePathId = devicePathId,
    };

    /// <summary>Builds the <see cref="ControllerStateKind.UnsupportedMode"/> state.</summary>
    public static ControllerState UnsupportedMode(string message) =>
        new() { Kind = ControllerStateKind.UnsupportedMode, Message = message };

    /// <summary>Builds the <see cref="ControllerStateKind.Busy"/> state.</summary>
    public static ControllerState Busy(string message) =>
        new() { Kind = ControllerStateKind.Busy, Message = message };

    /// <summary>Builds the <see cref="ControllerStateKind.Error"/> state.</summary>
    public static ControllerState Error(string message) =>
        new() { Kind = ControllerStateKind.Error, Message = message };
}
