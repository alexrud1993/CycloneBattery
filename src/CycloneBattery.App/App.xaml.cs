using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CycloneBattery.App.CommandLine;
using CycloneBattery.App.Notifications;
using CycloneBattery.App.Services;
using CycloneBattery.App.Tray;
using CycloneBattery.App.UI;
using CycloneBattery.Core;
using CycloneBattery.Core.Diagnostics;
using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Logging;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Protocol;
using CycloneBattery.Core.Services;
using CycloneBattery.Core.Settings;
using CycloneBattery.Core.State;

namespace CycloneBattery.App;

/// <summary>
/// Composition root and lifetime owner for the tray application.
/// </summary>
/// <remarks>
/// All hardware work stays in <c>CycloneBattery.Core</c> and runs on background threads; this
/// class only wires the pieces together and marshals state changes onto the UI thread.
/// </remarks>
public partial class App : Application
{
    private CommandLineOptions _options = CommandLineOptions.None;
    private SingleInstanceGuard? _singleInstance;
    private FileLogSink? _log;
    private JsonAppSettingsService? _settings;
    private ControllerStateService? _stateService;
    private TrayIconFactory? _iconFactory;
    private TrayIconController? _tray;
    private WidgetWindow? _widget;
    private BatteryAlertMonitor? _alertMonitor;
    private TrayNotificationService? _notifications;
    private RegistryAutostartService? _autostart;
    private SettingsWindow? _settingsWindow;
    private DiagnosticsWindow? _diagnosticsWindow;
    private bool _diagnosticsSaved;
    private bool _shuttingDown;

    /// <summary>Version string printed in diagnostics and the log.</summary>
    public static string Version =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? typeof(App).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _options = CommandLineOptions.Parse(e.Args);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        TryEnsureDirectories();
        _log = new FileLogSink(AppPaths.LogDirectory);
        _log.Info(nameof(App), $"Cyclone Battery {Version} starting (args: {(e.Args.Length == 0 ? "<none>" : string.Join(' ', e.Args))})");

        foreach (string unknown in _options.Unknown)
        {
            _log.Warning(nameof(App), $"Ignoring unrecognized argument: {unknown}");
        }

        if (_options.Diagnostics)
        {
            StartDiagnosticsMode();
            return;
        }

        _singleInstance = SingleInstanceGuard.Acquire(OnShowWidgetRequestedFromOtherInstance);
        if (!_singleInstance.IsFirstInstance)
        {
            _log.Info(nameof(App), "Another instance is already running; signalling it and exiting");
            Shutdown();
            return;
        }

        _settings = new JsonAppSettingsService(new FileSettingsStore(AppPaths.SettingsFilePath), _log);

        var discovery = new HidSharpDeviceDiscovery();
        _stateService = new ControllerStateService(
            discovery,
            discovery,
            new ControllerStateMachine(),
            ControllerServiceOptions.Default,
            _log);

        _alertMonitor = new BatteryAlertMonitor();
        _alertMonitor.Configure(_settings.Current);
        _alertMonitor.AlertRaised += OnLowBatteryAlert;

        _iconFactory = new TrayIconFactory();
        _tray = new TrayIconController(_iconFactory);
        _notifications = new TrayNotificationService(_tray);
        _autostart = new RegistryAutostartService(CurrentExecutablePath, _log);
        WireTray();

        _widget = new WidgetWindow();
        _widget.ApplySettings(_settings.Current);
        _widget.PositionChanged += OnWidgetPositionChanged;

        _stateService.StateChanged += OnControllerStateChanged;
        _stateService.ReadingUpdated += OnReadingUpdated;

        SynchronizeAutostartWithSettings();
        UpdateTray(_stateService.State);
        _widget.Update(_stateService.State);

        if (!_options.Minimized && _settings.Current.ShowWidgetOnStartup)
        {
            ShowWidget();
        }

        _ = _stateService.StartAsync();
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        _shuttingDown = true;

