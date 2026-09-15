using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Settings;

namespace CycloneBattery.App.UI;

/// <summary>
/// The optional compact battery widget.
/// </summary>
/// <remarks>
/// Behaviour requirements met here: borderless, draggable, position persisted, no taskbar
/// button, optional always-on-top, and — importantly — no focus stealing. The window is shown
/// with <c>ShowActivated = false</c> and battery updates only touch bound properties, so an
/// update can never pull focus away from a game.
/// </remarks>
public partial class WidgetWindow : Window
{
    private readonly WidgetViewModel _viewModel = new();
    private bool _allowClose;
    private bool _positionRestored;

    public WidgetWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    /// <summary>Raised after the user finishes dragging, with the new position.</summary>
    public event EventHandler? PositionChanged;

    /// <summary>Applies a new controller state.</summary>
    public void Update(ControllerState state) => _viewModel.Update(state);

    /// <summary>Applies user settings that affect the widget.</summary>
    public void ApplySettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Topmost = settings.WidgetAlwaysOnTop;
        _viewModel.SetLowBatteryThreshold(settings.LowBatteryThresholdPercent);

        if (!settings.HasSavedWidgetPosition)
        {
            return;
        }

        // Restore inside the current work area: a monitor may have been unplugged since.
        Rect workArea = SystemParameters.WorkArea;
        double left = Math.Clamp(settings.WidgetLeft, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        double top = Math.Clamp(settings.WidgetTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
        Left = left;
        Top = top;
        _positionRestored = true;
    }

    /// <summary>Shows the widget without activating it, so it never takes focus.</summary>
    public void ShowWithoutActivation()
    {
        if (IsVisible)
        {
            return;
        }

        if (!_positionRestored)
        {
            PlaceInWorkAreaCorner();
            _positionRestored = true;
        }

        Show();
    }

    /// <summary>Allows the window to be closed for real (application shutdown).</summary>
    public void CloseForShutdown()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the widget only hides it; the application lives in the tray.
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            PositionChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClosing(e);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            // DragMove blocks until the drag ends, so raising the event afterwards captures the
            // final position exactly once.
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Can happen if the button was released before the drag started.
            return;
        }

        PositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PlaceInWorkAreaCorner()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Top + 24;
    }
}
