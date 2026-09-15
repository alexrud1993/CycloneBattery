using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;
using CycloneBattery.Core.Services;
using CycloneBattery.Core.State;
using CycloneBattery.Tests.Testing;
using Xunit;

namespace CycloneBattery.Tests.Services;

/// <summary>
/// Exercises the real orchestration path (<c>RefreshNowAsync</c> and the background loop) with
/// fake HID transports, so no controller is required.
/// </summary>
public class ControllerStateServiceTests
{
    private static readonly ControllerServiceOptions FastOptions = new()
    {
        HeartbeatInterval = TimeSpan.FromMilliseconds(20),
        DisconnectedScanInterval = TimeSpan.FromMilliseconds(20),
        ConnectingRetryInterval = TimeSpan.FromMilliseconds(20),
        BusyRetryInterval = TimeSpan.FromMilliseconds(20),
        ErrorRetryInterval = TimeSpan.FromMilliseconds(20),
        StaleAfter = TimeSpan.FromSeconds(12),
        ReadSliceMilliseconds = 1,
        Probe = new ProbeOptions
        {
            HeartbeatWaitMilliseconds = 50,
            WakeWaitMilliseconds = 50,
            PollSliceMilliseconds = 1,
        },
    };

    private static readonly ControllerServiceOptions NoHeartbeatReads = FastOptions with
    {
        Probe = new ProbeOptions
        {
            HeartbeatWaitMilliseconds = 0,
            WakeWaitMilliseconds = 50,
            PollSliceMilliseconds = 1,
        },
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(ControllerServiceOptions? options = null)
        {
            Clock = new ManualClock();
            Machine = new ControllerStateMachine(Clock);
            Options = options ?? FastOptions;
            Service = new ControllerStateService(Discovery, Factory, Machine, Options, Log, Clock);
        }

        public FakeHidDeviceDiscovery Discovery { get; } = new();

        public FakeTransportFactory Factory { get; } = new();

        public ManualClock Clock { get; }

        public MemoryLogSink Log { get; } = new();

        public ControllerStateMachine Machine { get; }

        public ControllerServiceOptions Options { get; }

        public ControllerStateService Service { get; }

        public async ValueTask DisposeAsync() => await Service.DisposeAsync();
    }