        try
        {
            if (_stateService is not null)
            {
                _stateService.StateChanged -= OnControllerStateChanged;
                _stateService.ReadingUpdated -= OnReadingUpdated;
                _stateService.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }
        catch (Exception ex)
        {
            _log?.Warning(nameof(App), $"Error while stopping monitoring: {ex.Message}");
        }

        _tray?.Dispose();
        _iconFactory?.Dispose();

        if (_widget is not null)
        {
            _widget.PositionChanged -= OnWidgetPositionChanged;
            _widget.CloseForShutdown();
        }

        _singleInstance?.Dispose();
        _log?.Info(nameof(App), "Shutdown complete");

        base.OnExit(e);
    }

    // ---------------------------------------------------------------- diagnostics mode

    private void StartDiagnosticsMode()
    {
        bool consoleAvailable = ConsoleAttacher.TryAttach();
        _log?.Info(nameof(App), $"Diagnostics mode (console attached: {consoleAvailable}, save: {_options.SaveDiagnostics})");

        var discovery = new HidSharpDeviceDiscovery();
        var runner = new DiagnosticsRunner(discovery, discovery);

        var window = new DiagnosticsWindow(ct => RunDiagnosticsAsync(runner, ct));
        window.SaveRequested += OnSaveDiagnosticsText;
        window.Closed += (_, _) => Shutdown();
        _diagnosticsWindow = window;
        window.Show();

        _ = InitializeDiagnosticsAsync(window);

        async Task InitializeDiagnosticsAsync(DiagnosticsWindow target)
        {
            try
            {
                await target.RunAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log?.Error(nameof(App), "Diagnostics run failed", ex);
            }
        }
    }

    private async Task<string> RunDiagnosticsAsync(DiagnosticsRunner runner, CancellationToken cancellationToken)
    {
        DiagnosticsReport report = await Task
            .Run(() => runner.Run(AppPaths.SettingsFilePath, AppPaths.LogDirectory, Version, cancellationToken), cancellationToken)
            .ConfigureAwait(true);

        string text = DiagnosticsRunner.Format(report);

        try
        {
            Console.Write(text);
        }
        catch (Exception)
        {
            // No console available; the window still shows the report.
        }

        if (_options.SaveDiagnostics && !_diagnosticsSaved)
        {
            _diagnosticsSaved = true;
            string path = SaveDiagnosticsText(text);
            Console.WriteLine($"Diagnostics saved to: {path}");
        }

        return text;
    }

    private string SaveDiagnosticsText(string text)
    {
        try
        {
            AppPaths.EnsureDirectories();
            string path = AppPaths.DiagnosticFilePath();
            File.WriteAllText(path, text);
            _log?.Info(nameof(App), $"Diagnostics saved to {path}");
            return path;
        }
        catch (Exception ex)
        {
            _log?.Error(nameof(App), "Could not save diagnostics", ex);
            return "Could not save diagnostics: " + ex.Message;
        }
    }

