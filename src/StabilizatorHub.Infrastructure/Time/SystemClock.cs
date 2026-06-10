using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
