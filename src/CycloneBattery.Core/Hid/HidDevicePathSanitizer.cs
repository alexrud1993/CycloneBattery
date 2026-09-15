using System.Security.Cryptography;
using System.Text;

namespace CycloneBattery.Core.Hid;

/// <summary>
/// Turns a raw HID device path into a short, stable, non-reversible token.
/// </summary>
/// <remarks>
/// Windows device paths embed USB instance information. The technical spec requires raw device
/// paths to be sanitized or hashed before they reach the log, so this is the only supported way
/// to reference a device path in text output.
/// </remarks>
public static class HidDevicePathSanitizer
{
    private const int TokenLength = 12;

    /// <summary>
    /// Returns a <see cref="TokenLength"/>-character hex token derived from
    /// <paramref name="devicePath"/>, or <c>&lt;none&gt;</c> when the path is empty.
    /// </summary>
    public static string Sanitize(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return "<none>";
        }

        // Lower-cased first so that paths differing only by letter case map to the same token.
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(devicePath.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash, 0, TokenLength / 2).ToLowerInvariant();
    }
}
