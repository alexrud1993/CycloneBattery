using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;

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
    /// <summary>Message shown when another program owns the HID interface.</summary>
    public const string BusyMessage = "Controller interface is busy. Close GameSir Connect and retry.";

    /// <summary>Message shown while the interface is open but the controller is not streaming.</summary>
    public const string WaitingForStatusMessage = "Interface opened, waiting for 0x12 status report";

    /// <summary>Message shown after a stale reading expired.</summary>
    public const string StaleMessage = "No status report received recently";

    private readonly IClock _clock;
    private readonly object _gate = new();

    private ControllerState _current = ControllerState.Disconnected("Starting");

    public ControllerStateMachine(IClock? clock = null) => _clock = clock ?? new SystemClock();

    /// <summary>Raised on the caller's thread whenever <see cref="Current"/> changes.</summary>
    public event EventHandler<ControllerState>? StateChanged;

    /// <summary>How long a connected reading stays valid without a fresh <c>0x12</c> report.</summary>
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

    /// <summary>The current state.</summary>
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

    /// <summary>The last reading that parsed successfully, even if it has since gone stale.</summary>
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

    /// <summary>Rejection reason of the most recent frame that was refused.</summary>
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

    private BatteryReading? _lastKnownReading;
    private ParseFailureReason? _lastParseFailure;
    private TimeSpan _staleAfter = TimeSpan.FromSeconds(12);

    /// <summary>Applies the result of one discovery + validation pass.</summary>
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

    /// <summary>Applies a freshly validated reading while already connected.</summary>
    public void ApplyReading(BatteryReading reading, string? devicePathId)
    {
        lock (_gate)
        {
            _lastKnownReading = reading;
        }

        Transition(ControllerState.Connected(reading, devicePathId));
    }

    /// <summary>
    /// Records that a frame was rejected. The current state is deliberately left untouched so a
    /// single bad packet cannot blank the UI.
    /// </summary>
    public void NoteRejectedFrame(ParseFailureReason reason)
    {
        lock (_gate)
        {
            _lastParseFailure = reason;
        }
    }

    /// <summary>Records a transport-level failure without leaving the recoverable states.</summary>
    public void NoteTransportFailure(string message) => Transition(ControllerState.Error(message));

    /// <summary>
    /// Moves a connected state to <see cref="ControllerStateKind.Disconnected"/> once its reading
    /// is older than <see cref="StaleAfter"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the state actually changed.</returns>
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

    /// <summary>Forces the state back to disconnected, e.g. when the app is shutting down.</summary>
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

        // Raised outside the lock: subscribers marshal to the UI thread and must not deadlock us.
        handler?.Invoke(this, next);
    }
}
