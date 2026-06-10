using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Tests.Support;

/// <summary>Deterministic clock for time-dependent tests.</summary>
public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan delta) => UtcNow += delta;
}
