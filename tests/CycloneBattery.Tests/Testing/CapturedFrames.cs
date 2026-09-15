namespace CycloneBattery.Tests.Testing;

/// <summary>
/// Real <c>0x12</c> frames captured upstream, plus helpers for building frames in tests.
/// </summary>
/// <remarks>
/// <para>
/// The two captures below are reproduced from
/// https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md and were zero-padded to
/// the documented 64-byte report length. They are used to lock the raw indexing down: in both
/// frames <c>byte[0] = 0x12</c> (the report id is part of the buffer) and
/// <c>byte[36] = 0x64</c> = 100%.
/// </para>
/// <para>
/// Note that both captures show <c>byte[35] = 0x00</c> and predate the 2026-06-05 confirmation of
/// the cable flag, so they are only used for <b>index</b> verification here. The cable flag itself
/// is covered by synthetic frames and must additionally be confirmed on real hardware
/// (see <c>docs/HARDWARE_TEST.md</c>).
/// </para>
/// </remarks>
public static class CapturedFrames
{
    /// <summary>Captured frame: controller plugged in, full charge (opcode 0x03 wake).</summary>
    public const string PluggedFullHex =
        "12808080800F00000000FD6500FEFF00001000E9FF44203F00000000000000000000000064000000000000000000000000000000000000000000000000000000";

    /// <summary>Captured frame: controller on battery, full charge (opcode 0x03 wake).</summary>
    public const string OnBatteryFullHex =
        "12808080800F00000000ED0D00FEFF00000E00A5009B20F9FD000000000000000000000064000101180000000000000000000000000000000000000000000000";

    /// <summary>The captured "plugged, full" frame as bytes.</summary>
    public static byte[] PluggedFull { get; } = FromHex(PluggedFullHex);

    /// <summary>The captured "on battery, full" frame as bytes.</summary>
    public static byte[] OnBatteryFull { get; } = FromHex(OnBatteryFullHex);

    /// <summary>Parses an upper- or lower-case hex string into bytes.</summary>
    public static byte[] FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        if (hex.Length % 2 != 0)
        {
            throw new ArgumentException("Hex string must have an even length.", nameof(hex));
        }

        return Convert.FromHexString(hex);
    }

    /// <summary>
    /// Builds a well-formed <c>0x12</c> status frame.
    /// </summary>
    /// <param name="batteryPercent">Value written to absolute index 36.</param>
    /// <param name="cableConnected">Value written to absolute index 35.</param>
    /// <param name="notTheChargingFlag">Value written to absolute index 37 (must be ignored).</param>
    /// <param name="length">Total frame length including the report id byte.</param>
    public static byte[] StatusFrame(
        int batteryPercent,
        bool cableConnected = false,
        byte notTheChargingFlag = 0x00,
        int length = 64)
    {
        var frame = new byte[length];
        frame[0] = 0x12;
        frame[35] = (byte)(cableConnected ? 0x01 : 0x00);
        frame[36] = (byte)batteryPercent;

        if (length > 37)
        {
            frame[37] = notTheChargingFlag;
        }

        return frame;
    }

    /// <summary>Builds a <c>0x10</c> command/event reply, which must never be treated as battery data.</summary>
    public static byte[] EventFrame(int length = 64)
    {
        var frame = new byte[length];
        frame[0] = 0x10;
        frame[1] = 0x06;
        return frame;
    }
}