    [Fact]
    public async Task NoControllerProducesDisconnected()
    {
        await using var fixture = new Fixture();
        fixture.Discovery.Identity = ControllerIdentity.None;

        ControllerState state = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Disconnected, state.Kind);
        Assert.Equal(1, fixture.Discovery.EnumerateCallCount);
        Assert.Null(state.BatteryPercent);
    }

    [Fact]
    public async Task ValidInterfaceConnectsAndReportsBattery()
    {
        await using var fixture = new Fixture();
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        FakeTransport transport = fixture.Factory.Register(candidate);
        transport.EnqueueReading(73);

        var readings = new List<BatteryReading>();
        fixture.Service.ReadingUpdated += (_, reading) => readings.Add(reading);

        ControllerState state = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Connected, state.Kind);
        Assert.Equal(73, state.BatteryPercent);
        Assert.False(state.CableConnected);
        Assert.Equal(candidate.SanitizedId, state.DevicePathId);
        Assert.Single(readings);
        Assert.Equal(73, readings[0].BatteryPercent);
    }

    [Fact]
    public async Task HeartbeatIsTheFirstWriteAfterConnecting()
    {
        await using var fixture = new Fixture();
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        FakeTransport transport = fixture.Factory.Register(candidate);
        transport.EnqueueReading(50);

        await fixture.Service.RefreshNowAsync();

        byte[] firstWrite = Assert.Single(transport.WrittenReports);
        Assert.Equal(CycloneProtocol.OutputReportId, firstWrite[0]);
        Assert.Equal(CycloneProtocol.HeartbeatOpcode, firstWrite[1]);
    }

    [Fact]
    public async Task WakeFallbackIsUsedWhenTheHeartbeatIsNotEnough()
    {
        await using var fixture = new Fixture(NoHeartbeatReads);
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        FakeTransport transport = fixture.Factory.Register(candidate);
        transport.EnqueueReading(66);

        ControllerState state = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Connected, state.Kind);
        Assert.Equal(66, state.BatteryPercent);
        Assert.Equal(2, transport.WrittenReports.Count);
        Assert.Equal(CycloneProtocol.WakeOpcode, transport.WrittenReports[1][1]);
    }

    [Fact]
    public async Task LockedInterfaceProducesBusyState()
    {
        await using var fixture = new Fixture();
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        fixture.Factory.FailAsBusy = true;
        fixture.Factory.FailOpenFor(candidate.DevicePath);

        ControllerState state = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Busy, state.Kind);
        Assert.Equal(ControllerStateMachine.BusyMessage, state.Message);
    }

    [Fact]
    public async Task TransportFailureBecomesErrorAndTriggersRediscovery()
    {
        await using var fixture = new Fixture();
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        FakeTransport transport = fixture.Factory.Register(candidate);
        transport.EnqueueReading(73);
        await fixture.Service.RefreshNowAsync();

        transport.ThrowOnRead = new HidTransportException("device removed");
        ControllerState failed = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Error, failed.Kind);
        Assert.True(transport.Disposed);
        Assert.Contains("device removed", failed.Message, StringComparison.Ordinal);

        ControllerState recovered = await fixture.Service.RefreshNowAsync();

        Assert.Equal(2, fixture.Discovery.EnumerateCallCount);
        Assert.NotEqual(ControllerStateKind.Error, recovered.Kind);
    }

    [Fact]
    public async Task ForceReconnectDropsTheInterfaceAndRediscovers()
    {
        await using var fixture = new Fixture();
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        FakeTransport transport = fixture.Factory.Register(candidate);
        transport.EnqueueReading(73);
        await fixture.Service.RefreshNowAsync();

        await fixture.Service.ForceReconnectAsync();

        Assert.True(transport.Disposed);
        Assert.Equal(ControllerStateKind.Disconnected, fixture.Service.State.Kind);

        transport.EnqueueReading(74);
        ControllerState state = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Connected, state.Kind);
        Assert.Equal(74, state.BatteryPercent);
    }

    [Fact]
    public async Task StaleReadingBecomesDisconnectedAfterTimeout()
    {
        await using var fixture = new Fixture();
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        FakeTransport transport = fixture.Factory.Register(candidate);
        transport.EnqueueReading(73);
        await fixture.Service.RefreshNowAsync();
        Assert.Equal(ControllerStateKind.Connected, fixture.Service.State.Kind);

        fixture.Clock.Advance(TimeSpan.FromSeconds(13));
        ControllerState state = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Disconnected, state.Kind);
        Assert.Equal(ControllerStateMachine.StaleMessage, state.Message);
    }

    [Fact]
    public async Task UnsupportedModeIsReportedWithoutABatteryValue()
    {
        await using var fixture = new Fixture();
        fixture.Discovery.Identity = ControllerIdentity.DualShock4;

        ControllerState state = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Disconnected, state.Kind);
        Assert.Contains("DS4", state.Message, StringComparison.Ordinal);
        Assert.Null(state.BatteryPercent);
    }

    [Fact]
    public async Task EnumerationFailureBecomesErrorState()
    {
        await using var fixture = new Fixture();
        fixture.Discovery.ThrowOnEnumerate = new InvalidOperationException("HID stack unavailable");

        ControllerState state = await fixture.Service.RefreshNowAsync();

        Assert.Equal(ControllerStateKind.Error, state.Kind);
        Assert.Contains("HID enumeration failed", state.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackgroundLoopConnectsAndStopReleasesTheInterface()
    {
        await using var fixture = new Fixture();
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        FakeTransport transport = fixture.Factory.Register(candidate);
        transport.EnqueueReading(81);

        await fixture.Service.StartAsync();

        bool connected = await WaitUntilAsync(
            () => fixture.Service.State.Kind == ControllerStateKind.Connected,
            TimeSpan.FromSeconds(5));

        await fixture.Service.StopAsync();

        Assert.True(connected, "background loop did not reach the Connected state");
        Assert.Equal(81, fixture.Service.State.BatteryPercent);
        Assert.True(transport.Disposed);
    }

    [Fact]
    public async Task StateChangedIsRaisedWhenTheStateMoves()
    {
        await using var fixture = new Fixture();
        HidDeviceCandidate candidate = fixture.Discovery.AddCandidate();
        FakeTransport transport = fixture.Factory.Register(candidate);
        transport.EnqueueReading(73);

        var observed = new List<ControllerStateKind>();
        fixture.Service.StateChanged += (_, state) => observed.Add(state.Kind);

        await fixture.Service.RefreshNowAsync();

        Assert.Contains(ControllerStateKind.Connected, observed);
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }
}
