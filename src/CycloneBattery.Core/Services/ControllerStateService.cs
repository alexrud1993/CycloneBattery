using System.Diagnostics;
using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Logging;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;
using CycloneBattery.Core.State;

namespace CycloneBattery.Core.Services;

/// <summary>
/// Keeps <see cref="ControllerStateMachine"/> fed with real HID data on a background thread.
/// </summary>
/// <remarks>
/// <para>
/// Two phases alternate:
/// </para>
/// <list type="number">
///   <item><description><b>Discovery</b> — enumerate candidates, validate them with
///   <see cref="CycloneInterfaceProber"/>, keep the first interface that streams
///   <c>0x12</c>.</description></item>
///   <item><description><b>Connected</b> — send the heartbeat about once a second and consume
///   incoming reports. When reports stop, one safe reinitialization is attempted before the
///   interface is dropped and rediscovered.</description></item>
/// </list>
/// <para>
/// Reads block with a short timeout instead of spinning, so an idle connected app costs
/// effectively no CPU.
/// </para>
/// </remarks>
public sealed class ControllerStateService : IControllerStateService
{
    private readonly IHidDeviceDiscovery _discovery;
    private readonly CycloneInterfaceProber _prober;
    private readonly ControllerStateMachine _machine;
    private readonly ILogSink _log;
    private readonly IClock _clock;
    private readonly ControllerServiceOptions _options;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly object _gate = new();

    private ICycloneHidTransport? _transport;
    private string? _devicePathId;
    private int _consecutiveQuietWindows;
    private int? _lastLoggedPercent;
    private bool? _lastLoggedCable;
    private CancellationTokenSource? _runCts;
    private Task? _loopTask;
    private TaskCompletionSource? _wakeSignal;
    private bool _disposed;

    public ControllerStateService(
        IHidDeviceDiscovery discovery,
        ICycloneHidTransportFactory transportFactory,
        ControllerStateMachine stateMachine,
        ControllerServiceOptions? options = null,
        ILogSink? log = null,
        IClock? clock = null)
        : this(discovery, new CycloneInterfaceProber(transportFactory), stateMachine, options, log, clock)
    {
    }

