using System.Collections.ObjectModel;
using CycloneBattery.Core.Models;

namespace CycloneBattery.Core.Hid;

/// <summary>Result of probing one candidate interface.</summary>
public sealed record CandidateProbeResult
{
    /// <summary>The interface that was probed.</summary>
    public required HidDeviceCandidate Candidate { get; init; }

    /// <summary>Whether the interface could be opened.</summary>
    public bool Opened { get; init; }

    /// <summary>Sanitized error text when the interface could not be opened.</summary>
    public string? OpenError { get; init; }

    /// <summary><see langword="true"/> when the open failure looks like another program owns the device.</summary>
    public bool LooksBusy { get; init; }

    /// <summary>Whether the <c>0x0F / 0xF2</c> heartbeat was sent successfully.</summary>
    public bool HeartbeatSent { get; init; }

    /// <summary>Whether the <c>0x0F / 0x03</c> wake fallback was needed and sent successfully.</summary>
    public bool WakeSent { get; init; }

    /// <summary>Number of input reports observed while waiting.</summary>
    public int ReportsSeen { get; init; }

    /// <summary>Distinct report ids observed while waiting, e.g. <c>10, 12</c>.</summary>
    public string ReportIdsSeen { get; init; } = "";

    /// <summary>Whether a frame was successfully decoded into a battery reading.</summary>
    public bool StatusReceived { get; init; }

    /// <summary>Rejection reason of the last <c>0x12</c> frame seen, if it was rejected.</summary>
    public Protocol.ParseFailureReason LastParseFailure { get; init; }

    /// <summary>Hex prefix of the first report observed, for pasted diagnostics.</summary>
    public string FirstFrameHex { get; init; } = "";

    /// <summary>Sanitized error text from a failed read/write, when applicable.</summary>
    public string? TransportError { get; init; }

    /// <summary>Milliseconds spent probing this candidate.</summary>
    public long ElapsedMilliseconds { get; init; }
}

/// <summary>Why a probe run ended the way it did.</summary>
public enum ProbeOutcomeKind
{
    /// <summary>A working interface was found and validated.</summary>
    Success = 0,

    /// <summary>No HID interface with the supported identity is present.</summary>
    NoCandidates,

    /// <summary>Every candidate exists but could not be opened, and the cause looks like another process.</summary>
    AllBusy,

    /// <summary>An interface opened, but no valid <c>0x12</c> status report arrived.</summary>
    OpenedButNoStatus,

    /// <summary>Every candidate failed with a transport error.</summary>
    TransportFailed,
}

/// <summary>The complete outcome of one discovery + validation pass.</summary>
public sealed record InterfaceProbeOutcome
{
    /// <summary>Why the pass ended.</summary>
    public required ProbeOutcomeKind Kind { get; init; }

    /// <summary>Per-candidate detail, in probe order. Used verbatim by diagnostics.</summary>
    public IReadOnlyList<CandidateProbeResult> Attempts { get; init; } = Array.Empty<CandidateProbeResult>();

    /// <summary>The open, validated transport when <see cref="Kind"/> is <see cref="ProbeOutcomeKind.Success"/>.</summary>
    public ICycloneHidTransport? Transport { get; init; }

    /// <summary>The first valid reading observed during validation.</summary>
    public BatteryReading? FirstReading { get; init; }

    /// <summary>Whether the wake fallback was what finally produced status reports.</summary>
    public bool NeededWakeFallback { get; init; }

    /// <summary>Total probe duration in milliseconds.</summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>Synthetic outcome meaning "no candidate interface is present".</summary>
    public static InterfaceProbeOutcome NoCandidates() => new() { Kind = ProbeOutcomeKind.NoCandidates };

    /// <summary>Synthetic outcome meaning "every candidate is held by another process".</summary>
    public static InterfaceProbeOutcome Busy() => new() { Kind = ProbeOutcomeKind.AllBusy };

    /// <summary>Builds an outcome with the supplied transport closed and released.</summary>
    public void ReleaseTransport()
    {
        try
        {
            Transport?.Dispose();
        }
        catch
        {
            // Disposing a transport that already failed must never mask the original outcome.
        }
    }
}

/// <summary>Tunables for <see cref="CycloneInterfaceProber"/>.</summary>
public sealed record ProbeOptions
{
    /// <summary>How long to wait for a <c>0x12</c> frame after sending the heartbeat.</summary>
    public int HeartbeatWaitMilliseconds { get; init; } = 900;

    /// <summary>How long to wait for a <c>0x12</c> frame after sending the wake fallback.</summary>
    public int WakeWaitMilliseconds { get; init; } = 900;

    /// <summary>Blocking slice used while polling for a report.</summary>
    public int PollSliceMilliseconds { get; init; } = 25;

    /// <summary>Defaults used everywhere except tests.</summary>
    public static ProbeOptions Default { get; } = new();

    /// <summary>Longer waits, used by <c>--diagnostics</c> so a slow interface still gets a fair chance.</summary>
    public static ProbeOptions ForDiagnostics { get; } = new()
    {
        HeartbeatWaitMilliseconds = 1200,
        WakeWaitMilliseconds = 1200,
        PollSliceMilliseconds = 25,
    };
}
