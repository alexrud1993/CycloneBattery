namespace CycloneBattery.Core.Protocol;

/// <summary>
/// Decodes the battery fields out of a Cyclone 2 <c>0x12</c> controller status report.
/// </summary>
/// <remarks>
/// <para>
/// <b>Indexing contract.</b> The frame passed in is the raw HID report as delivered by the
/// transport, i.e. <c>frame[0]</c> is the report id byte. HidSharp buffers work exactly this
/// way: <c>HidDevice.GetMaxInputReportLength()</c> is documented as "the maximum input report
/// length, including the Report ID byte", and <c>HidStream</c> reads/writes the report id in
/// the first byte. The upstream captured frames in
/// https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md use the same
/// convention (<c>12 80 80 ... 64 ...</c> with <c>byte[36] = 0x64</c>).
/// </para>
/// <para>
/// Therefore the documented absolute offsets apply without any shift:
/// <c>byte[35]</c> = cable/power flag, <c>byte[36]</c> = battery percent.
/// <c>byte[37]</c> is deliberately never read: upstream captures show it stays <c>0x00</c>
/// across plug/unplug cycles, so it is not a charging flag.
/// </para>
/// </remarks>
public static class BatteryFrameParser
{
    /// <summary>Absolute index of the report id byte inside a raw frame.</summary>
    public const int ReportIdIndex = 0;

    /// <summary>Absolute index of the cable / charging flag (<c>0x00</c> battery, <c>0x01</c> cable).</summary>
    public const int CableFlagIndex = 35;

    /// <summary>Absolute index of the battery percentage (raw <c>0..100</c>).</summary>
    public const int BatteryPercentIndex = 36;

    /// <summary>
    /// Absolute index that must NOT be interpreted as the charging flag. Kept as a named
    /// constant so the unit test asserting this stays readable.
    /// </summary>
    public const int NotTheChargingFlagIndex = 37;

    /// <summary>
    /// Smallest frame length that still contains the battery byte, including the report id
    /// byte. Anything shorter is rejected as <see cref="ParseFailureReason.TooShort"/>.
    /// </summary>
    public const int MinimumStatusReportLength = BatteryPercentIndex + 1;

    /// <summary>
    /// Attempts to decode a battery reading.
    /// </summary>
    /// <param name="frame">Raw HID report, report id first.</param>
    /// <param name="reading">
    /// The decoded reading when the method returns <see langword="true"/>; otherwise
    /// <see langword="default"/>. <c>ReceivedAtUtc</c> is left unset and must be stamped by
    /// the caller (<see cref="BatteryReading.At"/>).
    /// </param>
    /// <param name="reason">
    /// <see cref="ParseFailureReason.None"/> on success, otherwise the exact rejection cause.
    /// </param>
    /// <returns><see langword="true"/> only for a well-formed <c>0x12</c> frame.</returns>
    public static bool TryParse(
        ReadOnlySpan<byte> frame,
        out BatteryReading reading,
        out ParseFailureReason reason)
    {
        reading = default;

        if (frame.IsEmpty)
        {
            reason = ParseFailureReason.Empty;
            return false;
        }

        if (frame[ReportIdIndex] != CycloneProtocol.StatusReportId)
        {
            reason = ParseFailureReason.WrongReportId;
            return false;
        }

        if (frame.Length < MinimumStatusReportLength)
        {
            reason = ParseFailureReason.TooShort;
            return false;
        }

        byte cableFlag = frame[CableFlagIndex];
        if (cableFlag > 1)
        {
            reason = ParseFailureReason.InvalidCableFlag;
            return false;
        }

        byte percent = frame[BatteryPercentIndex];
        if (percent > 100)
        {
            reason = ParseFailureReason.BatteryOutOfRange;
            return false;
        }

        reading = new BatteryReading(
            BatteryPercent: percent,
            CableConnected: cableFlag == 1,
            ReceivedAtUtc: default,
            ReportLength: frame.Length);

        reason = ParseFailureReason.None;
        return true;
    }

    /// <summary>Convenience overload that discards the rejection reason.</summary>
    public static bool TryParse(ReadOnlySpan<byte> frame, out BatteryReading reading) =>
        TryParse(frame, out reading, out _);

    /// <summary><see langword="true"/> when the frame carries the <c>0x12</c> report id.</summary>
    public static bool IsStatusReport(ReadOnlySpan<byte> frame) =>
        !frame.IsEmpty && frame[ReportIdIndex] == CycloneProtocol.StatusReportId;

    /// <summary>
    /// Renders the first <paramref name="count"/> bytes of a frame as upper-case hex, used by
    /// diagnostics so a pasted log is enough to re-derive the offsets.
    /// </summary>
    public static string ToHexPrefix(ReadOnlySpan<byte> frame, int count = 48)
    {
        if (frame.IsEmpty)
        {
            return "<empty>";
        }

        int take = Math.Min(count, frame.Length);
        var chars = new char[(take * 2) + take - 1];
        int pos = 0;
        for (int i = 0; i < take; i++)
        {
            if (i > 0)
            {
                chars[pos++] = ' ';
            }

            frame[i].TryFormat(chars.AsSpan(pos), out _, "X2");
            pos += 2;
        }

        string hex = new(chars, 0, pos);
        return take < frame.Length ? hex + " ..." : hex;
    }
}
