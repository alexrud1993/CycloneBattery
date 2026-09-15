namespace CycloneBattery.Core.Services;

/// <summary>
/// Builds and applies the current-user autostart registration.
/// </summary>
/// <remarks>
/// The MVP uses <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> only: no machine-wide
/// key, no scheduled task, and therefore no administrator rights.
/// Reference: https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys
/// </remarks>
public interface IAutostartService
{
    /// <summary><see langword="false"/> on platforms without the Windows Run key.</summary>
    bool IsSupported { get; }

    /// <summary>The entry that would be (or is) registered.</summary>
    AutostartEntry Entry { get; }

    /// <summary><see langword="true"/> when the Run value exists.</summary>
    bool IsEnabled();

    /// <summary>Creates or updates the Run value.</summary>
    void Enable();

    /// <summary>Removes the Run value. Safe to call when it does not exist.</summary>
    void Disable();

    /// <summary>Applies <paramref name="desired"/>, enabling or disabling as needed.</summary>
    void SetEnabled(bool desired);
}
