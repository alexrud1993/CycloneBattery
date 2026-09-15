using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;
using CycloneBattery.Tests.Testing;
using Xunit;

namespace CycloneBattery.Tests.Hid;

/// <summary>
/// Covers interface selection: the first matching path is never trusted, and only an interface
/// that actually produces a valid <c>0x12</c> report is selected.
/// </summary>
public class CycloneInterfaceProberTests
{
    private static readonly ProbeOptions WithFrames = new()
    {
        HeartbeatWaitMilliseconds = 50,
        WakeWaitMilliseconds = 50,
        PollSliceMilliseconds = 1,
    };

    private static readonly ProbeOptions NoReads = new()
    {
        HeartbeatWaitMilliseconds = 0,
        WakeWaitMilliseconds = 0,
        PollSliceMilliseconds = 1,
    };

    [Fact]
    public void NoCandidatesProducesNoCandidatesOutcome()
    {
        var prober = new CycloneInterfaceProber(new FakeTransportFactory());

        InterfaceProbeOutcome outcome = prober.Probe(Array.Empty<HidDeviceCandidate>(), WithFrames);

        Assert.Equal(ProbeOutcomeKind.NoCandidates, outcome.Kind);
        Assert.Empty(outcome.Attempts);
        Assert.Null(outcome.Transport);
    }

    [Fact]
    public void EveryInterfaceBusyProducesBusyOutcome()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory { FailAsBusy = true };
        factory.FailOpenFor(candidate.DevicePath);
        var prober = new CycloneInterfaceProber(factory);

        InterfaceProbeOutcome outcome = prober.Probe(discovery.EnumerateCandidates(), WithFrames);

