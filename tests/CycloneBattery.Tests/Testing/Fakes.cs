using CycloneBattery.Core.Hid;
using CycloneBattery.Core.Models;
using CycloneBattery.Core.Settings;

namespace CycloneBattery.Tests.Testing;

/// <summary>In-memory <see cref="IHidDeviceDiscovery"/>.</summary>
public sealed class FakeHidDeviceDiscovery : IHidDeviceDiscovery
{
    private readonly List<HidDeviceCandidate> _candidates = [];

    /// <summary>Identity reported by <see cref="DetectIdentity"/>.</summary>
    public ControllerIdentity Identity { get; set; } = ControllerIdentity.XInput;

    /// <summary>Number of enumeration calls, to assert rescan behaviour.</summary>
    public int EnumerateCallCount { get; private set; }

    /// <summary>When set, <see cref="EnumerateCandidates"/> throws it.</summary>
    public Exception? ThrowOnEnumerate { get; set; }

    /// <summary>Adds a candidate and returns it.</summary>
    public HidDeviceCandidate AddCandidate(string devicePath = @"\\?\hid#vid_3537&pid_100b#1", int inputReportLength = 64, int outputReportLength = 64)
    {
        var candidate = new HidDeviceCandidate(
            devicePath,
            0x3537,
            0x100B,
            inputReportLength,
            outputReportLength,
            "Xbox 360 Controller for Windows",
            "GameSir");

        _candidates.Add(candidate);
        return candidate;
    }

    /// <summary>Removes all candidates.</summary>
    public void Clear() => _candidates.Clear();

    /// <inheritdoc />
    public IReadOnlyList<HidDeviceCandidate> EnumerateCandidates()
    {
        EnumerateCallCount++;
        if (ThrowOnEnumerate is not null)
        {
            throw ThrowOnEnumerate;
        }

        return _candidates.ToArray();
    }

    /// <inheritdoc />
    public ControllerIdentity DetectIdentity() => Identity;
}

/// <summary>Scriptable <see cref="ICycloneHidTransport"/>.</summary>
public sealed class FakeTransport : ICycloneHidTransport
{
    private readonly Queue<byte[]> _frames = new();

    public FakeTransport(HidDeviceCandidate candidate) => Candidate = candidate;

    /// <inheritdoc />
    public HidDeviceCandidate Candidate { get; }

    /// <inheritdoc />
    public int MaxInputReportLength => Candidate.MaxInputReportLength;

    /// <inheritdoc />
    public int MaxOutputReportLength => Candidate.MaxOutputReportLength;

    /// <summary>Every report written to this transport, in order.</summary>
    public List<byte[]> WrittenReports { get; } = [];

    /// <summary>Number of read calls.</summary>
    public int ReadCallCount { get; private set; }

    /// <summary><see langword="true"/> after <see cref="Dispose"/>.</summary>
    public bool Disposed { get; private set; }

    /// <summary>When set, every write throws it.</summary>
    public HidTransportException? ThrowOnWrite { get; set; }

    /// <summary>When set, every read throws it.</summary>
    public HidTransportException? ThrowOnRead { get; set; }

    /// <summary>Queues raw frames to be returned by subsequent reads.</summary>
    public void Enqueue(params byte[][] frames)
    {
        foreach (byte[] frame in frames)
        {
            _frames.Enqueue(frame);
        }
    }

    /// <summary>Queues a well-formed status frame.</summary>
    public void EnqueueReading(int batteryPercent, bool cableConnected = false) =>
        Enqueue(CapturedFrames.StatusFrame(batteryPercent, cableConnected));

    /// <inheritdoc />
    public void WriteOutputReport(ReadOnlySpan<byte> report)
    {
        WrittenReports.Add(report.ToArray());
        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }
    }

    /// <inheritdoc />
    public bool TryReadInputReport(int timeoutMilliseconds, out ReadOnlyMemory<byte> frame)
    {
        ReadCallCount++;

        if (ThrowOnRead is not null)
        {
            throw ThrowOnRead;
        }

        if (_frames.Count == 0)
        {
            frame = default;
            return false;
        }

        frame = _frames.Dequeue();
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => Disposed = true;
}

/// <summary><see cref="ICycloneHidTransportFactory"/> that hands out pre-built fake transports.</summary>
public sealed class FakeTransportFactory : ICycloneHidTransportFactory
{
    private readonly Dictionary<string, FakeTransport> _transports = new(StringComparer.Ordinal);
    private readonly HashSet<string> _failOpen = new(StringComparer.Ordinal);

    /// <summary>Device paths recorded for every open attempt.</summary>
    public List<string> OpenAttempts { get; } = [];

    /// <summary>When set, opening fails with a busy-looking error.</summary>
    public bool FailAsBusy { get; set; }

    /// <summary>Marks a device path as un-openable.</summary>
    public void FailOpenFor(string devicePath) => _failOpen.Add(devicePath);

    /// <summary>Registers a transport for a device path.</summary>
    public FakeTransport Register(HidDeviceCandidate candidate)
    {
        var transport = new FakeTransport(candidate);
        _transports[candidate.DevicePath] = transport;
        return transport;
    }

    /// <inheritdoc />
    public TransportOpenResult TryOpen(HidDeviceCandidate candidate, out ICycloneHidTransport? transport)
    {
        OpenAttempts.Add(candidate.DevicePath);

        if (_failOpen.Contains(candidate.DevicePath))
        {
            transport = null;
            return TransportOpenResult.Failed(
                FailAsBusy ? "Access is denied" : "The device is no longer attached",
                FailAsBusy);
        }

        if (_transports.TryGetValue(candidate.DevicePath, out FakeTransport? registered))
        {
            transport = registered;
            return TransportOpenResult.Opened();
        }

        transport = null;
        return TransportOpenResult.Failed("no transport registered for this path", false);
    }
}

/// <summary>In-memory <see cref="ISettingsStore"/>.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    /// <inheritdoc />
    public string? Content { get; set; }

    /// <inheritdoc />
    public string Location => "memory://settings.json";

    /// <summary>Number of write calls.</summary>
    public int WriteCount { get; private set; }

    /// <summary>When set, <see cref="WriteAllText"/> throws it.</summary>
    public Exception? ThrowOnWrite { get; set; }

    /// <inheritdoc />
    public string? ReadAllText() => Content;

    /// <inheritdoc />
    public void WriteAllText(string content)
    {
        if (ThrowOnWrite is not null)
        {
            throw ThrowOnWrite;
        }

        WriteCount++;
        Content = content;
    }
}
