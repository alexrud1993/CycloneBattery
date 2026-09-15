using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;
using CycloneBattery.Core.Services;
using CycloneBattery.Core.State;
using CycloneBattery.Tests.Testing;
using Xunit;

namespace CycloneBattery.Tests.State;

/// <summary>
/// Covers the state model: explicit transitions, busy handling, and the rule that a rejected
/// frame never blanks a good reading.
/// </summary>
public class ControllerStateMachineTests
{
    private static BatteryReading Reading(int percent, bool cable = false, DateTimeOffset? at = null) =>
        new(percent, cable, at ?? DateTimeOffset.UnixEpoch, 64);

    [Fact]
    public void InitialStateIsDisconnected()
    {
        var machine = new ControllerStateMachine();

        Assert.Equal(ControllerStateKind.Disconnected, machine.Current.Kind);
        Assert.Null(machine.Current.BatteryPercent);
        Assert.Null(machine.LastKnownReading);
    }

    [Fact]
    public void DisconnectedToConnectingWhenInterfaceOpensButIsQuiet()
    {
        var machine = new ControllerStateMachine();

        machine.ApplyProbeOutcome(new InterfaceProbeOutcome { Kind = ProbeOutcomeKind.OpenedButNoStatus }, ControllerIdentity.XInput);

        Assert.Equal(ControllerStateKind.Connecting, machine.Current.Kind);
        Assert.Equal(ControllerStateMachine.WaitingForStatusMessage, machine.Current.Message);
    }

    [Fact]
    public void ConnectingToConnectedWhenValidReportArrives()
    {
        var machine = new ControllerStateMachine();
        machine.ApplyProbeOutcome(new InterfaceProbeOutcome { Kind = ProbeOutcomeKind.OpenedButNoStatus }, ControllerIdentity.XInput);

        machine.ApplyReading(Reading(73), "abc123");

        Assert.Equal(ControllerStateKind.Connected, machine.Current.Kind);
        Assert.Equal(73, machine.Current.BatteryPercent);
        Assert.False(machine.Current.CableConnected);
        Assert.Equal("abc123", machine.Current.DevicePathId);
        Assert.True(machine.Current.HasBattery);
    }

    [Fact]
    public void ConnectedToDisconnectedWhenIdentityDisappears()
    {
        var machine = new ControllerStateMachine();
        machine.ApplyReading(Reading(73), null);

        machine.ApplyProbeOutcome(InterfaceProbeOutcome.NoCandidates(), ControllerIdentity.None);

        Assert.Equal(ControllerStateKind.Disconnected, machine.Current.Kind);
        Assert.Null(machine.Current.BatteryPercent);
        Assert.False(machine.Current.HasBattery);
    }

    [Fact]
    public void BusyOutcomeUsesTheActionableMessage()
    {
        var machine = new ControllerStateMachine();

        machine.ApplyProbeOutcome(InterfaceProbeOutcome.Busy(), ControllerIdentity.XInput);

        Assert.Equal(ControllerStateKind.Busy, machine.Current.Kind);
        Assert.Equal(ControllerStateMachine.BusyMessage, machine.Current.Message);
    }

    [Fact]
    public void TransportFailureBecomesErrorState()
    {
        var machine = new ControllerStateMachine();

        machine.NoteTransportFailure("device unplugged mid-read");

        Assert.Equal(ControllerStateKind.Error, machine.Current.Kind);
        Assert.Contains("device unplugged mid-read", machine.Current.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedModeIsReportedWithoutInventingABatteryValue()
    {
        var machine = new ControllerStateMachine();

        machine.ApplyProbeOutcome(InterfaceProbeOutcome.NoCandidates(), ControllerIdentity.DualShock4);

        Assert.Equal(ControllerStateKind.Disconnected, machine.Current.Kind);
        Assert.Contains("DS4", machine.Current.Message, StringComparison.Ordinal);
        Assert.Null(machine.Current.BatteryPercent);
    }

    [Fact]
    public void RejectedFrameDoesNotEraseLastGoodReading()
    {
        var machine = new ControllerStateMachine();
        machine.ApplyReading(Reading(73), null);

        machine.NoteRejectedFrame(ParseFailureReason.BatteryOutOfRange);

        Assert.Equal(ControllerStateKind.Connected, machine.Current.Kind);
        Assert.Equal(73, machine.Current.BatteryPercent);
        Assert.Equal(ParseFailureReason.BatteryOutOfRange, machine.LastParseFailure);
        Assert.Equal(73, machine.LastKnownReading?.BatteryPercent);
    }

    [Fact]
    public void StaleReadingIsNotExpiredBeforeTimeout()
    {
        var clock = new ManualClock();
        var machine = new ControllerStateMachine(clock) { StaleAfter = TimeSpan.FromSeconds(12) };

        machine.ApplyReading(Reading(73, at: clock.UtcNow), null);
        clock.Advance(TimeSpan.FromSeconds(11));

        Assert.False(machine.ExpireStaleReading());
        Assert.Equal(ControllerStateKind.Connected, machine.Current.Kind);
    }

    [Fact]
    public void StaleReadingBecomesDisconnectedAfterTimeout()
    {
        var clock = new ManualClock();
        var machine = new ControllerStateMachine(clock) { StaleAfter = TimeSpan.FromSeconds(12) };

        machine.ApplyReading(Reading(73, at: clock.UtcNow), null);
        clock.Advance(TimeSpan.FromSeconds(13));

        Assert.True(machine.ExpireStaleReading());
        Assert.Equal(ControllerStateKind.Disconnected, machine.Current.Kind);
        Assert.Equal(ControllerStateMachine.StaleMessage, machine.Current.Message);

        // The last known value is kept for the UI/log even though the state went stale.
        Assert.Equal(73, machine.LastKnownReading?.BatteryPercent);
    }

    [Fact]
    public void ExpiringAnAlreadyDisconnectedStateChangesNothing()
    {
        var machine = new ControllerStateMachine();

        Assert.False(machine.ExpireStaleReading());
        Assert.Equal(ControllerStateKind.Disconnected, machine.Current.Kind);
    }

    [Fact]
    public void StateChangedFiresOnlyOnRealChanges()
    {
        var machine = new ControllerStateMachine();
        int changes = 0;
        machine.StateChanged += (_, _) => changes++;

        machine.ApplyReading(Reading(73), null);
        machine.ApplyReading(Reading(73), null);   // identical state: no event
        machine.ApplyReading(Reading(72), null);   // battery changed: event

        Assert.Equal(2, changes);
    }

    [Fact]
    public void ResetReturnsToDisconnected()
    {
        var machine = new ControllerStateMachine();
        machine.ApplyReading(Reading(73), null);

        machine.Reset("forced reconnect");

        Assert.Equal(ControllerStateKind.Disconnected, machine.Current.Kind);
        Assert.Equal("forced reconnect", machine.Current.Message);
    }

    [Fact]
    public void CableStateIsSurfacedOnTheState()
    {
        var machine = new ControllerStateMachine();

        machine.ApplyReading(Reading(88, cable: true), null);

        Assert.True(machine.Current.CableConnected);
        Assert.False(machine.Current.IsOnBattery);
    }
}
