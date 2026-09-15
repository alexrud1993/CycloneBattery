namespace CycloneBattery.App.Services;

/// <summary>
/// Guarantees a single running instance using a named mutex, and lets a second launch ask the
/// first one to show the widget.
/// </summary>
/// <remarks>
/// Mutex name per specification: <c>Local\CycloneBattery.SingleInstance</c>. The <c>Local\</c>
/// prefix keeps it per-session, which is the right scope for a tray utility.
/// </remarks>
public sealed class SingleInstanceGuard : IDisposable
{
    /// <summary>Name of the mutex that marks "an instance is already running".</summary>
    public const string MutexName = @"Local\CycloneBattery.SingleInstance";

    /// <summary>Name of the event used to tell the running instance to show its widget.</summary>
    public const string ShowWidgetEventName = @"Local\CycloneBattery.ShowWidget";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showWidgetEvent;
    private readonly Thread _listener;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle showWidgetEvent, Action onShowWidgetRequested)
    {
        _mutex = mutex;
        _showWidgetEvent = showWidgetEvent;

        _listener = new Thread(() => Listen(onShowWidgetRequested))
        {
            IsBackground = true,
            Name = "SingleInstanceListener",
        };

        _listener.Start();
    }

    /// <summary><see langword="true"/> when this process is the first instance.</summary>
    public bool IsFirstInstance { get; private set; }

    /// <summary>
    /// Tries to become the single instance.
    /// </summary>
    /// <param name="onShowWidgetRequested">
    /// Invoked on a background thread when another launch asks this instance to show the widget.
    /// </param>
    /// <returns>The guard. Check <see cref="IsFirstInstance"/>.</returns>
    public static SingleInstanceGuard Acquire(Action onShowWidgetRequested)
    {
        ArgumentNullException.ThrowIfNull(onShowWidgetRequested);

        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWidgetEventName);

        var guard = new SingleInstanceGuard(mutex, showEvent, onShowWidgetRequested)
        {
            IsFirstInstance = createdNew,
        };

        if (!createdNew)
        {
            // Not the first instance: ask the owner to surface itself, then stop listening.
            try
            {
                showEvent.Set();
            }
            catch (Exception)
            {
                // The other instance may have exited in the meantime; nothing to do.
            }

            guard.StopListening();
        }

        return guard;
    }

    private void Listen(Action onShowWidgetRequested)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                int index = WaitHandle.WaitAny(
                    [_showWidgetEvent, _cts.Token.WaitHandle],
                    TimeSpan.FromMilliseconds(500));

                if (index == 0)
                {
                    onShowWidgetRequested();
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    private void StopListening()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopListening();

        try
        {
            _showWidgetEvent.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Ignore.
        }

        try
        {
            if (IsFirstInstance)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
            // Not owned; nothing to release.
        }
        finally
        {
            _mutex.Dispose();
            _cts.Dispose();
        }
    }
}
