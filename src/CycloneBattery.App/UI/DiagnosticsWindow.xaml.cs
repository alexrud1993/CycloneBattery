using System.Windows;

namespace CycloneBattery.App.UI;

/// <summary>
/// Shows the diagnostics report and lets the user copy or save it.
/// </summary>
/// <remarks>
/// The report itself is produced by <c>CycloneBattery.Core</c>; this window only renders it and
/// runs the work on a background task so the UI never blocks on HID I/O.
/// </remarks>
public partial class DiagnosticsWindow : Window
{
    private readonly Func<CancellationToken, Task<string>> _runDiagnostics;
    private CancellationTokenSource? _currentRun;

    public DiagnosticsWindow(Func<CancellationToken, Task<string>> runDiagnostics)
    {
        _runDiagnostics = runDiagnostics ?? throw new ArgumentNullException(nameof(runDiagnostics));
        InitializeComponent();

        RunAgainButton.Click += async (_, _) => await RunAsync().ConfigureAwait(true);
        CopyButton.Click += OnCopy;
        SaveButton.Click += OnSave;
    }

    /// <summary>Raised when the user asks for the report to be written to the log folder.</summary>
    public event EventHandler<string>? SaveRequested;

    /// <summary>Runs the diagnostics pass and renders the result.</summary>
    public async Task RunAsync()
    {
        _currentRun?.Cancel();
        using var cts = new CancellationTokenSource();
        _currentRun = cts;

        RunAgainButton.IsEnabled = false;
        StatusText.Text = "Probing hardware…";

        try
        {
            string report = await _runDiagnostics(cts.Token).ConfigureAwait(true);
            OutputBox.Text = report;
            StatusText.Text = $"Completed at {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            OutputBox.Text = "Diagnostics failed: " + ex;
            StatusText.Text = "Failed";
        }
        finally
        {
            RunAgainButton.IsEnabled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _currentRun?.Cancel();
        _currentRun?.Dispose();
        _currentRun = null;
        base.OnClosed(e);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(OutputBox.Text);
            StatusText.Text = "Copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Copy failed: " + ex.Message;
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => SaveRequested?.Invoke(this, OutputBox.Text);
}
