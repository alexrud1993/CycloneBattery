namespace CycloneBattery.Core.Protocol;

/// <summary>
/// A validated battery sample decoded from a <c>0x12</c> controller status report.
/// </summary>
/// <param name="BatteryPercent">Battery charge in percent, guaranteed to be within <c>0..100</c>.</param>
/// <param name="CableConnected">
/// <see langword="true"/> when the charging cable / external power is connected
/// (raw cable flag <c>0x01</c>), <see langword="false"/> when running on battery (<c>0x00</c>).
/// </param>
/// <param name="ReceivedAtUtc">When the frame was received. Stamped by the caller, not by the parser.</param>
/// <param name="ReportLength">Total length of the frame that produced this reading, including the report id byte.</param>
public readonly record struct BatteryReading(
    int BatteryPercent,
    bool CableConnected,
    DateTimeOffset ReceivedAtUtc,
    int ReportLength)
{
    /// <summary>Returns a copy stamped with <paramref name="receivedAtUtc"/>.</summary>
    public BatteryReading At(DateTimeOffset receivedAtUtc) => this with { ReceivedAtUtc = receivedAtUtc };

    /// <summary>Short human readable form, e.g. <c>73% (on battery, 64B)</c>.</summary>
    public string Describe() =>
        $"{BatteryPercent}% ({(CableConnected ? "cable/power connected" : "on battery")}, {ReportLength}B)";
}
