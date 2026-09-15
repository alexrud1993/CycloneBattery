using HidSharp;
using CycloneBattery.Core.Models;

namespace CycloneBattery.Core.Hid;

/// <summary>
/// <see cref="ICycloneHidTransport"/> over a HidSharp <see cref="HidStream"/>.
/// </summary>
/// <remarks>
/// Reads are blocking with an explicit timeout, which keeps the background service free of busy
/// polling. Buffers are sized from the HID-reported report lengths, never from a hard-coded
/// guess, and every frame handed to the caller is a private copy.
/// </remarks>
internal sealed class HidSharpTransport : ICycloneHidTransport
{
    private readonly HidStream _stream;
    private readonly byte[] _readBuffer;
    private bool _disposed;

    internal HidSharpTransport(
        HidDeviceCandidate candidate,
        HidStream stream,
        int maxInputReportLength,
        int maxOutputReportLength)
    {
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        MaxInputReportLength = maxInputReportLength;
        MaxOutputReportLength = maxOutputReportLength;

        // A device that reports no usable length still needs a buffer; 64 bytes is the observed
        // Cyclone 2 report size.
        int bufferLength = maxInputReportLength > 0 ? maxInputReportLength : Protocol.CycloneProtocol.DefaultStatusReportLength;
        _readBuffer = new byte[bufferLength];
    }

    /// <inheritdoc />
    public HidDeviceCandidate Candidate { get; }

    /// <inheritdoc />
    public int MaxInputReportLength { get; }

    /// <inheritdoc />
    public int MaxOutputReportLength { get; }

    /// <inheritdoc />
    public void WriteOutputReport(ReadOnlySpan<byte> report)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (report.IsEmpty)
        {
            throw new ArgumentException("An output report must contain at least the report id byte.", nameof(report));
        }

        byte[] buffer = report.ToArray();
        try
        {
            // HidSharp expects the report id in the first byte, which is exactly how
            // StatusActivationCommand builds its frames.
            _stream.Write(buffer);
        }
        catch (IOException ex)
        {
            throw new HidTransportException(Describe(ex), ex) { LooksBusy = HidSharpDeviceDiscovery.LooksBusy(ex) };
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new HidTransportException(Describe(ex), ex) { LooksBusy = true };
        }
        catch (ObjectDisposedException ex)
        {
            throw new HidTransportException(Describe(ex), ex);
        }
    }

    /// <inheritdoc />
    public bool TryReadInputReport(int timeoutMilliseconds, out ReadOnlyMemory<byte> frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        frame = default;

        if (timeoutMilliseconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), "Timeout must be positive.");
        }

        try
        {
            _stream.ReadTimeout = timeoutMilliseconds;
            int read = _stream.Read(_readBuffer, 0, _readBuffer.Length);
            if (read <= 0)
            {
                return false;
            }

            frame = new ReadOnlyMemory<byte>(_readBuffer, 0, read).ToArray();
            return true;
        }
        catch (TimeoutException)
        {
            // No report within the slice: perfectly normal while the controller is quiet.
            return false;
        }
        catch (IOException ex)
        {
            throw new HidTransportException(Describe(ex), ex) { LooksBusy = HidSharpDeviceDiscovery.LooksBusy(ex) };
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new HidTransportException(Describe(ex), ex) { LooksBusy = true };
        }
        catch (ObjectDisposedException ex)
        {
            throw new HidTransportException(Describe(ex), ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _stream.Dispose();
        }
        catch (IOException)
        {
            // The device may already be gone; nothing useful can be done here.
        }
        catch (ObjectDisposedException)
        {
            // Already closed by HidSharp.
        }
    }

    private string Describe(Exception ex) =>
        ex.Message is { Length: > 0 } message
            ? $"{ex.GetType().Name}: {message.Replace(Candidate.DevicePath, Candidate.SanitizedId, StringComparison.OrdinalIgnoreCase)}"
            : ex.GetType().Name;
}