    private void OnSaveDiagnosticsText(object? sender, string text)
    {
        string path = SaveDiagnosticsText(text);
        MessageBox.Show("Saved to:\n" + path, "Cyclone Battery", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---------------------------------------------------------------- tray wiring

    private void WireTray()
    {
        if (_tray is null)
        {
            return;
        }

        _tray.ShowHideWidgetRequested += (_, _) => ToggleWidget();
        _tray.RefreshRequested += (_, _) => _ = RefreshAsync();
        _tray.ToggleAutostartRequested += (_, _) => ToggleAutostart();
        _tray.ToggleLowBatteryAlertRequested += (_, _) => ToggleLowBatteryAlert();
        _tray.LowBatteryThresholdSelected += (_, threshold) => SetLowBatteryThreshold(threshold);
        _tray.SettingsRequested += (_, _) => ShowSettingsWindow();
        _tray.DiagnosticsRequested += (_, _) => ShowDiagnosticsWindow();
        _tray.ExitRequested += (_, _) => Shutdown();
    }

    private void OnControllerStateChanged(object? sender, ControllerState state) =>
        Dispatcher.InvokeAsync(() =>
        {
            UpdateTray(state);
            _widget?.Update(state);

            if (state.Kind == ControllerStateKind.Disconnected
                && _settings?.Current.HideWidgetWhenDisconnected == true
                && _widget?.IsVisible == true)
            {
                HideWidget();
            }
        }, DispatcherPriority.Background);

    private void OnReadingUpdated(object? sender, BatteryReading reading) => _alertMonitor?.Process(reading);

    private void OnLowBatteryAlert(object? sender, LowBatteryAlertEventArgs e) =>
        Dispatcher.InvokeAsync(() =>
        {
            _log?.Info(nameof(App), $"Low battery alert at {e.BatteryPercent}% (threshold {e.ThresholdPercent}%)");
            _notifications?.ShowLowBattery(e.BatteryPercent, e.ThresholdPercent);
        });

    private void OnShowWidgetRequestedFromOtherInstance() =>
        Dispatcher.InvokeAsync(() =>
        {
            ShowWidget();
            UpdateTray(_stateService?.State ?? ControllerState.Disconnected());
        });

    private void UpdateTray(ControllerState state)
    {
        if (_tray is null || _settings is null)
        {
            return;
        }

        _tray.Update(state, _settings.Current, _widget?.IsVisible == true);
    }

    private async Task RefreshAsync()
    {
        if (_stateService is null)
        {
            return;
        }

        try
        {
            await _stateService.RefreshNowAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log?.Warning(nameof(App), $"Manual refresh failed: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- widget

    private void ToggleWidget()
    {
        if (_widget is null)
        {
            return;
        }

        if (_widget.IsVisible)
        {
            HideWidget();
        }
        else
        {
            ShowWidget();
        }
    }

    private void ShowWidget()
    {
        if (_widget is null)
        {
            return;
        }

        _widget.ShowWithoutActivation();
        _widget.Update(_stateService?.State ?? ControllerState.Disconnected());
        PersistWidgetState(visible: true);
        UpdateTray(_stateService?.State ?? ControllerState.Disconnected());
    }

    private void HideWidget()
    {
        if (_widget is null)
        {
            return;
        }

        _widget.Hide();
        PersistWidgetState(visible: false);
        UpdateTray(_stateService?.State ?? ControllerState.Disconnected());
    }

    private void OnWidgetPositionChanged(object? sender, EventArgs e)
    {
        if (_widget is null || _settings is null || _shuttingDown)
        {
            return;
        }

        AppSettings settings = _settings.Current.Clone();
        settings.WidgetLeft = _widget.Left;
        settings.WidgetTop = _widget.Top;
        settings.WidgetVisible = _widget.IsVisible;

        try
        {
            _settings.Save(settings);
        }
        catch (Exception ex)
        {
            _log?.Warning(nameof(App), $"Could not persist widget position: {ex.Message}");
        }
    }

    private void PersistWidgetState(bool visible)
    {
        if (_widget is null || _settings is null)
        {
            return;
        }

        AppSettings settings = _settings.Current.Clone();
        settings.WidgetVisible = visible;
        settings.WidgetLeft = _widget.Left;
        settings.WidgetTop = _widget.Top;

        try
        {
            _settings.Save(settings);
        }
        catch (Exception ex)
        {
            _log?.Warning(nameof(App), $"Could not persist widget state: {ex.Message}");
        }
    }

    // ---------------------------------------------------------------- settings / autostart / alerts

    private void ShowSettingsWindow()
    {
        if (_settings is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var window = new SettingsWindow(_settings.Current);
        window.TestNotificationRequested += (_, _) => _notifications?.ShowTestNotification();
        window.ForceReconnectRequested += (_, _) => _ = ForceReconnectAsync();
        window.OpenLogFolderRequested += (_, _) => OpenLogFolder();
        window.RunDiagnosticsRequested += (_, _) => ShowDiagnosticsWindow();
        window.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow = window;

        if (window.ShowDialog() == true)
        {
            ApplySettings(window.Result);
        }

        _settingsWindow = null;
    }

    private void ApplySettings(AppSettings updated)
    {
        if (_settings is null)
        {
            return;
        }

        bool autostartChanged = updated.StartWithWindows != _settings.Current.StartWithWindows;

        try
        {
            _settings.Save(updated);
        }
        catch (Exception ex)
        {
            _log?.Error(nameof(App), "Could not save settings", ex);
            MessageBox.Show("Could not save settings:\n" + ex.Message, "Cyclone Battery", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _alertMonitor?.Configure(updated);
        _widget?.ApplySettings(updated);

        if (updated.WidgetAlwaysOnTop == false && _widget is not null)
        {
            _widget.Topmost = false;
        }

        if (autostartChanged)
        {
            SynchronizeAutostartWithSettings();
        }

        UpdateTray(_stateService?.State ?? ControllerState.Disconnected());
    }

    private void SynchronizeAutostartWithSettings()
    {
        if (_settings is null || _autostart is null)
        {
            return;
        }

        bool desired = _settings.Current.StartWithWindows;
        _autostart.SetEnabled(desired);

        // If the registry write failed, do not leave the checkbox lying to the user.
        if (desired && !_autostart.IsEnabled())
        {
            AppSettings corrected = _settings.Current.Clone();
            corrected.StartWithWindows = false;

            try
            {
                _settings.Save(corrected);
            }
            catch (Exception ex)
            {
                _log?.Warning(nameof(App), $"Could not revert the autostart setting: {ex.Message}");
            }

            _log?.Warning(nameof(App), "Autostart could not be enabled; setting reverted");
        }
    }

    private void ToggleAutostart()
    {
        if (_settings is null)
        {
            return;
        }

        AppSettings updated = _settings.Current.Clone();
        updated.StartWithWindows = !updated.StartWithWindows;
        ApplySettings(updated);
    }

    private void ToggleLowBatteryAlert()
    {
        if (_settings is null)
        {
            return;
        }

        AppSettings updated = _settings.Current.Clone();
        updated.LowBatteryAlertEnabled = !updated.LowBatteryAlertEnabled;
        ApplySettings(updated);
    }

    private void SetLowBatteryThreshold(int threshold)
    {
        if (_settings is null)
        {
            return;
        }

        AppSettings updated = _settings.Current.Clone();
        updated.LowBatteryAlertEnabled = true;
        updated.LowBatteryThresholdPercent = threshold;
        ApplySettings(updated);
    }

    private async Task ForceReconnectAsync()
    {
        if (_stateService is null)
        {
            return;
        }

        _alertMonitor?.Reset();
        await _stateService.ForceReconnectAsync().ConfigureAwait(true);
        await _stateService.RefreshNowAsync().ConfigureAwait(true);
    }

    private void OpenLogFolder()
    {
        try
        {
            AppPaths.EnsureDirectories();
            Process.Start(new ProcessStartInfo(AppPaths.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log?.Warning(nameof(App), $"Could not open the log folder: {ex.Message}");
        }
    }

    private void ShowDiagnosticsWindow()
    {
        if (_diagnosticsWindow is not null)
        {
            _diagnosticsWindow.Activate();
            return;
        }

        var discovery = new HidSharpDeviceDiscovery();
        var runner = new DiagnosticsRunner(discovery, discovery);

        var window = new DiagnosticsWindow(ct => RunDiagnosticsAsync(runner, ct));
        window.SaveRequested += OnSaveDiagnosticsText;
        window.Closed += (_, _) => _diagnosticsWindow = null;
        _diagnosticsWindow = window;
        window.Show();
        _ = RunDiagnosticsWindowAsync(window);

        async Task RunDiagnosticsWindowAsync(DiagnosticsWindow target)
        {
            try
            {
                await target.RunAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log?.Warning(nameof(App), $"Diagnostics run failed: {ex.Message}");
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private static string CurrentExecutablePath =>
        Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "CycloneBattery.exe";

    private void TryEnsureDirectories()
    {
        try
        {
            AppPaths.EnsureDirectories();
        }
        catch (Exception)
        {
            // Logging falls back to a sink that tolerates a missing folder.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error(nameof(App), "Unhandled UI exception", e.Exception);
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _log?.Error(nameof(App), "Unhandled exception", e.ExceptionObject as Exception);

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Error(nameof(App), "Unobserved task exception", e.Exception);
        e.SetObserved();
    }
}
