using CycloneBattery.Core.Hid;

namespace CycloneBattery.Core.Models;

/// <summary>
/// One HID interface that reports the Cyclone 2 USB identity.
/// </summary>
/// <remarks>
/// The Cyclone 2 exposes several HID interfaces with the same VID/PID (XInput plus a vendor
/// interface). Only one of them streams the <c>0x12</c> status report, so every candidate has
/// to be enumerated and actively validated — never "take the first match".
/// </remarks>
public sealed record HidDeviceCandidate(
    string DevicePath,
    int VendorId,
    int ProductId,
    int MaxInputReportLength,
    int MaxOutputReportLength,
    string? ProductName,
    string? Manufacturer)
{
    /// <summary>
    /// A short non-reversible identifier for this device path, safe to write to the log and to
    /// paste into a bug report. Raw Windows device paths contain instance ids, so they are
    /// never logged directly.
    /// </summary>
    public string SanitizedId => HidDevicePathSanitizer.Sanitize(DevicePath);

    /// <summary>One-line description used by diagnostics.</summary>
    public string Describe()
    {
        string product = string.IsNullOrWhiteSpace(ProductName) ? "?" : ProductName;
        string maker = string.IsNullOrWhiteSpace(Manufacturer) ? "?" : Manufacturer;
        return
            $"vid=0x{VendorId:X4} pid=0x{ProductId:X4} in={MaxInputReportLength}B out={MaxOutputReportLength}B " +
            $"product=\"{product}\" manufacturer=\"{maker}\" id={SanitizedId}";
    }
}
