using System.Windows;
using CycloneBattery.Core.Settings;

namespace CycloneBattery.App.UI;

/// <summary>
/// Settings dialog.
/// </summary>
/// <remarks>
/// The window edits a private draft of <see cref="AppSettings"/> and only exposes the result
/// through <see cref="Result"/> when the user presses OK, so pressing Cancel can never mutate
/// persisted settings.
/// </remarks>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _draft;

    public SettingsWindow(AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);
        InitializeComponent();

        _draft = current.Clone();
        _draft.Normalize();

        ThresholdComboBox.ItemsSource = AppSettings.AllowedLowBatteryThresholds;
        ThresholdComboBox.SelectedItem = _draft.LowBatteryThresholdPercent;

        StartWithWindowsCheckBox.IsChecked = _draft.StartWithWindows;
        ShowWidgetOnStartupCheckBox.IsChecked = _draft.ShowWidgetOnStartup;
        AlwaysOnTopCheckBox.IsChecked = _draft.WidgetAlwaysOnTop;
        HideWhenDisconnectedCheckBox.IsChecked = _draft.HideWidgetWhenDisconnected;
        AlertEnabledCheckBox.IsChecked = _draft.LowBatteryAlertEnabled;

        UpdateThresholdEnabled();
        AlertEnabledCheckBox.Checked += (_, _) => UpdateThresholdEnabled();
        AlertEnabledCheckBox.Unchecked += (_, _) => UpdateThresholdEnabled();

        OkButton.Click += OnOk;
        TestNotificationButton.Click += (_, _) => TestNotificationRequested?.Invoke(this, EventArgs.Empty);
        ForceReconnectButton.Click += (_, _) => ForceReconnectRequested?.Invoke(this, EventArgs.Empty);
        OpenLogFolderButton.Click += (_, _) => OpenLogFolderRequested?.Invoke(this, EventArgs.Empty);
        RunDiagnosticsButton.Click += (_, _) => RunDiagnosticsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Verifies the Windows notification mechanism.</summary>
    public event EventHandler? TestNotificationRequested;

    /// <summary>Drops the current interface and rescans.</summary>
    public event EventHandler? ForceReconnectRequested;

    /// <summary>Opens the log folder in Explorer.</summary>
    public event EventHandler? OpenLogFolderRequested;

    /// <summary>Opens the diagnostics window.</summary>
    public event EventHandler? RunDiagnosticsRequested;

    /// <summary>The edited settings. Only meaningful after <see cref="Window.DialogResult"/> is <see langword="true"/>.</summary>
    public AppSettings Result => _draft;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _draft.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _draft.ShowWidgetOnStartup = ShowWidgetOnStartupCheckBox.IsChecked == true;
        _draft.WidgetAlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        _draft.HideWidgetWhenDisconnected = HideWhenDisconnectedCheckBox.IsChecked == true;
        _draft.LowBatteryAlertEnabled = AlertEnabledCheckBox.IsChecked == true;

        if (ThresholdComboBox.SelectedItem is int threshold)
        {
            _draft.LowBatteryThresholdPercent = threshold;
        }

        _draft.Normalize();
        DialogResult = true;
    }

    private void UpdateThresholdEnabled() => ThresholdComboBox.IsEnabled = AlertEnabledCheckBox.IsChecked == true;
}
