namespace CycloneBattery.Core.Protocol;

/// <summary>
/// Why <see cref="BatteryFrameParser"/> refused to turn a frame into a reading.
/// </summary>
/// <remarks>
/// A rejected frame never produces a fake value: the caller keeps the last good
/// reading (if any) and the reason is surfaced in diagnostics and the log.
/// </remarks>
public enum ParseFailureReason
{
    /// <summary>Not a failure — the frame parsed successfully.</summary>
    None = 0,

    /// <summary>The frame was empty (length 0).</summary>
    Empty,

    /// <summary>
    /// The report id was not <c>0x12</c>. Most commonly the <c>0x10</c> command/event
    /// reply, whose battery byte position is meaningless.
    /// </summary>
    WrongReportId,

    /// <summary>The frame is too short to contain the battery byte at absolute index 36.</summary>
    TooShort,

    /// <summary>
    /// The cable flag at absolute index 35 was neither <c>0x00</c> nor <c>0x01</c>.
    /// </summary>
    InvalidCableFlag,

    /// <summary>The battery byte at absolute index 36 was greater than 100.</summary>
    BatteryOutOfRange,
}
