using HidSharp;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;

namespace CycloneBattery.Core.Hid;

/// <summary>
/// <see cref="IHidDeviceDiscovery"/> / <see cref="ICycloneHidTransportFactory"/> implementation
/// backed by HidSharp.
/// </summary>
/// <remarks>
/// Every HidSharp call that can fail is wrapped: a device that is present but owned by another
/// program (typically GameSir Connect) must degrade into a <c>Busy</c> state, never into a crash.
/// Device paths are scrubbed out of exception text before it leaves this class.
/// </remarks>
public sealed class HidSharpDeviceDiscovery : IHidDeviceDiscovery, ICycloneHidTransportFactory
{
    /// <inheritdoc />
    public IReadOnlyList<HidDeviceCandidate> EnumerateCandidates()
    {
        var candidates = new List<HidDeviceCandidate>();

        foreach (HidDevice device in SafeGetHidDevices(CycloneProtocol.VendorId, CycloneProtocol.ProductId))
        {
            candidates.Add(ToCandidate(device));
        }

        return candidates;
    }

    /// <inheritdoc />
    public ControllerIdentity DetectIdentity()
    {
        ControllerIdentity best = ControllerIdentity.None;

        foreach (HidDevice device in SafeGetHidDevices(null, null))
        {
            ControllerIdentity identity = ControllerIdentityExtensions.FromUsb(
                SafeValue(() => device.VendorID),
                SafeValue(() => device.ProductID));

            best = Stronger(best, identity);

            if (best == ControllerIdentity.XInput)
            {
                // The supported identity wins outright; no need to keep scanning.
                return best;
            }
        }

        return best;
    }

    /// <inheritdoc />
    public TransportOpenResult TryOpen(HidDeviceCandidate candidate, out ICycloneHidTransport? transport)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        transport = null;

        HidDevice? device = null;
        try
        {
            device = SafeGetHidDevices(CycloneProtocol.VendorId, CycloneProtocol.ProductId)
                .FirstOrDefault(d => string.Equals(d.DevicePath, candidate.DevicePath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (IsExpectedHidFailure(ex))
        {
            return TransportOpenResult.Failed(Scrub(ex, candidate.DevicePath), false);
        }

        if (device is null)
        {
            return TransportOpenResult.Failed("device path is no longer present", false);
        }

        HidStream stream;
        try
        {
            stream = device.Open();
        }
        catch (UnauthorizedAccessException ex)
        {
            // Access denied: another process (usually GameSir Connect) owns the interface.
            return TransportOpenResult.Failed(Scrub(ex, candidate.DevicePath), true);
        }
        catch (IOException ex)
        {
            return TransportOpenResult.Failed(Scrub(ex, candidate.DevicePath), LooksBusy(ex));
        }
        catch (Exception ex) when (IsExpectedHidFailure(ex))
        {
            return TransportOpenResult.Failed(Scrub(ex, candidate.DevicePath), false);
        }

        int maxInput = SafeValue(() => device.GetMaxInputReportLength());
        int maxOutput = SafeValue(() => device.GetMaxOutputReportLength());

        transport = new HidSharpTransport(candidate, stream, maxInput, maxOutput);
        return TransportOpenResult.Opened();
    }

    private static HidDeviceCandidate ToCandidate(HidDevice device) => new(
        DevicePath: device.DevicePath ?? "",
        VendorId: SafeValue(() => device.VendorID),
        ProductId: SafeValue(() => device.ProductID),
        MaxInputReportLength: SafeValue(() => device.GetMaxInputReportLength()),
        MaxOutputReportLength: SafeValue(() => device.GetMaxOutputReportLength()),
        ProductName: SafeString(() => device.GetProductName()),
        Manufacturer: SafeString(() => device.GetManufacturer()));

    private static IEnumerable<HidDevice> SafeGetHidDevices(int? vendorId, int? productId)
    {
        try
        {
            return vendorId is null && productId is null
                ? DeviceList.Local.GetHidDevices().ToList()
                : DeviceList.Local.GetHidDevices(vendorId, productId).ToList();
        }
        catch (Exception ex) when (IsExpectedHidFailure(ex))
        {
            // No usable HID stack, or the device vanished mid-enumeration.
            return Array.Empty<HidDevice>();
        }
    }

    private static int SafeValue(Func<int> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (IsExpectedHidFailure(ex))
        {
            return 0;
        }
    }

    private static string? SafeString(Func<string?> read)
    {
        try
        {
            string? value = read();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (Exception ex) when (IsExpectedHidFailure(ex))
        {
            return null;
        }
    }

    private static bool IsExpectedHidFailure(Exception ex) => ex switch
    {
        IOException => true,
        UnauthorizedAccessException => true,
        NotSupportedException => true,
        PlatformNotSupportedException => true,
        InvalidOperationException => true,
        ObjectDisposedException => true,
        TimeoutException => true,
        _ => false,
    };

    /// <summary>
    /// Heuristic for the Windows messages that mean "another program has this device open".
    /// </summary>
    internal static bool LooksBusy(Exception ex)
    {
        if (ex is UnauthorizedAccessException)
        {
            return true;
        }

        string message = ex.Message ?? "";
        return message.Contains("another process", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("access denied", StringComparison.OrdinalIgnoreCase)
            || message.Contains("sharing violation", StringComparison.OrdinalIgnoreCase)
            || message.Contains("being used by", StringComparison.OrdinalIgnoreCase);
    }

    private static string Scrub(Exception ex, string? devicePath)
    {
        string text = $"{ex.GetType().Name}: {ex.Message}";
        if (!string.IsNullOrWhiteSpace(devicePath))
        {
            text = text.Replace(devicePath, HidDevicePathSanitizer.Sanitize(devicePath), StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    private static ControllerIdentity Stronger(ControllerIdentity current, ControllerIdentity candidate)
    {
        return Rank(candidate) > Rank(current) ? candidate : current;

        static int Rank(ControllerIdentity identity) => identity switch
        {
            ControllerIdentity.XInput => 6,
            ControllerIdentity.DongleIdle => 5,
            ControllerIdentity.DualShock4 => 4,
            ControllerIdentity.SwitchPro => 3,
            ControllerIdentity.OtherGameSir => 2,
            _ => 0,
        };
    }
}
