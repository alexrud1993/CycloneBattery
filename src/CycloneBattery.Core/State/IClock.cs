namespace CycloneBattery.Core.State;

/// <summary>Abstraction over current UTC time for deterministic state-machine tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Production clock implementation.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
