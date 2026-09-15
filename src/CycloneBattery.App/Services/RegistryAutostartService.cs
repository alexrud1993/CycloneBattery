using Microsoft.Win32;
using CycloneBattery.Core.Logging;
using CycloneBattery.Core.Services;

namespace CycloneBattery.App.Services;

/// <summary>
/// <see cref="IAutostartService"/> that writes the current-user Run value.
/// </summary>
/// <remarks>
/// Only <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> is touched, so no administrator
/// rights are required and disabling the setting removes the value cleanly.
/// </remarks>
public sealed class RegistryAutostartService : IAutostartService
{
    private readonly ILogSink _log;

    public RegistryAutostartService(string executablePath, ILogSink? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _log = log ?? NullLogSink.Instance;
        Entry = AutostartCommandBuilder.Build(executablePath, startSilently: true);
    }

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public AutostartEntry Entry { get; }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        if (!IsSupported)
        {
            return false;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutostartCommandBuilder.RunKeyPath);
            return key?.GetValue(AutostartCommandBuilder.ValueName) is string value
                && AutostartCommandBuilder.TryParseExecutablePath(value) is not null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            _log.Warning(nameof(RegistryAutostartService), $"Could not read the Run value: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public void Enable()
    {
        if (!IsSupported)
        {
            _log.Warning(nameof(RegistryAutostartService), "Autostart is only supported on Windows");
            return;
        }

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(AutostartCommandBuilder.RunKeyPath);
            key.SetValue(AutostartCommandBuilder.ValueName, Entry.ValueData, RegistryValueKind.String);
            _log.Info(nameof(RegistryAutostartService), "Autostart enabled");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            _log.Error(nameof(RegistryAutostartService), "Could not enable autostart", ex);
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        if (!IsSupported)
        {
            return;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AutostartCommandBuilder.RunKeyPath, writable: true);
            key?.DeleteValue(AutostartCommandBuilder.ValueName, throwOnMissingValue: false);
            _log.Info(nameof(RegistryAutostartService), "Autostart disabled");
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            _log.Error(nameof(RegistryAutostartService), "Could not disable autostart", ex);
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool desired)
    {
        if (desired)
        {
            Enable();
        }
        else
        {
            Disable();
        }
    }
}
