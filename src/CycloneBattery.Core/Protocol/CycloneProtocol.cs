namespace CycloneBattery.Core.Protocol;

/// <summary>
/// Every constant that describes the GameSir Cyclone 2 vendor HID protocol.
/// </summary>
/// <remarks>
/// Sources (see <c>docs/PROTOCOL_NOTES.md</c> for the full derivation):
///   * https://github.com/vdemonchy/cyclone2-linux/blob/main/docs/protocol.md
///   * https://github.com/NaokoAF/InFract/blob/main/InFract/Drivers/GameSir/Cyclone2Notes.md
/// </remarks>
public static class CycloneProtocol
{
    /// <summary>USB Vendor ID of the Cyclone 2 in XInput/Xbox mode.</summary>
    public const int VendorId = 0x3537;

    /// <summary>USB Product ID of the Cyclone 2 in XInput/Xbox mode.</summary>
    public const int ProductId = 0x100B;

    /// <summary>
    /// USB Product ID the 2.4 GHz dongle reports while no controller is active
    /// ("HID mode", indicator hidden). Never carries a battery value.
    /// </summary>
    public const int DongleIdleProductId = 0x0575;

    /// <summary>OUTPUT report id used for every command sent to the controller.</summary>
    public const byte OutputReportId = 0x0F;

    /// <summary>INPUT report id that carries the live controller status, including battery.</summary>
    public const byte StatusReportId = 0x12;

    /// <summary>INPUT report id used for command/event replies. It does NOT carry battery.</summary>
    public const byte EventReportId = 0x10;

    /// <summary>
    /// Opcode of the heartbeat GameSir Connect sends roughly once per second to keep the
    /// extended <see cref="StatusReportId"/> stream enabled.
    /// </summary>
    public const byte HeartbeatOpcode = 0xF2;

    /// <summary>
    /// Opcode of the secondary wake command (<c>0F 03 00 00 ...</c>, a zero-length register
    /// write with no payload). It reliably starts the <see cref="StatusReportId"/> stream and
    /// is used only as a fallback when the heartbeat produced nothing.
    /// </summary>
    public const byte WakeOpcode = 0x03;

    /// <summary>
    /// Report length the controller was observed to use (including the report id byte).
    /// Used only as a fallback when a HID stack reports no usable output report length.
    /// </summary>
    public const int DefaultOutputReportLength = 64;

    /// <summary>Report length observed for <see cref="StatusReportId"/> frames (including the report id byte).</summary>
    public const int DefaultStatusReportLength = 64;
}
