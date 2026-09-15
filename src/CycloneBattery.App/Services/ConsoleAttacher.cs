using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CycloneBattery.App.Services;

/// <summary>
/// Attaches a windowed (WinExe) process to the console it was started from.
/// </summary>
/// <remarks>
/// <c>CycloneBattery.exe --diagnostics</c> is meant to be run from PowerShell, but a WPF
/// executable has no console of its own. Attaching to the parent process makes
/// <see cref="Console"/> writes appear in the user's terminal.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class ConsoleAttacher
{
    private const int AttachParentProcess = -1;

    /// <summary>
    /// Attaches to the parent console and rewires <see cref="Console.Out"/>.
    /// </summary>
    /// <returns><see langword="true"/> when console output is now available.</returns>
    internal static bool TryAttach()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            return false;
        }

        try
        {
            var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(writer);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}
