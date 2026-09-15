using System.Windows.Forms;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Settings;

namespace CycloneBattery.App.Tray;

/// <summary>
/// Owns the notification-area icon, its tooltip and its context menu.
/// </summary>
/// <remarks>
/// The tray is the primary interface, so it must always be reachable: every menu item is created
/// once and updated in place rather than rebuilt, which avoids the flicker and the handle churn
/// that rebuilding a <see cref="ContextMenuStrip"/> on every battery change would cause.
/// </remarks>
public sealed class TrayIconController : IDisposable
{
    private const int BalloonTimeoutMilliseconds = 6000;

    private readonly NotifyIcon _notifyIcon;
    private readonly TrayIconFactory _icons;

    private readonly ToolStripMenuItem _headerItem;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _widgetItem;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly ToolStripMenuItem _lowBatteryItem;
    private readonly ToolStripMenuItem _alertEnabledItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _diagnosticsItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ToolStripMenuItem[] _thresholdItems;

    private bool _disposed;

    public TrayIconController(TrayIconFactory icons)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));

        _headerItem = new ToolStripMenuItem("Cyclone 2") { Enabled = false };
        _statusItem = new ToolStripMenuItem("Unknown") { Enabled = false };
        _widgetItem = new ToolStripMenuItem("Show widget");
        _refreshItem = new ToolStripMenuItem("Refresh");
        _autostartItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = false };
        _lowBatteryItem = new ToolStripMenuItem("Low battery alert");
        _alertEnabledItem = new ToolStripMenuItem("Enabled") { CheckOnClick = false };
        _settingsItem = new ToolStripMenuItem("Settings…");
        _diagnosticsItem = new ToolStripMenuItem("Diagnostics…");
        _exitItem = new ToolStripMenuItem("Exit");

        _thresholdItems = AppSettings.AllowedLowBatteryThresholds
            .Select(threshold => new ToolStripMenuItem($"{threshold}%") { Tag = threshold })
            .ToArray();

        foreach (ToolStripMenuItem thresholdItem in _thresholdItems)
        {
            thresholdItem.Click += OnThresholdClicked;
        }

        _lowBatteryItem.DropDownItems.Add(_alertEnabledItem);
        _lowBatteryItem.DropDownItems.Add(new ToolStripSeparator());
        foreach (ToolStripMenuItem thresholdItem in _thresholdItems)
        {
            _lowBatteryItem.DropDownItems.Add(thresholdItem);
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(_headerItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_widgetItem);
        menu.Items.Add(_refreshItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autostartItem);
        menu.Items.Add(_lowBatteryItem);
        menu.Items.Add(_settingsItem);
        menu.Items.Add(_diagnosticsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_exitItem);

        _notifyIcon = new NotifyIcon
        {
            Text = "Cyclone 2 — starting",
            Visible = true,
            ContextMenuStrip = menu,
            Icon = _icons.Get(BatteryIconKind.Unknown),
        };

        _notifyIcon.DoubleClick += OnDoubleClick;
        _widgetItem.Click += (_, _) => ShowHideWidgetRequested?.Invoke(this, EventArgs.Empty);
        _refreshItem.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        _autostartItem.Click += (_, _) => ToggleAutostartRequested?.Invoke(this, EventArgs.Empty);
        _alertEnabledItem.Click += (_, _) => ToggleLowBatteryAlertRequested?.Invoke(this, EventArgs.Empty);
        _settingsItem.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _diagnosticsItem.Click += (_, _) => DiagnosticsRequested?.Invoke(this, EventArgs.Empty);
        _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Show or hide the mini widget.</summary>
    public event EventHandler? ShowHideWidgetRequested;

    /// <summary>Force an immediate rescan.</summary>
    public event EventHandler? RefreshRequested;

    /// <summary>Toggle the Windows autostart entry.</summary>
    public event EventHandler? ToggleAutostartRequested;

    /// <summary>Toggle the low-battery alert.</summary>
    public event EventHandler? ToggleLowBatteryAlertRequested;

    /// <summary>A threshold entry was picked from the submenu.</summary>
    public event EventHandler<int>? LowBatteryThresholdSelected;

    /// <summary>Open the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Open the diagnostics window.</summary>
    public event EventHandler? DiagnosticsRequested;

    /// <summary>Exit the application.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>Updates icon, tooltip and menu text. Must be called on the UI thread.</summary>
    public void Update(ControllerState state, AppSettings settings, bool widgetVisible)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);

        int threshold = settings.LowBatteryThresholdPercent;
        _notifyIcon.Icon = _icons.Get(TrayIconFactory.Classify(state, threshold));
        _notifyIcon.Text = BuildTooltip(state);

        _statusItem.Text = BuildStatusLine(state);
        _widgetItem.Text = widgetVisible ? "Hide widget" : "Show widget";
        _autostartItem.Checked = settings.StartWithWindows;
        _alertEnabledItem.Checked = settings.LowBatteryAlertEnabled;

        foreach (ToolStripMenuItem thresholdItem in _thresholdItems)
        {
            thresholdItem.Checked = settings.LowBatteryAlertEnabled
                && thresholdItem.Tag is int value
                && value == threshold;
        }

        _lowBatteryItem.Enabled = true;
    }

    /// <summary>Shows a balloon notification. Used for the low-battery alert and the test action.</summary>
    public void ShowBalloon(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMilliseconds, title, message, icon);
    }

    /// <summary>Tooltip text per specification.</summary>
    public static string BuildTooltip(ControllerState state)
    {
        if (!state.HasBattery)
        {
            return state.Kind switch
            {
                ControllerStateKind.Busy => "Cyclone 2 — Interface busy",
                ControllerStateKind.Connecting => "Cyclone 2 — Connecting",
                ControllerStateKind.UnsupportedMode => "Cyclone 2 — Unsupported mode",
                ControllerStateKind.Error => "Cyclone 2 — Error",
                _ => "Cyclone 2 — Disconnected",
            };
        }

        string text = $"Cyclone 2 — {state.BatteryPercent}%";
        return state.CableConnected == true ? text + " — Charging" : text;
    }

    private static string BuildStatusLine(ControllerState state)
    {
        if (!state.HasBattery)
        {
            return state.Message is { Length: > 0 } message ? message : state.Kind.ToString();
        }

        string power = state.CableConnected switch
        {
            true => "Charging",
            false => "On battery",
            null => "Power state unknown",
        };

        return $"{state.BatteryPercent}% · {power}";
    }

    private void OnDoubleClick(object? sender, EventArgs e) => ShowHideWidgetRequested?.Invoke(this, EventArgs.Empty);

    private void OnThresholdClicked(object? sender, EventArgs e)
    {
        if (sender is ToolStripMenuItem { Tag: int threshold })
        {
            LowBatteryThresholdSelected?.Invoke(this, threshold);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
