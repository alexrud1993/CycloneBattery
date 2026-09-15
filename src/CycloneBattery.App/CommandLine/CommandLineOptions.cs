namespace CycloneBattery.App.CommandLine;

/// <summary>Parsed command line.</summary>
/// <param name="Diagnostics">Run the hardware diagnostics flow instead of the tray application.</param>
/// <param name="SaveDiagnostics">Also write the diagnostics report to the log folder.</param>
/// <param name="Minimized">Autostart mode: do not show the widget even if the setting is on.</param>
/// <param name="Unknown">Arguments that were not recognized, surfaced in diagnostics output.</param>
public sealed record CommandLineOptions(
    bool Diagnostics,
    bool SaveDiagnostics,
    bool Minimized,
    IReadOnlyList<string> Unknown)
{
    /// <summary>Diagnostics flag accepted by the specification.</summary>
    public const string DiagnosticsSwitch = "--diagnostics";

    /// <summary>Optional flag that saves the report next to the logs.</summary>
    public const string SaveSwitch = "--save";

    /// <summary>Flag written into the autostart Run value.</summary>
    public const string MinimizedSwitch = "--minimized";

    /// <summary>Empty options, used when no arguments were supplied.</summary>
    public static CommandLineOptions None { get; } = new(false, false, false, Array.Empty<string>());

    /// <summary>Parses <c>--diagnostics</c>, <c>--save</c> and <c>--minimized</c> (case-insensitive).</summary>
    public static CommandLineOptions Parse(IEnumerable<string>? args)
    {
        var unknown = new List<string>();
        bool diagnostics = false;
        bool save = false;
        bool minimized = false;

        if (args is not null)
        {
            foreach (string raw in args)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string arg = raw.Trim();
                if (string.Equals(arg, DiagnosticsSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics = true;
                }
                else if (string.Equals(arg, SaveSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    save = true;
                }
                else if (string.Equals(arg, MinimizedSwitch, StringComparison.OrdinalIgnoreCase))
                {
                    minimized = true;
                }
                else
                {
                    unknown.Add(arg);
                }
            }
        }

        return new CommandLineOptions(diagnostics, save, minimized, unknown);
    }
}
