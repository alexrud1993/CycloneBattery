using System.Collections.ObjectModel;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;

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
    public ParseFailureReason LastParseFailure { get; init; }

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
    Success = 0,
    NoCandidates,
    AllBusy,
    OpenedButNoStatus,
    TransportFailed,
}

/// <summary>The complete outcome of one discovery + validation pass.</summary>
public sealed record InterfaceProbeOutcome
{
    public required ProbeOutcomeKind Kind { get; init; }
    public IReadOnlyList<CandidateProbeResult> Attempts { get; init; } = Array.Empty<CandidateProbeResult>();
    public ICycloneHidTransport? Transport { get; init; }
    public BatteryReading? FirstReading { get; init; }
    public bool NeededWakeFallback { get; init; }
    public long ElapsedMilliseconds { get; init; }

    public static InterfaceProbeOutcome NoCandidates() => new() { Kind = ProbeOutcomeKind.NoCandidates };
    public static InterfaceProbeOutcome Busy() => new() { Kind = ProbeOutcomeKind.AllBusy };

    public void ReleaseTransport()
    {
        try
        {
            Transport?.Dispose();
        }
        catch
        {
        }
    }
}

/// <summary>Tunables for <see cref="CycloneInterfaceProber"/>.</summary>
public sealed record ProbeOptions
{
    public int HeartbeatWaitMilliseconds { get; init; } = 900;
    public int WakeWaitMilliseconds { get; init; } = 900;
    public int PollSliceMilliseconds { get; init; } = 25;
    public static ProbeOptions Default { get; } = new();
    public static ProbeOptions ForDiagnostics { get; } = new()
    {
        HeartbeatWaitMilliseconds = 1200,
        WakeWaitMilliseconds = 1200,
        PollSliceMilliseconds = 25,
    };
}
