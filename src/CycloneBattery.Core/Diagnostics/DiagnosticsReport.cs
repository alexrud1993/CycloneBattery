using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;

namespace CycloneBattery.Core.Diagnostics;

/// <summary>
/// Everything <c>--diagnostics</c> collects, in a form that can be rendered as paste-friendly
/// text or shown in the diagnostics window.
/// </summary>
public sealed record DiagnosticsReport
{
    /// <summary>When the report was produced.</summary>
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>Application version.</summary>
    public required string AppVersion { get; init; }

    /// <summary>OS description, e.g. <c>Microsoft Windows 10.0.26100</c>.</summary>
    public required string OperatingSystem { get; init; }

    /// <summary>OS architecture.</summary>
    public required string OsArchitecture { get; init; }

    /// <summary>Process architecture.</summary>
    public required string ProcessArchitecture { get; init; }

    /// <summary>CLR version.</summary>
    public required string RuntimeVersion { get; init; }

    /// <summary>Path of the settings file.</summary>
    public required string SettingsPath { get; init; }

    /// <summary>Folder log files are written to.</summary>
    public required string LogDirectory { get; init; }

    /// <summary>The strongest Cyclone 2 identity found on the bus.</summary>
    public required ControllerIdentity Identity { get; init; }

    /// <summary>One line per enumerated candidate interface (device paths sanitized).</summary>
    public required IReadOnlyList<string> Candidates { get; init; }

    /// <summary>Overall result of the validation pass.</summary>
    public required ProbeOutcomeKind Outcome { get; init; }

    /// <summary>Milliseconds the validation pass took.</summary>
    public required long ProbeElapsedMilliseconds { get; init; }

    /// <summary>Per-candidate detail.</summary>
    public required IReadOnlyList<CandidateProbeResult> Attempts { get; init; }

    /// <summary>Sanitized id of the interface that was selected, if any.</summary>
    public string? SelectedInterfaceId { get; init; }

    /// <summary>Max input report length of the selected interface.</summary>
    public int SelectedInputReportLength { get; init; }

    /// <summary>Max output report length of the selected interface.</summary>
    public int SelectedOutputReportLength { get; init; }

    /// <summary>Whether the heartbeat alone produced status reports.</summary>
    public bool HeartbeatSucceeded { get; init; }

    /// <summary>Whether the wake fallback was required.</summary>
    public bool WakeFallbackRequired { get; init; }

    /// <summary>Whether a valid <c>0x12</c> report was decoded.</summary>
    public bool StatusReportReceived { get; init; }

    /// <summary>Parsed battery percentage, when valid.</summary>
    public int? BatteryPercent { get; init; }

    /// <summary>Parsed cable flag, when valid.</summary>
    public bool? CableConnected { get; init; }

    /// <summary>How many additional frames were decoded during the confirmation burst.</summary>
    public int ConfirmedFrames { get; init; }

    /// <summary>Lowest percentage seen during the confirmation burst.</summary>
    public int? ConfirmedMinPercent { get; init; }

    /// <summary>Highest percentage seen during the confirmation burst.</summary>
    public int? ConfirmedMaxPercent { get; init; }

    /// <summary>Hex prefix of the first frame observed on the selected interface.</summary>
    public string FirstFrameHex { get; init; } = "";

    /// <summary>Names of running processes known to compete for the interface.</summary>
    public required IReadOnlyList<string> ConflictingProcesses { get; init; }

    /// <summary>Free-form notes and sanitized errors.</summary>
    public required IReadOnlyList<string> Notes { get; init; }
}
