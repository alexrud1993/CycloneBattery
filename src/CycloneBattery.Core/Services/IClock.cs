namespace CycloneBattery.Core.Services;

/// <summary>Abstraction over wall-clock time so state timing can be tested deterministically.</summary>
public interface IClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default <see cref="IClock"/> backed by <see cref="DateTimeOffset.UtcNow"/>.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>A clock whose time is controlled by the caller. Used by unit tests.</summary>
public sealed class ManualClock : IClock
{
    public ManualClock(DateTimeOffset? start = null) => UtcNow = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan delta) => UtcNow += delta;

    /// <summary>Sets the clock to an exact value.</summary>
    public void Set(DateTimeOffset value) => UtcNow = value;
}