    public ControllerStateService(
        IHidDeviceDiscovery discovery,
        CycloneInterfaceProber prober,
        ControllerStateMachine stateMachine,
        ControllerServiceOptions? options = null,
        ILogSink? log = null,
        IClock? clock = null)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _prober = prober ?? throw new ArgumentNullException(nameof(prober));
        _machine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _options = options ?? ControllerServiceOptions.Default;
        _log = log ?? NullLogSink.Instance;
        _clock = clock ?? new SystemClock();
        _machine.StaleAfter = _options.StaleAfter;
        _machine.StateChanged += OnMachineStateChanged;
    }

    /// <inheritdoc />
    public event EventHandler<ControllerState>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<BatteryReading>? ReadingUpdated;

    /// <inheritdoc />
    public ControllerState State => _machine.Current;

    /// <inheritdoc />
    public BatteryReading? LastKnownReading => _machine.LastKnownReading;

    /// <summary>The interface currently in use, for diagnostics.</summary>
    public string? CurrentDevicePathId
    {
        get
        {
            lock (_gate)
            {
                return _devicePathId;
            }
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_gate)
        {
            if (_loopTask is not null)
            {
                return Task.CompletedTask;
            }

            _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = _runCts.Token;
            _loopTask = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        }

        _log.Info(nameof(ControllerStateService), "Controller monitoring started");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        Task? loop;
        CancellationTokenSource? cts;

        lock (_gate)
        {
            loop = _loopTask;
            cts = _runCts;
            _loopTask = null;
            _runCts = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        cts?.Dispose();
        DropTransport("monitoring stopped");
        SignalWake();
        _log.Info(nameof(ControllerStateService), "Controller monitoring stopped");
    }

    /// <inheritdoc />
    public Task ForceReconnectAsync()
    {
        DropTransport("forced reconnect");
        _machine.Reset("Reconnecting");
        SignalWake();
        _log.Info(nameof(ControllerStateService), "Forced reconnect requested");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ControllerState> RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Always on the thread pool: the UI thread must never block on HID I/O.
            if (_transport is null)
            {
                await Task.Run(() => ProbeAndAttach(cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await Task.Run(() => PumpConnected(_options.HeartbeatInterval, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }

            _machine.ExpireStaleReading();
            return _machine.Current;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _machine.StateChanged -= OnMachineStateChanged;
        await StopAsync().ConfigureAwait(false);
        _ioGate.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (_transport is null)
                    {
                        await Task.Run(() => ProbeAndAttach(cancellationToken), cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Run(() => PumpConnected(_options.HeartbeatInterval, cancellationToken), cancellationToken)
                            .ConfigureAwait(false);
                        _machine.ExpireStaleReading();
                    }
                }
                finally
                {
                    _ioGate.Release();
                }

                TimeSpan delay = DelayFor(_machine.Current.Kind);
                if (delay > TimeSpan.Zero)
                {
                    await WaitAsync(delay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _log.Error(nameof(ControllerStateService), "Controller loop failed", ex);
        }
        finally
        {
            DropTransport("loop exited");
        }
    }

    private void OnMachineStateChanged(object? sender, ControllerState state) => StateChanged?.Invoke(this, state);

    private TimeSpan DelayFor(ControllerStateKind kind) => kind switch
    {
        ControllerStateKind.Connected => TimeSpan.Zero,
        ControllerStateKind.Connecting => _options.ConnectingRetryInterval,
        ControllerStateKind.Disconnected => _options.DisconnectedScanInterval,
        ControllerStateKind.Busy => _options.BusyRetryInterval,
        ControllerStateKind.Error => _options.ErrorRetryInterval,
        ControllerStateKind.UnsupportedMode => _options.UnsupportedModeRetryInterval,
        _ => _options.DisconnectedScanInterval,
    };

    private void ProbeAndAttach(CancellationToken cancellationToken)
    {
        ControllerIdentity identity = DetectIdentitySafe();

        IReadOnlyList<HidDeviceCandidate> candidates;
        try
        {
            candidates = _discovery.EnumerateCandidates();
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(ControllerStateService), $"HID enumeration failed: {ex.Message}");
            _machine.NoteTransportFailure($"HID enumeration failed: {ex.Message}");
            return;
        }

        if (candidates.Count == 0)
        {
            _machine.ApplyProbeOutcome(InterfaceProbeOutcome.NoCandidates(), identity);
            return;
        }

        _log.Info(nameof(ControllerStateService), $"Validating {candidates.Count} candidate interface(s)");

        InterfaceProbeOutcome outcome;
        try
        {
            outcome = _prober.Probe(candidates, _options.Probe, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(ControllerStateService), $"Interface validation failed: {ex.Message}");
            _machine.NoteTransportFailure($"Interface validation failed: {ex.Message}");
            return;
        }

        if (outcome.Kind == ProbeOutcomeKind.Success && outcome.Transport is not null)
        {
            lock (_gate)
            {
                _transport = outcome.Transport;
                _devicePathId = outcome.Transport.Candidate.SanitizedId;
            }

            _consecutiveQuietWindows = 0;
            _log.Info(
                nameof(ControllerStateService),
                $"Connected via interface {_devicePathId} " +
                $"(in={outcome.Transport.Candidate.MaxInputReportLength}B out={outcome.Transport.Candidate.MaxOutputReportLength}B, " +
                $"wakeFallback={outcome.NeededWakeFallback}, probe={outcome.ElapsedMilliseconds}ms)");
        }
        else
        {
            outcome.ReleaseTransport();
            _log.Info(nameof(ControllerStateService), $"No validated interface ({outcome.Kind}, {outcome.ElapsedMilliseconds}ms)");
        }

        _machine.ApplyProbeOutcome(outcome, identity);

        if (outcome.FirstReading is { } reading)
        {
            PublishReading(reading);
        }
    }

    private void PumpConnected(TimeSpan window, CancellationToken cancellationToken)
    {
        ICycloneHidTransport? transport;
        lock (_gate)
        {
            transport = _transport;
        }

        if (transport is null)
        {
            return;
        }

        if (!SendHeartbeat(transport))
        {
            return;
        }

        bool receivedAnything = false;
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < window && !cancellationToken.IsCancellationRequested)
        {
            bool gotFrame;
            try
            {
                gotFrame = transport.TryReadInputReport(_options.ReadSliceMilliseconds, out ReadOnlyMemory<byte> frame);
                if (!gotFrame)
                {
                    continue;
                }

                HandleFrame(frame);
                receivedAnything = true;
            }
            catch (HidTransportException ex)
            {
                HandleTransportFailure(ex);
                return;
            }
        }

        if (receivedAnything)
        {
            _consecutiveQuietWindows = 0;
            return;
        }

        _consecutiveQuietWindows++;

        if (_consecutiveQuietWindows == 1)
        {
            // One safe reinitialization before giving up on this interface.
            _log.Info(nameof(ControllerStateService), "No status reports; sending wake fallback");
            TryWrite(transport, StatusActivationCommand.BuildWake(transport.MaxOutputReportLength), "wake");
            return;
        }

        _log.Info(nameof(ControllerStateService), "Interface stopped streaming; rediscovering");
        DropTransport("interface went quiet");
    }

    private void HandleFrame(ReadOnlyMemory<byte> frame)
    {
        if (BatteryFrameParser.TryParse(frame.Span, out BatteryReading reading, out ParseFailureReason reason))
        {
            BatteryReading stamped = reading.At(_clock.UtcNow);
            _machine.ApplyReading(stamped, CurrentDevicePathId);
            PublishReading(stamped);

            if (_lastLoggedPercent != stamped.BatteryPercent)
            {
                _log.Info(nameof(ControllerStateService), $"Battery {stamped.BatteryPercent}%");
                _lastLoggedPercent = stamped.BatteryPercent;
            }

            if (_lastLoggedCable != stamped.CableConnected)
            {
                _log.Info(nameof(ControllerStateService), $"Cable state: {(stamped.CableConnected ? "connected" : "disconnected")}");
                _lastLoggedCable = stamped.CableConnected;
            }

            return;
        }

        _machine.NoteRejectedFrame(reason);

        if (BatteryFrameParser.IsStatusReport(frame.Span))
        {
            _log.Warning(
                nameof(ControllerStateService),
                $"Rejected 0x12 frame ({reason}, {frame.Length}B): {BatteryFrameParser.ToHexPrefix(frame.Span, 40)}");
        }
    }

    private bool SendHeartbeat(ICycloneHidTransport transport) =>
        TryWrite(transport, StatusActivationCommand.BuildHeartbeat(transport.MaxOutputReportLength), "heartbeat");

    private bool TryWrite(ICycloneHidTransport transport, byte[] report, string what)
    {
        try
        {
            transport.WriteOutputReport(report);
            return true;
        }
        catch (HidTransportException ex)
        {
            HandleTransportFailure(ex, what);
            return false;
        }
    }

    private void HandleTransportFailure(HidTransportException ex, string? during = null)
    {
        string context = during is null ? "" : $" during {during}";
        _log.Warning(nameof(ControllerStateService), $"HID transport failed{context}: {ex.Message}");

        if (ex.LooksBusy)
        {
            DropTransport("interface busy");
            _machine.ApplyProbeOutcome(InterfaceProbeOutcome.Busy(), ControllerIdentity.XInput);
            return;
        }

        DropTransport("transport failure");
        _machine.NoteTransportFailure(ex.Message);
    }

    private void DropTransport(string reason)
    {
        ICycloneHidTransport? transport;
        lock (_gate)
        {
            transport = _transport;
            _transport = null;
            _devicePathId = null;
        }

        if (transport is null)
        {
            return;
        }

        _consecutiveQuietWindows = 0;
        _lastLoggedPercent = null;
        _lastLoggedCable = null;

        try
        {
            transport.Dispose();
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(ControllerStateService), $"Error while releasing interface: {ex.Message}");
        }

        _log.Info(nameof(ControllerStateService), $"Interface released ({reason})");
    }

    private ControllerIdentity DetectIdentitySafe()
    {
        try
        {
            return _discovery.DetectIdentity();
        }
        catch (Exception ex)
        {
            _log.Warning(nameof(ControllerStateService), $"Identity detection failed: {ex.Message}");
            return ControllerIdentity.None;
        }
    }

    private void PublishReading(BatteryReading reading) => ReadingUpdated?.Invoke(this, reading);

    private void SignalWake()
    {
        TaskCompletionSource? signal;
        lock (_gate)
        {
            signal = _wakeSignal;
            _wakeSignal = null;
        }

        signal?.TrySetResult();
    }

    private async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            _wakeSignal ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            signal = _wakeSignal;
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() => signal.TrySetResult());
        await Task.WhenAny(Task.Delay(delay, cancellationToken), signal.Task).ConfigureAwait(false);
    }
}
