namespace CycloneBattery.Core.Hid;

/// <summary>
/// Raised when an HID operation fails for a reason other than a read timeout.
/// </summary>
/// <remarks>
/// The state service treats this as "the current interface is unusable", drops it and starts
/// rediscovery. It is never surfaced to the user as a crash.
/// </remarks>
public sealed class HidTransportException : Exception
{
    public HidTransportException(string message)
        : base(message)
    {
    }

    public HidTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// <see langword="true"/> when the failure looks like "another program owns the interface"
    /// (GameSir Connect, another utility) rather than "the device disappeared".
    /// </summary>
    public bool LooksBusy { get; init; }
}
