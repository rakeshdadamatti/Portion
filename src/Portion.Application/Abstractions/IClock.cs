namespace Portion.Application.Abstractions;

/// <summary>Abstraction over the system clock so that time-dependent logic is deterministic in tests.</summary>
public interface IClock
{
    /// <summary>Current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Default <see cref="IClock" /> backed by <see cref="DateTimeOffset.UtcNow" />.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
