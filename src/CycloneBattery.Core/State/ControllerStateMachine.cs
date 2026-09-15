using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;
using CycloneBattery.Core.Services;

namespace CycloneBattery.Core.State;

/// <summary>
/// The single place where controller state transitions are decided.
/// </summary>
/// <remarks>
/// <para>
/// This type contains no I/O and no timers: callers feed it probe outcomes, readings and time,
/// and it publishes state changes. That is what makes the state rules unit-testable without a
/// controller.
/// </para>
/// <para>
/// Two rules from the specification matter most here:
/// </para>
/// <list type="bullet">
///   <item><description>a rejected frame never erases the last good reading immediately — the
///   reading only expires through <see cref="ExpireStaleReading"/>;</description></item>
///   <item><description>an invalid battery value is never clamped or faked.</description></item>
/// </list>
/// </remarks>
public sealed class ControllerStateMachine
{
    public const string BusyMessage = "Controller interface is busy. Close GameSir Connect and retry.";
    public const string WaitingForStatusMessage = "Interface opened, waiting for 0x12 status report";
    public const string StaleMessage = "No status report received recently";

    private readonly IClock _clock;
    private readonly object _gate = new();
    private ControllerState _current = ControllerState.Disconnected("Starting");
    private BatteryReading? _lastKnownReading;
    private ParseFailureReason? _lastParseFailure;
    private TimeSpan _staleAfter = TimeSpan.FromSeconds(12);

    public ControllerStateMachine(IClock? clock = null) => _clock = clock ?? new SystemClock();

    public event EventHandler<ControllerState>? StateChanged;

    public TimeSpan StaleAfter
    {
        get
        {
            lock (_gate)
            {
                return _staleAfter;
            }
        }
        set
        {
            lock (_gate)
            {
                _staleAfter = value;
            }
        }
    }

    public ControllerState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public BatteryReading? LastKnownReading
    {
        get
        {
            lock (_gate)
            {
                return _lastKnownReading;
            }
        }
    }

    public ParseFailureReason? LastParseFailure
    {
        get
        {
            lock (_gate)
            {
                return _lastParseFailure;
            }
        }
    }

    public void ApplyProbeOutcome(InterfaceProbeOutcome outcome, ControllerIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        ControllerState next = outcome.Kind switch
        {
            ProbeOutcomeKind.Success when outcome.FirstReading is { } reading =>
                ControllerState.Connected(reading, outcome.Transport?.Candidate.SanitizedId),
            ProbeOutcomeKind.NoCandidates => ControllerState.Disconnected(identity.Describe()),
            ProbeOutcomeKind.AllBusy => ControllerState.Busy(BusyMessage),
            ProbeOutcomeKind.OpenedButNoStatus => ControllerState.Connecting(
                WaitingForStatusMessage,
                outcome.Attempts.FirstOrDefault(a => a.Opened)?.Candidate.SanitizedId),
            _ => ControllerState.Error(DescribeTransportFailure(outcome)),
        };

        if (outcome.FirstReading is { } freshReading)
        {
            lock (_gate)
            {
                _lastKnownReading = freshReading;
            }
        }

        Transition(next);
    }

    public void ApplyReading(BatteryReading reading, string? devicePathId)
    {
        lock (_gate)
        {
            _lastKnownReading = reading;
        }

        Transition(ControllerState.Connected(reading, devicePathId));
    }

    public void NoteRejectedFrame(ParseFailureReason reason)
    {
        lock (_gate)
        {
            _lastParseFailure = reason;
        }
    }

    public void NoteTransportFailure(string message) => Transition(ControllerState.Error(message));

    public bool ExpireStaleReading()
    {
        ControllerState snapshot = Current;
        if (snapshot.Kind is not ControllerStateKind.Connected)
        {
            return false;
        }

        DateTimeOffset? lastUpdated = snapshot.LastUpdated;
        if (lastUpdated is null)
        {
            return false;
        }

        if (_clock.UtcNow - lastUpdated.Value < StaleAfter)
        {
            return false;
        }

        ControllerState previous = snapshot;
        Transition(ControllerState.Disconnected(StaleMessage));
        return !Equals(previous, Current);
    }

    public void Reset(string reason) => Transition(ControllerState.Disconnected(reason));

    private static string DescribeTransportFailure(InterfaceProbeOutcome outcome)
    {
        string? firstError = outcome.Attempts
            .Select(a => a.TransportError ?? a.OpenError)
            .FirstOrDefault(e => !string.IsNullOrWhiteSpace(e));

        return firstError is null ? "HID transport failed" : $"HID transport failed: {firstError}";
    }

    private void Transition(ControllerState next)
    {
        EventHandler<ControllerState>? handler;

        lock (_gate)
        {
            if (Equals(_current, next))
            {
                return;
            }

            _current = next;
            handler = StateChanged;
        }

        handler?.Invoke(this, next);
    }
}