        Assert.Equal(ProbeOutcomeKind.AllBusy, outcome.Kind);
        Assert.True(outcome.Attempts[0].LooksBusy);
        Assert.False(outcome.Attempts[0].Opened);
    }

    [Fact]
    public void InterfaceThatNeverStreamsStatusIsNotSelected()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        factory.Register(candidate);
        var prober = new CycloneInterfaceProber(factory);

        InterfaceProbeOutcome outcome = prober.Probe(discovery.EnumerateCandidates(), WithFrames);

        Assert.Equal(ProbeOutcomeKind.OpenedButNoStatus, outcome.Kind);
        Assert.Null(outcome.Transport);
        Assert.True(outcome.Attempts[0].Opened);
        Assert.False(outcome.Attempts[0].StatusReceived);
        Assert.True(outcome.Attempts[0].HeartbeatSent);
    }

    [Fact]
    public void HeartbeatIsTriedFirstAndWakeIsOnlyAFallback()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(candidate);
        transport.EnqueueReading(73);
        var prober = new CycloneInterfaceProber(factory);

        InterfaceProbeOutcome outcome = prober.Probe(discovery.EnumerateCandidates(), WithFrames);

        Assert.Equal(ProbeOutcomeKind.Success, outcome.Kind);
        Assert.False(outcome.NeededWakeFallback);
        Assert.Single(transport.WrittenReports);
        Assert.Equal(CycloneProtocol.OutputReportId, transport.WrittenReports[0][0]);
        Assert.Equal(CycloneProtocol.HeartbeatOpcode, transport.WrittenReports[0][1]);
        Assert.Equal(73, outcome.FirstReading?.BatteryPercent);
        Assert.Same(transport, outcome.Transport);
    }

    [Fact]
    public void WakeFallbackIsUsedWhenHeartbeatProducesNothing()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(candidate);
        transport.EnqueueReading(64);
        var prober = new CycloneInterfaceProber(factory);

        // HeartbeatWait = 0 means the heartbeat window performs no reads at all, so only the
        // wake fallback can find the queued frame.
        InterfaceProbeOutcome outcome = prober.Probe(
            discovery.EnumerateCandidates(),
            NoReads with { WakeWaitMilliseconds = 50 });

        Assert.Equal(ProbeOutcomeKind.Success, outcome.Kind);
        Assert.True(outcome.NeededWakeFallback);
        Assert.Equal(2, transport.WrittenReports.Count);
        Assert.Equal(CycloneProtocol.HeartbeatOpcode, transport.WrittenReports[0][1]);
        Assert.Equal(CycloneProtocol.WakeOpcode, transport.WrittenReports[1][1]);
    }

    [Fact]
    public void SecondInterfaceIsSelectedWhenTheFirstIsQuiet()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate first = discovery.AddCandidate(@"\\?\hid#vid_3537&pid_100b#quiet");
        HidDeviceCandidate second = discovery.AddCandidate(@"\\?\hid#vid_3537&pid_100b#streaming");

        var factory = new FakeTransportFactory();
        FakeTransport quiet = factory.Register(first);
        FakeTransport streaming = factory.Register(second);
        streaming.EnqueueReading(41);

        var prober = new CycloneInterfaceProber(factory);
        InterfaceProbeOutcome outcome = prober.Probe(discovery.EnumerateCandidates(), WithFrames);

        Assert.Equal(ProbeOutcomeKind.Success, outcome.Kind);
        Assert.Equal(second.DevicePath, outcome.Transport!.Candidate.DevicePath);
        Assert.Equal(41, outcome.FirstReading?.BatteryPercent);

        // The rejected interface must have been closed again.
        Assert.True(quiet.Disposed);
        Assert.False(streaming.Disposed);
        Assert.Equal(2, factory.OpenAttempts.Count);
    }

    [Fact]
    public void EventReportsAloneDoNotCountAsStatus()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(candidate);
        transport.Enqueue(CapturedFrames.EventFrame(), CapturedFrames.EventFrame());
        var prober = new CycloneInterfaceProber(factory);

        InterfaceProbeOutcome outcome = prober.Probe(discovery.EnumerateCandidates(), WithFrames);

        Assert.Equal(ProbeOutcomeKind.OpenedButNoStatus, outcome.Kind);
        Assert.Equal(2, outcome.Attempts[0].ReportsSeen);
        Assert.Equal("10", outcome.Attempts[0].ReportIdsSeen);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public void RejectedStatusFrameIsReportedInAttemptDetails()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(candidate);

        byte[] invalid = CapturedFrames.StatusFrame(0);
        invalid[BatteryFrameParser.BatteryPercentIndex] = 0xFF;   // 255% -> out of range
        transport.Enqueue(invalid);

        var prober = new CycloneInterfaceProber(factory);
        InterfaceProbeOutcome outcome = prober.Probe(discovery.EnumerateCandidates(), WithFrames);

        Assert.Equal(ProbeOutcomeKind.OpenedButNoStatus, outcome.Kind);
        Assert.Equal(ParseFailureReason.BatteryOutOfRange, outcome.Attempts[0].LastParseFailure);
        Assert.NotEmpty(outcome.Attempts[0].FirstFrameHex);
    }

    [Fact]
    public void TransportFailureIsReportedAndReleasesTheInterface()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(candidate);
        transport.ThrowOnRead = new HidTransportException("device removed");
        var prober = new CycloneInterfaceProber(factory);

        InterfaceProbeOutcome outcome = prober.Probe(discovery.EnumerateCandidates(), WithFrames);

        Assert.Equal(ProbeOutcomeKind.TransportFailed, outcome.Kind);
        Assert.Contains("device removed", outcome.Attempts[0].TransportError, StringComparison.Ordinal);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public void OutputReportUsesHidReportedLength()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate(inputReportLength: 64, outputReportLength: 32);
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(candidate);
        transport.EnqueueReading(50);
        var prober = new CycloneInterfaceProber(factory);

        prober.Probe(discovery.EnumerateCandidates(), WithFrames);

        Assert.Equal(32, Assert.Single(transport.WrittenReports).Length);
    }
}
