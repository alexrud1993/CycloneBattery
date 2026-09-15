using CycloneBattery.Core.Protocol;
using Xunit;

namespace CycloneBattery.Tests.Protocol;

/// <summary>
/// Proves that the only two commands this application can send are the safe status
/// activation reports, and that they honour the HID-reported report length.
/// </summary>
public class StatusActivationCommandTests
{
    [Fact]
    public void HeartbeatStartsWithReportIdAndOpcode()
    {
        byte[] report = StatusActivationCommand.BuildHeartbeat(64);

        Assert.Equal(CycloneProtocol.OutputReportId, report[0]);
        Assert.Equal(CycloneProtocol.HeartbeatOpcode, report[1]);
    }

    [Fact]
    public void WakeStartsWithReportIdAndOpcode()
    {
        byte[] report = StatusActivationCommand.BuildWake(64);

        Assert.Equal(CycloneProtocol.OutputReportId, report[0]);
        Assert.Equal(CycloneProtocol.WakeOpcode, report[1]);
    }

    [Theory]
    [InlineData(64)]
    [InlineData(32)]
    [InlineData(8)]
    public void UsesReportedOutputReportLength(int reportedLength)
    {
        Assert.Equal(reportedLength, StatusActivationCommand.BuildHeartbeat(reportedLength).Length);
        Assert.Equal(reportedLength, StatusActivationCommand.BuildWake(reportedLength).Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-5)]
    public void FallsBackToObservedLengthWhenReportedLengthIsUnusable(int reportedLength)
    {
        byte[] report = StatusActivationCommand.BuildHeartbeat(reportedLength);

        Assert.Equal(CycloneProtocol.DefaultOutputReportLength, report.Length);
        Assert.Equal(CycloneProtocol.DefaultOutputReportLength, StatusActivationCommand.ResolveOutputReportLength(reportedLength));
    }

    [Fact]
    public void HeartbeatCarriesNoPayload()
    {
        byte[] report = StatusActivationCommand.BuildHeartbeat(64);

        // Every byte after the opcode must stay zero: no register address, no length, no data.
        Assert.All(report.Skip(2), b => Assert.Equal(0, b));
    }

    [Fact]
    public void WakeCarriesNoPayload()
    {
        byte[] report = StatusActivationCommand.BuildWake(64);

        Assert.All(report.Skip(2), b => Assert.Equal(0, b));
    }

    [Fact]
    public void HeartbeatAndWakeDifferOnlyInOpcode()
    {
        byte[] heartbeat = StatusActivationCommand.BuildHeartbeat(64);
        byte[] wake = StatusActivationCommand.BuildWake(64);

        Assert.Equal(heartbeat.Length, wake.Length);
        Assert.Equal(heartbeat[0], wake[0]);
        Assert.NotEqual(heartbeat[1], wake[1]);
    }
}
