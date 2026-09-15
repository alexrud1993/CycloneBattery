using CycloneBattery.Core.Protocol;
using CycloneBattery.Core.Services;
using CycloneBattery.Core.Settings;
using CycloneBattery.Core.State;
using Xunit;

namespace CycloneBattery.Tests.State;

/// <summary>
/// Covers the low-battery alert rules: one alert per discharge, no spam, hysteresis re-arm, and
/// charging behaviour.
/// </summary>
public class BatteryAlertMonitorTests
{
    private static BatteryReading Reading(int percent, bool cable = false) =>
        new(percent, cable, DateTimeOffset.UnixEpoch, 64);

    private static BatteryAlertMonitor CreateMonitor(int threshold = 20, int hysteresis = 5, bool enabled = true)
    {
        var monitor = new BatteryAlertMonitor(new ManualClock());
        monitor.Configure(threshold, hysteresis, enabled);
        return monitor;
    }

    [Fact]
    public void NoAlertAboveThreshold()
    {
        BatteryAlertMonitor monitor = CreateMonitor();

        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(80)));
        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(45)));
        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(21)));
    }

    [Fact]
    public void OneAlertWhenCrossingThreshold()
    {
        BatteryAlertMonitor monitor = CreateMonitor();
        var raised = new List<LowBatteryAlertEventArgs>();
        monitor.AlertRaised += (_, args) => raised.Add(args);

        monitor.Process(Reading(40));
        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(20)));

        Assert.Single(raised);
        Assert.Equal(20, raised[0].BatteryPercent);
        Assert.Equal(20, raised[0].ThresholdPercent);
    }

    [Fact]
    public void NoRepeatedAlertWhileBatteryStaysLow()
    {
        BatteryAlertMonitor monitor = CreateMonitor();

        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(20)));
        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(18)));
        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(15)));
        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(10)));
    }

    [Fact]
    public void NoReArmInsideTheHysteresisBand()
    {
        BatteryAlertMonitor monitor = CreateMonitor(threshold: 20, hysteresis: 5);

        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(19)));

        // Rising to 24 is still inside 20+5, so a new alert must not arm.
        monitor.Process(Reading(24));

        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(20)));
    }

    [Fact]
    public void ReArmsAfterRisingAboveThresholdPlusHysteresis()
    {
        BatteryAlertMonitor monitor = CreateMonitor(threshold: 20, hysteresis: 5);

        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(19)));
        monitor.Process(Reading(26));

        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(20)));
    }

    [Fact]
    public void ChargingSuppressesAlertAndReArmsForNextDischarge()
    {
        BatteryAlertMonitor monitor = CreateMonitor();

        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(15, cable: true)));

        // Unplugging below the threshold is now a fresh discharge event.
        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(15, cable: false)));
    }

    [Fact]
    public void DisabledMonitorNeverFires()
    {
        BatteryAlertMonitor monitor = CreateMonitor(enabled: false);

        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(5)));
        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(1)));
    }

    [Fact]
    public void EnablingAtLowBatteryFiresOnceThenStaysQuiet()
    {
        BatteryAlertMonitor monitor = CreateMonitor(enabled: false);
        monitor.Process(Reading(12));

        monitor.Configure(20, 5, enabled: true);

        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(12)));
        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(11)));
    }

    [Fact]
    public void ResetForgetsThePendingDischarge()
    {
        BatteryAlertMonitor monitor = CreateMonitor();
        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(19)));

        monitor.Reset();

        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(18)));
    }

    [Fact]
    public void ConfigureFromSettingsUsesDocumentedDefaults()
    {
        var monitor = new BatteryAlertMonitor();
        monitor.Configure(AppSettings.CreateDefault());

        Assert.Equal(LowBatteryAlertDecision.None, monitor.Process(Reading(21)));
        Assert.Equal(LowBatteryAlertDecision.Fire, monitor.Process(Reading(20)));
    }

    [Fact]
    public void LastFiredAtIsRecorded()
    {
        var clock = new ManualClock();
        var monitor = new BatteryAlertMonitor(clock);
        monitor.Configure(20, 5, true);

        Assert.Null(monitor.LastFiredAt);

        monitor.Process(Reading(10));

        Assert.Equal(clock.UtcNow, monitor.LastFiredAt);
    }
}
