using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;

namespace CycloneBattery.Core.Diagnostics;

/// <summary>
/// Runs a bounded, read-only hardware investigation and produces a
/// <see cref="DiagnosticsReport"/>.
/// </summary>
/// <remarks>
/// This exists because the developer environment cannot touch the real controller: the output is
/// designed so that a pasted log alone is enough to tell whether interface selection, report
/// lengths, heartbeat handling or the byte offsets are the problem.
/// </remarks>
public sealed class DiagnosticsRunner
{
    /// <summary>Process names that are known to hold the Cyclone 2 HID interface.</summary>
    public static readonly string[] KnownConflictingProcesses =
    [
        "GameSir Connect",
        "GameSirConnect",
        "GameSir",
        "GameSir Nexus",
        "GameSirNexus",
    ];

    private readonly IHidDeviceDiscovery _discovery;
    private readonly CycloneInterfaceProber _prober;

    public DiagnosticsRunner(IHidDeviceDiscovery discovery, ICycloneHidTransportFactory transportFactory)
        : this(discovery, new CycloneInterfaceProber(transportFactory))
    {
    }

    public DiagnosticsRunner(IHidDeviceDiscovery discovery, CycloneInterfaceProber prober)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _prober = prober ?? throw new ArgumentNullException(nameof(prober));
    }

    /// <summary>
    /// Collects a report. Blocking: callers should run this on a background thread.
    /// </summary>
    /// <param name="settingsPath">Path of the settings file, for the environment section.</param>
    /// <param name="logDirectory">Folder logs are written to.</param>
    /// <param name="appVersion">Version string to print.</param>
    /// <param name="cancellationToken">Cancels the validation pass.</param>
    public DiagnosticsReport Run(
        string settingsPath,
        string logDirectory,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();

        ControllerIdentity identity = SafeDetectIdentity(notes);
        IReadOnlyList<HidDeviceCandidate> candidates;
        try
        {
            candidates = _discovery.EnumerateCandidates();
        }
        catch (Exception ex)
        {
            candidates = Array.Empty<HidDeviceCandidate>();
            notes.Add($"Enumeration failed: {ex.GetType().Name}: {ex.Message}");
        }

        InterfaceProbeOutcome outcome = _prober.Probe(candidates, ProbeOptions.ForDiagnostics, cancellationToken);

        int confirmedFrames = 0;
        int? minPercent = null;
        int? maxPercent = null;

        if (outcome.Transport is not null)
        {
            try
            {
                // Short confirmation burst: proves the stream is steady, not a one-off frame.
                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 600 && !cancellationToken.IsCancellationRequested)
                {
                    if (!outcome.Transport.TryReadInputReport(25, out ReadOnlyMemory<byte> frame))
                    {
                        continue;
                    }

                    if (BatteryFrameParser.TryParse(frame.Span, out BatteryReading reading, out _))
                    {
                        confirmedFrames++;
                        minPercent = minPercent is null ? reading.BatteryPercent : Math.Min(minPercent.Value, reading.BatteryPercent);
                        maxPercent = maxPercent is null ? reading.BatteryPercent : Math.Max(maxPercent.Value, reading.BatteryPercent);
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
            OperatingSystem = RuntimeInformation.OSDescription,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeVersion = RuntimeInformation.FrameworkDescription,
            SettingsPath = settingsPath,
            LogDirectory = logDirectory,
            Identity = identity,
            Candidates = candidates.Select(c => c.Describe()).ToList(),
            Outcome = outcome.Kind,
            ProbeElapsedMilliseconds = outcome.ElapsedMilliseconds,
            Attempts = outcome.Attempts,
            SelectedInterfaceId = selected?.Candidate.SanitizedId,
            SelectedInputReportLength = selected?.Candidate.MaxInputReportLength ?? 0,
            SelectedOutputReportLength = selected?.Candidate.MaxOutputReportLength ?? 0,
            HeartbeatSucceeded = selected?.HeartbeatSent ?? false,
            WakeFallbackRequired = outcome.NeededWakeFallback,
            StatusReportReceived = reading is not null,
            BatteryPercent = reading?.BatteryPercent,
            CableConnected = reading?.CableConnected,
            ConfirmedFrames = confirmedFrames,
            ConfirmedMinPercent = minPercent,
            ConfirmedMaxPercent = maxPercent,
            FirstFrameHex = selected?.FirstFrameHex ?? "",
            ConflictingProcesses = FindConflictingProcesses(notes),
            Notes = notes,
        };
    }

    private ControllerIdentity SafeDetectIdentity(List<string> notes)
    {
        try
        {
            return _discovery.DetectIdentity();
        }
        catch (Exception ex)
        {
            notes.Add($"Identity detection failed: {ex.GetType().Name}: {ex.Message}");
            return ControllerIdentity.None;
        }
    }

    private static IReadOnlyList<string> FindConflictingProcesses(List<string> notes)
    {
        var found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in KnownConflictingProcesses)
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        found.Add($"{process.ProcessName} (pid {process.Id})");
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                notes.Add($"Could not enumerate process '{name}': {ex.Message}");
            }
        }

        return found.ToList();
    }

    /// <summary>
    /// Renders a report as plain text that can be pasted into a bug report without editing.
    /// </summary>
    public static string Format(DiagnosticsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();

        sb.AppendLine("=== Cyclone Battery diagnostics ===");
        sb.AppendLine($"Generated (UTC)      : {report.GeneratedAtUtc:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"App version          : {report.AppVersion}");
        sb.AppendLine($"OS                   : {report.OperatingSystem} ({report.OsArchitecture})");
        sb.AppendLine($"Process architecture : {report.ProcessArchitecture}");
        sb.AppendLine($"Runtime              : {report.RuntimeVersion}");
        sb.AppendLine($"Settings file        : {report.SettingsPath}");
        sb.AppendLine($"Log folder           : {report.LogDirectory}");
        sb.AppendLine();

        sb.AppendLine("--- Controller identity ---");
        sb.AppendLine($"Identity             : {report.Identity} ({report.Identity.Describe()})");
        sb.AppendLine($"Expected             : vid=0x{CycloneProtocol.VendorId:X4} pid=0x{CycloneProtocol.ProductId:X4} (XInput)");
        sb.AppendLine();

        sb.AppendLine("--- Matching HID devices ---");
        if (report.Candidates.Count == 0)
        {
            sb.AppendLine("(none)");
        }
        else
        {
            for (int i = 0; i < report.Candidates.Count; i++)
            {
                sb.AppendLine($"[{i}] {report.Candidates[i]}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- Interface validation ---");
        sb.AppendLine($"Outcome              : {report.Outcome}");
        sb.AppendLine($"Probe duration       : {report.ProbeElapsedMilliseconds} ms");

        if (report.Attempts.Count == 0)
        {
            sb.AppendLine("(no candidates probed)");
        }

        for (int i = 0; i < report.Attempts.Count; i++)
        {
            CandidateProbeResult attempt = report.Attempts[i];
            sb.AppendLine($"[{i}] id={attempt.Candidate.SanitizedId} opened={YesNo(attempt.Opened)} " +
                          $"heartbeat={YesNo(attempt.HeartbeatSent)} wake={YesNo(attempt.WakeSent)} " +
                          $"status={YesNo(attempt.StatusReceived)} reports={attempt.ReportsSeen} " +
                          $"ids=[{attempt.ReportIdsSeen}] elapsed={attempt.ElapsedMilliseconds}ms");

            if (!string.IsNullOrWhiteSpace(attempt.OpenError))
            {
                sb.AppendLine($"     open error   : {attempt.OpenError}");
            }

            if (!string.IsNullOrWhiteSpace(attempt.TransportError))
            {
                sb.AppendLine($"     transport    : {attempt.TransportError}");
            }

            if (attempt.LastParseFailure != ParseFailureReason.None)
            {
                sb.AppendLine($"     parse reject : {attempt.LastParseFailure}");
            }

            if (!string.IsNullOrWhiteSpace(attempt.FirstFrameHex))
            {
                sb.AppendLine($"     first frame  : {attempt.FirstFrameHex}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("--- Selected interface ---");
        sb.AppendLine($"Interface id         : {report.SelectedInterfaceId ?? "(none)"}");
        sb.AppendLine($"Input report length  : {report.SelectedInputReportLength} bytes (includes report id)");
        sb.AppendLine($"Output report length : {report.SelectedOutputReportLength} bytes (includes report id)");
        sb.AppendLine($"Heartbeat produced 0x12 : {YesNo(report.HeartbeatSucceeded && !report.WakeFallbackRequired)}");
        sb.AppendLine($"Wake fallback needed    : {YesNo(report.WakeFallbackRequired)}");

        sb.AppendLine();
        sb.AppendLine("--- Battery ---");
        sb.AppendLine($"0x12 report received : {YesNo(report.StatusReportReceived)}");
        sb.AppendLine($"Battery percent      : {(report.BatteryPercent is int percent ? percent.ToString() + "%" : "unknown")}");
        sb.AppendLine($"Cable flag (byte 35) : {CableText(report.CableConnected)}");
        sb.AppendLine($"Confirmed frames     : {report.ConfirmedFrames}" +
                      (report.ConfirmedFrames > 0 ? $" (battery {report.ConfirmedMinPercent}..{report.ConfirmedMaxPercent}%)" : ""));

        if (!string.IsNullOrWhiteSpace(report.FirstFrameHex))
        {
            sb.AppendLine($"First frame hex      : {report.FirstFrameHex}");
        }

        sb.AppendLine();
        sb.AppendLine("--- Environment ---");
        sb.AppendLine(report.ConflictingProcesses.Count == 0
            ? "Competing processes  : none detected"
            : "Competing processes  : " + string.Join(", ", report.ConflictingProcesses));

        if (report.Notes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Notes ---");
            foreach (string note in report.Notes)
            {
                sb.AppendLine(note);
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== End of diagnostics ===");
        return sb.ToString();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string CableText(bool? cable) => cable switch
    {
        true => "1 (cable / external power connected)",
        false => "0 (on battery)",
        null => "unknown",
    };
}
