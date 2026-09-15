using System.Diagnostics;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;

namespace CycloneBattery.Core.Hid;

/// <summary>
/// Validates HID candidate interfaces by actually talking to them, and hands back the one that
/// produces a real <c>0x12</c> status report.
/// </summary>
/// <remarks>
/// <para>
/// The Cyclone 2 exposes several HID interfaces with the same VID/PID, and only one of them
/// streams status. This type therefore never trusts enumeration order: it opens each candidate
/// in turn, sends the safe heartbeat, waits for a valid status frame, and only then falls back
/// to the secondary wake command. The first candidate that yields a decodable battery reading
/// wins and stays open.
/// </para>
/// <para>
/// Only <see cref="StatusActivationCommand"/> frames are ever written.
/// </para>
/// </remarks>
public sealed class CycloneInterfaceProber
{
    private readonly ICycloneHidTransportFactory _transportFactory;

    public CycloneInterfaceProber(ICycloneHidTransportFactory transportFactory)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
    }

    /// <summary>
    /// Probes <paramref name="candidates"/> and returns the first validated interface.
    /// </summary>
    /// <param name="candidates">Candidates from <see cref="IHidDeviceDiscovery.EnumerateCandidates"/>.</param>
    /// <param name="options">Timing tunables. Defaults to <see cref="ProbeOptions.Default"/>.</param>
    /// <param name="cancellationToken">Cancels between candidates and between poll slices.</param>
    /// <returns>
    /// The outcome. On success <see cref="InterfaceProbeOutcome.Transport"/> is open and owned by
    /// the caller; in every other case all transports have already been disposed.
    /// </returns>
    public InterfaceProbeOutcome Probe(
        IReadOnlyList<HidDeviceCandidate> candidates,
        ProbeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        options ??= ProbeOptions.Default;

        var totalStopwatch = Stopwatch.StartNew();
        var attempts = new List<CandidateProbeResult>();
        bool anyOpened = false;
        bool anyBusy = false;
        bool anyTransportError = false;

        foreach (HidDeviceCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CandidateProbeResult result = ProbeCandidate(
                candidate,
                options,
                cancellationToken,
                out ICycloneHidTransport? transport,
                out BatteryReading? reading,
                out bool neededWake);

            attempts.Add(result);

            if (result.StatusReceived && transport is not null)
            {
                return new InterfaceProbeOutcome
                {
                    Kind = ProbeOutcomeKind.Success,
                    Attempts = attempts,
                    Transport = transport,
                    FirstReading = reading,
                    NeededWakeFallback = neededWake,
                    ElapsedMilliseconds = totalStopwatch.ElapsedMilliseconds,
                };
            }

            anyOpened |= result.Opened;
            anyBusy |= result.LooksBusy;
            anyTransportError |= result.TransportError is not null;
        }

        ProbeOutcomeKind kind;
        if (attempts.Count == 0)
        {
            kind = ProbeOutcomeKind.NoCandidates;
        }
        else if (anyOpened)
        {
            // The identity is right and the interface is ours, but the controller is not
            // streaming status. Reported as "still connecting", never as a battery value.
            kind = ProbeOutcomeKind.OpenedButNoStatus;
        }
        else if (anyBusy)
        {
            kind = ProbeOutcomeKind.AllBusy;
        }
        else
        {
            kind = anyTransportError ? ProbeOutcomeKind.TransportFailed : ProbeOutcomeKind.NoCandidates;
        }

        return new InterfaceProbeOutcome
        {
            Kind = kind,
            Attempts = attempts,
            Transport = null,
            FirstReading = null,
            ElapsedMilliseconds = totalStopwatch.ElapsedMilliseconds,
        };
    }

    private CandidateProbeResult ProbeCandidate(
        HidDeviceCandidate candidate,
        ProbeOptions options,
        CancellationToken cancellationToken,
        out ICycloneHidTransport? transport,
        out BatteryReading? reading,
        out bool neededWake)
    {
        transport = null;
        reading = null;
        neededWake = false;

        var stopwatch = Stopwatch.StartNew();
        TransportOpenResult open = _transportFactory.TryOpen(candidate, out ICycloneHidTransport? opened);

        if (!open.Success || opened is null)
        {
            return new CandidateProbeResult
            {
                Candidate = candidate,
                Opened = false,
                OpenError = open.Error ?? "open failed",
                LooksBusy = open.LooksBusy,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            };
        }

        var scratch = new ProbeScratch();

        try
        {
            // Step 1 — the heartbeat GameSir Connect itself uses.
            scratch.HeartbeatSent = TrySend(
                opened,
                StatusActivationCommand.BuildHeartbeat(opened.MaxOutputReportLength),
                scratch,
                nameof(scratch.HeartbeatSent));

            if (scratch.HeartbeatSent)
            {
                reading = WaitForStatus(opened, options.HeartbeatWaitMilliseconds, options.PollSliceMilliseconds, scratch, cancellationToken);
            }

            // Step 2 — only if the heartbeat did not start the status stream.
            if (reading is null && !cancellationToken.IsCancellationRequested)
            {
                scratch.WakeSent = TrySend(
                    opened,
                    StatusActivationCommand.BuildWake(opened.MaxOutputReportLength),
                    scratch,
                    nameof(scratch.WakeSent));

                if (scratch.WakeSent)
                {
                    reading = WaitForStatus(opened, options.WakeWaitMilliseconds, options.PollSliceMilliseconds, scratch, cancellationToken);
                    neededWake = reading is not null;
                }
            }

            if (reading is not null)
            {
                // Hand the live transport to the caller; it must not be disposed here.
                transport = opened;
            }
            else
            {
                opened.Dispose();
            }
        }
        catch (HidTransportException ex)
        {
            scratch.TransportError ??= ex.Message;
            SafeDispose(opened);
        }
        catch (OperationCanceledException)
        {
            SafeDispose(opened);
            throw;
        }

        return new CandidateProbeResult
        {
            Candidate = candidate,
            Opened = true,
            HeartbeatSent = scratch.HeartbeatSent,
            WakeSent = scratch.WakeSent,
            ReportsSeen = scratch.ReportsSeen,
            ReportIdsSeen = scratch.FormatReportIds(),
            StatusReceived = reading is not null,
            LastParseFailure = scratch.LastFailure,
            FirstFrameHex = scratch.FirstFrameHex,
            TransportError = scratch.TransportError,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        };
    }

    private static bool TrySend(ICycloneHidTransport transport, byte[] report, ProbeScratch scratch, string which)
    {
        try
        {
            transport.WriteOutputReport(report);
            return true;
        }
        catch (HidTransportException ex)
        {
            scratch.TransportError ??= $"{which} failed: {ex.Message}";
            return false;
        }
    }

    private static BatteryReading? WaitForStatus(
        ICycloneHidTransport transport,
        int totalWaitMilliseconds,
        int pollSliceMilliseconds,
        ProbeScratch scratch,
        CancellationToken cancellationToken)
    {
        int slice = Math.Max(1, pollSliceMilliseconds);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < totalWaitMilliseconds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool gotFrame;
            try
            {
                gotFrame = transport.TryReadInputReport(slice, out ReadOnlyMemory<byte> frame);
                if (!gotFrame)
                {
                    continue;
                }

                scratch.ObserveFrame(frame);

                if (BatteryFrameParser.TryParse(frame.Span, out BatteryReading reading, out ParseFailureReason reason))
                {
                    return reading.At(DateTimeOffset.UtcNow);
                }

                // A rejected 0x12 is worth recording; a 0x10 event reply is not a failure.
                if (BatteryFrameParser.IsStatusReport(frame.Span))
                {
                    scratch.LastFailure = reason;
                }
            }
            catch (HidTransportException ex)
            {
                scratch.TransportError ??= ex.Message;
                return null;
            }
        }

        return null;
    }

    private static void SafeDispose(ICycloneHidTransport transport)
    {
        try
        {
            transport.Dispose();
        }
        catch
        {
            // A transport that already failed must not throw during cleanup.
        }
    }

    private sealed class ProbeScratch
    {
        private readonly SortedSet<byte> _reportIds = new();

        public bool HeartbeatSent { get; set; }

        public bool WakeSent { get; set; }

        public int ReportsSeen { get; private set; }

        public string FirstFrameHex { get; private set; } = "";

        public ParseFailureReason LastFailure { get; set; }

        public string? TransportError { get; set; }

        public void ObserveFrame(ReadOnlyMemory<byte> frame)
        {
            ReportsSeen++;
            if (FirstFrameHex.Length == 0)
            {
                FirstFrameHex = BatteryFrameParser.ToHexPrefix(frame.Span);
            }

            if (!frame.IsEmpty)
            {
                _reportIds.Add(frame.Span[0]);
            }
        }

        public string FormatReportIds() =>
            _reportIds.Count == 0 ? "" : string.Join(", ", _reportIds.Select(id => id.ToString("X2")));
    }
}
