using System.Diagnostics;
using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;

namespace CycloneBattery.Core.Diagnostics;

/// <summary>Runs a bounded, read-only hardware diagnostic pass.</summary>
public sealed class DiagnosticsRunner
{
    private readonly IHidDeviceDiscovery _discovery;
    private readonly CycloneInterfaceProber _prober;

    public DiagnosticsRunner(IHidDeviceDiscovery discovery, CycloneInterfaceProber prober)
    {
        _discovery = discovery;
        _prober = prober;
    }

    public DiagnosticsReport Run(string appVersion, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();
        IReadOnlyList<HidDeviceCandidate> candidates;

        try
        {
            candidates = _discovery.EnumerateCyclone2Candidates();
        }
        catch (Exception ex)
        {
            return new DiagnosticsReport
            {
                GeneratedAtUtc = DateTimeOffset.UtcNow,
                AppVersion = appVersion,
                OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                Notes = new[] { $"HID enumeration failed: {ex.Message}" },
            };
        }

        InterfaceProbeOutcome outcome = _prober.Probe(candidates, ProbeOptions.ForDiagnostics, cancellationToken);

        int confirmedFrames = 0;
        int? minPercent = null;
        int? maxPercent = null;

        if (outcome.Transport is not null)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 600 && !cancellationToken.IsCancellationRequested)
                {
                    if (!outcome.Transport.TryReadInputReport(25, out ReadOnlyMemory<byte> frame))
                    {
                        continue;
                    }

                    if (BatteryFrameParser.TryParse(frame.Span, out BatteryReading sampleReading, out _))
                    {
                        confirmedFrames++;
                        minPercent = minPercent is null ? sampleReading.BatteryPercent : Math.Min(minPercent.Value, sampleReading.BatteryPercent);
                        maxPercent = maxPercent is null ? sampleReading.BatteryPercent : Math.Max(maxPercent.Value, sampleReading.BatteryPercent);
                    }
                }
            }
            catch (HidTransportException ex)
            {
                notes.Add($"Confirmation read failed: {ex.Message}");
            }
            finally
            {
                outcome.ReleaseTransport();
            }
        }

        CandidateProbeResult? selected = outcome.Attempts.FirstOrDefault(a => a.StatusReceived);
        BatteryReading? reading = outcome.FirstReading;

        return new DiagnosticsReport
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            AppVersion = appVersion,
            OsDescription = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            Candidates = candidates,
            ProbeOutcome = outcome.Kind,
            Attempts = outcome.Attempts,
            SelectedDeviceId = selected?.Candidate.SanitizedId,
            BatteryPercent = reading?.BatteryPercent,
            CableConnected = reading?.CableConnected,
            ConfirmedFrames = confirmedFrames,
            MinConfirmedPercent = minPercent,
            MaxConfirmedPercent = maxPercent,
            NeededWakeFallback = outcome.NeededWakeFallback,
            Notes = notes,
        };
    }
}
