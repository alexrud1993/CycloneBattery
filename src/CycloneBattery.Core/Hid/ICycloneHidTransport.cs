using CycloneBattery.Core.Models;

namespace CycloneBattery.Core.Hid;

/// <summary>
/// An opened HID interface that can send status activation reports and read input reports.
/// </summary>
/// <remarks>
/// This is the only place the application touches a controller. Everything reachable through it
/// is read-only apart from the status heartbeat/wake, which is why the surface is this small.
/// </remarks>
public interface ICycloneHidTransport : IDisposable
{
    /// <summary>The interface this transport is bound to.</summary>
    HidDeviceCandidate Candidate { get; }

    /// <summary>HID-reported maximum input report length, including the report id byte.</summary>
    int MaxInputReportLength { get; }

    /// <summary>HID-reported maximum output report length, including the report id byte.</summary>
    int MaxOutputReportLength { get; }

    /// <summary>
    /// Sends one OUTPUT report. The first byte of <paramref name="report"/> must be the report
    /// id. Callers must only ever pass frames built by
    /// <see cref="Protocol.StatusActivationCommand"/>.
    /// </summary>
    /// <exception cref="HidTransportException">The write failed (device gone, interface busy).</exception>
    void WriteOutputReport(ReadOnlySpan<byte> report);

    /// <summary>
    /// Blocks for up to <paramref name="timeoutMilliseconds"/> waiting for one input report.
    /// </summary>
    /// <param name="timeoutMilliseconds">Maximum time to block. Must be &gt; 0.</param>
    /// <param name="frame">
    /// The raw report (report id first) when the method returns <see langword="true"/>.
    /// </param>
    /// <returns><see langword="false"/> on timeout with no data; <see langword="true"/> when a report arrived.</returns>
    /// <exception cref="HidTransportException">
    /// The read failed for a non-timeout reason, e.g. the device was unplugged.
    /// </exception>
    bool TryReadInputReport(int timeoutMilliseconds, out ReadOnlyMemory<byte> frame);
}
