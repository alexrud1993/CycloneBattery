using CycloneBattery.Core.Diagnostics;
using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Tests.Testing;
using Xunit;

namespace CycloneBattery.Tests.Diagnostics;

/// <summary>
/// The diagnostics path is what makes remote hardware debugging possible, so its content is
/// asserted explicitly: a pasted log must be enough to diagnose interface and offset problems.
/// </summary>
public class DiagnosticsRunnerTests
{
    [Fact]
    public void ReportDescribesAWorkingController()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(candidate);
        transport.EnqueueReading(73, cableConnected: true);
        transport.EnqueueReading(73, cableConnected: true);

        var runner = new DiagnosticsRunner(discovery, factory);
        DiagnosticsReport report = runner.Run(@"C:\settings.json", @"C:\logs", "1.2.3");

        Assert.Equal(ControllerIdentity.XInput, report.Identity);
        Assert.Equal(ProbeOutcomeKind.Success, report.Outcome);
        Assert.True(report.StatusReportReceived);
        Assert.Equal(73, report.BatteryPercent);
        Assert.True(report.CableConnected);
        Assert.Equal("1.2.3", report.AppVersion);
        Assert.Single(report.Candidates);
        Assert.NotNull(report.SelectedInterfaceId);
        Assert.Equal(64, report.SelectedInputReportLength);
    }

    [Fact]
    public void FormattedReportContainsEveryRequiredCategory()
    {
        var discovery = new FakeHidDeviceDiscovery();
        discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(discovery.EnumerateCandidates()[0]);
        transport.EnqueueReading(42);

        DiagnosticsReport report = new DiagnosticsRunner(discovery, factory)
            .Run(@"C:\settings.json", @"C:\logs", "9.9.9");

        string text = DiagnosticsRunner.Format(report);

        Assert.Contains("App version          : 9.9.9", text, StringComparison.Ordinal);
        Assert.Contains("OS                   :", text, StringComparison.Ordinal);
        Assert.Contains("Matching HID devices", text, StringComparison.Ordinal);
        Assert.Contains("vid=0x3537 pid=0x100B", text, StringComparison.Ordinal);
        Assert.Contains("Input report length", text, StringComparison.Ordinal);
        Assert.Contains("Output report length", text, StringComparison.Ordinal);
        Assert.Contains("Heartbeat produced 0x12", text, StringComparison.Ordinal);
        Assert.Contains("0x12 report received : yes", text, StringComparison.Ordinal);
        Assert.Contains("Battery percent      : 42%", text, StringComparison.Ordinal);
        Assert.Contains("Cable flag (byte 35) : 0 (on battery)", text, StringComparison.Ordinal);
        Assert.Contains("Interface id         :", text, StringComparison.Ordinal);
        Assert.Contains("=== End of diagnostics ===", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RawDevicePathsAreNeverIncluded()
    {
        var discovery = new FakeHidDeviceDiscovery();
        const string rawPath = @"\\?\hid#vid_3537&pid_100b#7&1a2b3c&0&0000";

        HidDeviceCandidate candidate = discovery.AddCandidate(rawPath);
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(candidate);
        transport.EnqueueReading(55);

        DiagnosticsReport report = new DiagnosticsRunner(discovery, factory)
            .Run(@"C:\settings.json", @"C:\logs", "1.0.0");

        string text = DiagnosticsRunner.Format(report);

        Assert.DoesNotContain(rawPath, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(candidate.SanitizedId, text, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingControllerIsReportedClearly()
    {
        var discovery = new FakeHidDeviceDiscovery { Identity = ControllerIdentity.None };
        var runner = new DiagnosticsRunner(discovery, new FakeTransportFactory());

        DiagnosticsReport report = runner.Run(@"C:\settings.json", @"C:\logs", "1.0.0");
        string text = DiagnosticsRunner.Format(report);

        Assert.Equal(ProbeOutcomeKind.NoCandidates, report.Outcome);
        Assert.False(report.StatusReportReceived);
        Assert.Null(report.BatteryPercent);
        Assert.Contains("Battery percent      : unknown", text, StringComparison.Ordinal);
        Assert.Contains("(none)", text, StringComparison.Ordinal);
        Assert.Contains("Interface id         : (none)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BusyInterfaceIsReportedAsBusy()
    {
        var discovery = new FakeHidDeviceDiscovery();
        HidDeviceCandidate candidate = discovery.AddCandidate();
        var factory = new FakeTransportFactory { FailAsBusy = true };
        factory.FailOpenFor(candidate.DevicePath);

        DiagnosticsReport report = new DiagnosticsRunner(discovery, factory)
            .Run(@"C:\settings.json", @"C:\logs", "1.0.0");

        Assert.Equal(ProbeOutcomeKind.AllBusy, report.Outcome);
        Assert.Contains("open error   :", DiagnosticsRunner.Format(report), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmationBurstCountsValidFrames()
    {
        var discovery = new FakeHidDeviceDiscovery();
        discovery.AddCandidate();
        var factory = new FakeTransportFactory();
        FakeTransport transport = factory.Register(discovery.EnumerateCandidates()[0]);
        transport.EnqueueReading(60);
        transport.EnqueueReading(61);
        transport.EnqueueReading(62);

        DiagnosticsReport report = new DiagnosticsRunner(discovery, factory)
            .Run(@"C:\settings.json", @"C:\logs", "1.0.0");

        Assert.True(report.ConfirmedFrames >= 2);
        Assert.Equal(61, report.ConfirmedMinPercent);
        Assert.Equal(62, report.ConfirmedMaxPercent);
    }

    [Fact]
    public void ConflictingProcessListIsAlwaysPresent()
    {
        var discovery = new FakeHidDeviceDiscovery { Identity = ControllerIdentity.None };

        DiagnosticsReport report = new DiagnosticsRunner(discovery, new FakeTransportFactory())
            .Run(@"C:\settings.json", @"C:\logs", "1.0.0");

        Assert.NotNull(report.ConflictingProcesses);
    }
}
