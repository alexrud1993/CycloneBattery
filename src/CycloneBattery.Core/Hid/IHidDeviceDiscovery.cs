using CycloneBattery.Core.Models;

namespace CycloneBattery.Core.Hid;

/// <summary>
/// Enumerates the HID interfaces that could belong to a GameSir Cyclone 2.
/// </summary>
public interface IHidDeviceDiscovery
{
    /// <summary>
    /// Returns every HID interface matching the Cyclone 2 XInput identity
    /// (VID <c>0x3537</c> / PID <c>0x100B</c>), or an empty list when none is present.
    /// </summary>
    /// <remarks>
    /// Implementations must not throw for devices that are present but locked by another
    /// process: enumeration itself does not need to open anything.
    /// </remarks>
    IReadOnlyList<HidDeviceCandidate> EnumerateCandidates();

    /// <summary>
    /// Returns the strongest Cyclone 2 identity currently visible on the bus, so the UI can
    /// distinguish "controller off" and "unsupported mode" from "nothing plugged in".
    /// </summary>
    ControllerIdentity DetectIdentity();
}
