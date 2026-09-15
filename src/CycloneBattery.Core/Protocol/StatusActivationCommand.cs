namespace CycloneBattery.Core.Protocol;

/// <summary>
/// Builds the only OUTPUT reports this application is ever allowed to send.
/// </summary>
/// <remarks>
/// Both commands are pure status activation:
/// <list type="bullet">
///   <item><description>heartbeat: <c>0F F2 00 00 ...</c></description></item>
///   <item><description>wake fallback: <c>0F 03 00 00 ...</c> (zero-length register write, no payload)</description></item>
/// </list>
/// No profile, register-with-data, RGB, rumble, calibration or firmware command is
/// representable through this type by design.
/// </remarks>
public static class StatusActivationCommand
{
    /// <summary>
    /// Builds a report-id-prefixed heartbeat report padded to
    /// <paramref name="outputReportLength"/> bytes.
    /// </summary>
    /// <param name="outputReportLength">
    /// The HID-reported maximum output report length (which already includes the report id
    /// byte). Values below the 2 bytes actually needed fall back to
    /// <see cref="CycloneProtocol.DefaultOutputReportLength"/>.
    /// </param>
    public static byte[] BuildHeartbeat(int outputReportLength) =>
        Build(CycloneProtocol.HeartbeatOpcode, outputReportLength);

    /// <summary>
    /// Builds a report-id-prefixed wake report padded to
    /// <paramref name="outputReportLength"/> bytes.
    /// </summary>
    /// <param name="outputReportLength">
    /// The HID-reported maximum output report length (which already includes the report id
    /// byte). Values below the 2 bytes actually needed fall back to
    /// <see cref="CycloneProtocol.DefaultOutputReportLength"/>.
    /// </param>
    public static byte[] BuildWake(int outputReportLength) =>
        Build(CycloneProtocol.WakeOpcode, outputReportLength);

    /// <summary>
    /// Resolves the buffer length that should be used for output reports: the
    /// HID-reported length when it is usable, otherwise the observed default of 64.
    /// </summary>
    public static int ResolveOutputReportLength(int reportedOutputReportLength) =>
        reportedOutputReportLength >= 2 ? reportedOutputReportLength : CycloneProtocol.DefaultOutputReportLength;

    private static byte[] Build(byte opcode, int outputReportLength)
    {
        var length = ResolveOutputReportLength(outputReportLength);
        var report = new byte[length];
        report[0] = CycloneProtocol.OutputReportId;
        report[1] = opcode;
        // Remaining bytes stay zero: a zero-padded, payload-free status activation.
        return report;
    }
}
