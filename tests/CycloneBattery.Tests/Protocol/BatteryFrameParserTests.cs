using CycloneBattery.Core.Protocol;
using CycloneBattery.Tests.Testing;
using Xunit;

namespace CycloneBattery.Tests.Protocol;

/// <summary>
/// Covers every parser rule from the specification, including the raw-indexing guarantee that a
/// single off-by-one cannot ship silently.
/// </summary>
public class BatteryFrameParserTests
{
    [Fact]
    public void IgnoresNonStatusReport()
    {
        byte[] frame = CapturedFrames.EventFrame();

        bool parsed = BatteryFrameParser.TryParse(frame, out BatteryReading reading, out ParseFailureReason reason);

        Assert.False(parsed);
        Assert.Equal(ParseFailureReason.WrongReportId, reason);
        Assert.Equal(default, reading);
        Assert.False(BatteryFrameParser.IsStatusReport(frame));
    }

    [Fact]
    public void RejectsEmptyFrame()
    {
        bool parsed = BatteryFrameParser.TryParse(Array.Empty<byte>(), out _, out ParseFailureReason reason);

        Assert.False(parsed);
        Assert.Equal(ParseFailureReason.Empty, reason);
    }

    [Fact]
    public void RejectsTooShortReport()
    {
        // 36 bytes reaches the cable flag at index 35 but not the battery byte at index 36.
        byte[] frame = new byte[BatteryFrameParser.MinimumStatusReportLength - 1];
        frame[0] = CycloneProtocol.StatusReportId;
        frame[35] = 0x00;

        bool parsed = BatteryFrameParser.TryParse(frame, out _, out ParseFailureReason reason);

        Assert.False(parsed);
        Assert.Equal(ParseFailureReason.TooShort, reason);
    }

