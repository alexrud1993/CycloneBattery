namespace CycloneBattery.Core.Models;

/// <summary>
/// Which GameSir Cyclone 2 (or Cyclone 2 dongle) identity is currently visible on the USB bus.
/// </summary>
/// <remarks>
/// The Cyclone 2 enumerates as a different USB device per output mode. Only
/// <see cref="XInput"/> exposes the vendor status report this application reads.
/// Source: https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md
/// </remarks>
public enum ControllerIdentity
{
    /// <summary>Nothing that looks like a Cyclone 2 is present.</summary>
    None = 0,

    /// <summary><c>3537:100B</c> — XInput/Xbox mode. The only supported identity.</summary>
    XInput,

    /// <summary>
    /// <c>3537:0575</c> — the dongle's own HID identity, also what the dongle shows while the
    /// controller is switched off. No battery source.
    /// </summary>
    DongleIdle,

    /// <summary>
    /// <c>054C:09CC</c> — DualShock 4 emulation. Research shows this mode exposes no reliable
    /// live battery value, so it is reported as unsupported rather than guessed.
    /// </summary>
    DualShock4,

    /// <summary><c>057E:2009</c> — Switch Pro Controller emulation. Not supported in the MVP.</summary>
    SwitchPro,

    /// <summary>Some other device with the GameSir vendor id.</summary>
    OtherGameSir,
}

/// <summary>Helpers for <see cref="ControllerIdentity"/>.</summary>
public static class ControllerIdentityExtensions
{
    /// <summary>Classifies a USB VID/PID pair.</summary>
    public static ControllerIdentity FromUsb(int vendorId, int productId)
    {
        return (vendorId, productId) switch
        {
            (0x3537, 0x100B) => ControllerIdentity.XInput,
            (0x3537, 0x0575) => ControllerIdentity.DongleIdle,
            (0x3537, _) => ControllerIdentity.OtherGameSir,
            (0x054C, 0x09CC) => ControllerIdentity.DualShock4,
            (0x057E, 0x2009) => ControllerIdentity.SwitchPro,
            _ => ControllerIdentity.None,
        };
    }

    /// <summary><see langword="true"/> when this identity can provide a battery reading.</summary>
    public static bool IsSupported(this ControllerIdentity identity) => identity == ControllerIdentity.XInput;

    /// <summary>Text shown to the user for an unsupported identity.</summary>
    public static string Describe(this ControllerIdentity identity) => identity switch
    {
        ControllerIdentity.None => "No GameSir Cyclone 2 detected",
        ControllerIdentity.XInput => "Cyclone 2 (XInput mode)",
        ControllerIdentity.DongleIdle => "Dongle present, controller not active",
        ControllerIdentity.DualShock4 => "Cyclone 2 in DS4 mode (battery not supported)",
        ControllerIdentity.SwitchPro => "Cyclone 2 in Switch mode (not supported)",
        ControllerIdentity.OtherGameSir => "GameSir device in an unrecognized mode",
        _ => "Unknown",
    };
}
