using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CycloneBattery.Core.Models;

namespace CycloneBattery.App.Tray;

/// <summary>Visual state of the tray icon.</summary>
public enum BatteryIconKind
{
    /// <summary>Connected, charge above the medium band.</summary>
    Healthy = 0,

    /// <summary>Connected, moderate charge.</summary>
    Medium,

    /// <summary>Connected, at or below the user's low-battery threshold.</summary>
    Low,

    /// <summary>Connected with cable / external power.</summary>
    Charging,

    /// <summary>Working on it: connecting, busy, error or unsupported mode.</summary>
    Unknown,

    /// <summary>Nothing connected.</summary>
    Disconnected,
}

/// <summary>
/// Draws the tray icons at runtime.
/// </summary>
/// <remarks>
/// All artwork is generated here: a generic battery outline plus a charge fill. No GameSir
/// artwork, logo or other proprietary asset is used anywhere in this project.
/// </remarks>
public sealed class TrayIconFactory : IDisposable
{
    private const int Size = 32;

    private static readonly Color HealthyColor = Color.FromArgb(76, 175, 80);
    private static readonly Color MediumColor = Color.FromArgb(255, 193, 7);
    private static readonly Color LowColor = Color.FromArgb(244, 67, 54);
    private static readonly Color ChargingColor = Color.FromArgb(33, 150, 243);
    private static readonly Color OutlineColor = Color.FromArgb(235, 235, 235);
    private static readonly Color MutedColor = Color.FromArgb(120, 120, 120);

    private readonly Dictionary<BatteryIconKind, Icon> _cache = [];
    private bool _disposed;

    /// <summary>Picks the icon kind for a state.</summary>
    public static BatteryIconKind Classify(ControllerState state, int lowBatteryThresholdPercent)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.HasBattery)
        {
            return state.Kind == ControllerStateKind.Disconnected
                ? BatteryIconKind.Disconnected
                : BatteryIconKind.Unknown;
        }

        if (state.CableConnected == true)
        {
            return BatteryIconKind.Charging;
        }

        int percent = state.BatteryPercent!.Value;
        if (percent <= lowBatteryThresholdPercent)
        {
            return BatteryIconKind.Low;
        }

        return percent <= 40 ? BatteryIconKind.Medium : BatteryIconKind.Healthy;
    }

    /// <summary>Returns a cached icon for <paramref name="kind"/>.</summary>
    public Icon Get(BatteryIconKind kind)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_cache.TryGetValue(kind, out Icon? cached))
        {
            return cached;
        }

        Icon icon = CreateIcon(kind);
        _cache[kind] = icon;
        return icon;
    }

    private static Icon CreateIcon(BatteryIconKind kind)
    {
        using var bitmap = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            Draw(graphics, kind);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            // Clone so the returned Icon owns its data, then release the GDI handle.
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void Draw(Graphics graphics, BatteryIconKind kind)
    {
        using var outline = new Pen(kind == BatteryIconKind.Disconnected ? MutedColor : OutlineColor, 2f);
        var body = new Rectangle(3, 9, 22, 14);
        var terminal = new Rectangle(25, 13, 4, 6);

        using (var terminalBrush = new SolidBrush(kind == BatteryIconKind.Disconnected ? MutedColor : OutlineColor))
        {
            graphics.FillRectangle(terminalBrush, terminal);
        }

        graphics.DrawRectangle(outline, body);

        if (kind is BatteryIconKind.Disconnected or BatteryIconKind.Unknown)
        {
            // Empty shell; for the disconnected state add a slash so it reads at 16 px.
            if (kind == BatteryIconKind.Disconnected)
            {
                using var slash = new Pen(MutedColor, 2f);
                graphics.DrawLine(slash, body.Left - 1, body.Bottom + 1, body.Right + 3, body.Top - 1);
            }

            return;
        }

        Color fill = kind switch
        {
            BatteryIconKind.Low => LowColor,
            BatteryIconKind.Medium => MediumColor,
            BatteryIconKind.Charging => ChargingColor,
            _ => HealthyColor,
        };

        int fillWidth = kind switch
        {
            BatteryIconKind.Low => 5,
            BatteryIconKind.Medium => 11,
            _ => 18,
        };

        using (var fillBrush = new SolidBrush(fill))
        {
            graphics.FillRectangle(fillBrush, body.Left + 2, body.Top + 2, fillWidth, body.Height - 4);
        }

        if (kind == BatteryIconKind.Charging)
        {
            using var bolt = new SolidBrush(Color.White);
            PointF[] points =
            [
                new(body.Left + 13, body.Top + 1),
                new(body.Left + 7, body.Top + 8),
                new(body.Left + 11, body.Top + 8),
                new(body.Left + 9, body.Top + 13),
                new(body.Left + 15, body.Top + 6),
                new(body.Left + 11, body.Top + 6),
            ];
            graphics.FillPolygon(bolt, points);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (Icon icon in _cache.Values)
        {
            icon.Dispose();
        }

        _cache.Clear();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