    [Fact]
    public void AcceptsFrameThatIsOneByteLongerThanMinimum()
    {
        byte[] frame = CapturedFrames.StatusFrame(42, length: BatteryFrameParser.MinimumStatusReportLength);

        bool parsed = BatteryFrameParser.TryParse(frame, out BatteryReading reading, out ParseFailureReason reason);

        Assert.True(parsed);
        Assert.Equal(ParseFailureReason.None, reason);
        Assert.Equal(42, reading.BatteryPercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void ReadsValidPercentages(int percent)
    {
        byte[] frame = CapturedFrames.StatusFrame(percent);

        bool parsed = BatteryFrameParser.TryParse(frame, out BatteryReading reading, out _);

        Assert.True(parsed);
        Assert.Equal(percent, reading.BatteryPercent);
        Assert.Equal(frame.Length, reading.ReportLength);
    }

    [Theory]
    [InlineData(101)]
    [InlineData(150)]
    [InlineData(255)]
    public void RejectsBatteryAboveOneHundred(int rawPercent)
    {
        byte[] frame = CapturedFrames.StatusFrame(0);
        frame[BatteryFrameParser.BatteryPercentIndex] = (byte)rawPercent;

        bool parsed = BatteryFrameParser.TryParse(frame, out BatteryReading reading, out ParseFailureReason reason);

        Assert.False(parsed);
        Assert.Equal(ParseFailureReason.BatteryOutOfRange, reason);
        Assert.Equal(default, reading);
    }

    [Fact]
    public void ReadsCableFlagFalse()
    {
        byte[] frame = CapturedFrames.StatusFrame(73, cableConnected: false);

        bool parsed = BatteryFrameParser.TryParse(frame, out BatteryReading reading, out _);

        Assert.True(parsed);
        Assert.False(reading.CableConnected);
    }

    [Fact]
    public void ReadsCableFlagTrue()
    {
        byte[] frame = CapturedFrames.StatusFrame(73, cableConnected: true);

        bool parsed = BatteryFrameParser.TryParse(frame, out BatteryReading reading, out _);

        Assert.True(parsed);
        Assert.True(reading.CableConnected);
    }

    [Fact]
    public void RejectsImplausibleCableFlag()
    {
        byte[] frame = CapturedFrames.StatusFrame(73);
        frame[BatteryFrameParser.CableFlagIndex] = 0x02;

        bool parsed = BatteryFrameParser.TryParse(frame, out _, out ParseFailureReason reason);

        Assert.False(parsed);
        Assert.Equal(ParseFailureReason.InvalidCableFlag, reason);
    }

    [Fact]
    public void Byte37IsNeverTreatedAsCharging()
    {
        // Cable flag is 0 but byte 37 is 1: the reading must still report "on battery".
        byte[] onBattery = CapturedFrames.StatusFrame(73, cableConnected: false, notTheChargingFlag: 0x01);
        Assert.True(BatteryFrameParser.TryParse(onBattery, out BatteryReading batteryReading, out _));
        Assert.False(batteryReading.CableConnected);

        // Cable flag is 1 and byte 37 is 0: the reading must report "cable connected".
        byte[] onCable = CapturedFrames.StatusFrame(73, cableConnected: true, notTheChargingFlag: 0x00);
        Assert.True(BatteryFrameParser.TryParse(onCable, out BatteryReading cableReading, out _));
        Assert.True(cableReading.CableConnected);
    }

    [Fact]
    public void CapturedFrameHasReportIdInFirstByte()
    {
        Assert.Equal(CycloneProtocol.StatusReportId, CapturedFrames.PluggedFull[BatteryFrameParser.ReportIdIndex]);
        Assert.Equal(CycloneProtocol.StatusReportId, CapturedFrames.OnBatteryFull[BatteryFrameParser.ReportIdIndex]);
    }

    [Fact]
    public void CapturedFrameBatteryByteIsAtIndex36()
    {
        Assert.Equal(0x64, CapturedFrames.PluggedFull[BatteryFrameParser.BatteryPercentIndex]);
        Assert.Equal(0x64, CapturedFrames.OnBatteryFull[BatteryFrameParser.BatteryPercentIndex]);
    }

    [Fact]
    public void CapturedFramesParseToFullBattery()
    {
        Assert.True(BatteryFrameParser.TryParse(CapturedFrames.PluggedFull, out BatteryReading plugged, out _));
        Assert.Equal(100, plugged.BatteryPercent);
        Assert.Equal(64, plugged.ReportLength);

        Assert.True(BatteryFrameParser.TryParse(CapturedFrames.OnBatteryFull, out BatteryReading onBattery, out _));
        Assert.Equal(100, onBattery.BatteryPercent);
        Assert.Equal(64, onBattery.ReportLength);
    }

    [Fact]
    public void CapturedFrameIsFullReportLength()
    {
        Assert.Equal(CycloneProtocol.DefaultStatusReportLength, CapturedFrames.PluggedFull.Length);
        Assert.Equal(CycloneProtocol.DefaultStatusReportLength, CapturedFrames.OnBatteryFull.Length);
    }

    [Fact]
    public void ReceivedAtIsStampedByCallerNotParser()
    {
        byte[] frame = CapturedFrames.StatusFrame(55);
        Assert.True(BatteryFrameParser.TryParse(frame, out BatteryReading reading, out _));
        Assert.Equal(default, reading.ReceivedAtUtc);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.Equal(now, reading.At(now).ReceivedAtUtc);
    }

    [Fact]
    public void HexPrefixShowsLeadingBytesForDiagnostics()
    {
        string hex = BatteryFrameParser.ToHexPrefix(CapturedFrames.PluggedFull, count: 4);

        Assert.Equal("12 80 80 80 ...", hex);
    }

    [Fact]
    public void HexPrefixOfEmptyFrameIsReadable()
    {
        Assert.Equal("<empty>", BatteryFrameParser.ToHexPrefix(ReadOnlySpan<byte>.Empty));
    }
}
