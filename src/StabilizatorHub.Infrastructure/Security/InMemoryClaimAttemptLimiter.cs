using System.Collections.Concurrent;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Infrastructure.Security;

/// <summary>
/// In-memory sliding lockout for pairing-code attempts: after
/// <see cref="MaxFailures"/> failures within the window, the key is blocked for
/// the cool-down. Single-instance deployment makes in-memory state sufficient.
/// </summary>
public sealed class InMemoryClaimAttemptLimiter : IClaimAttemptLimiter
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan BlockDuration = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly IClock _clock;

    public InMemoryClaimAttemptLimiter(IClock clock)
    {
        _clock = clock;
    }

    public bool IsBlocked(string key) =>
        _entries.TryGetValue(key, out var entry) && entry.BlockedUntilUtc > _clock.UtcNow;

    public void RecordFailure(string key)
    {
        var now = _clock.UtcNow;

        _entries.AddOrUpdate(
            key,
            _ => new Entry(1, now, DateTime.MinValue),
            (_, entry) =>
            {
                var failures = now - entry.WindowStartUtc > Window ? 1 : entry.Failures + 1;
                var windowStart = failures == 1 ? now : entry.WindowStartUtc;
                var blockedUntil = failures >= MaxFailures ? now + BlockDuration : entry.BlockedUntilUtc;

                return new Entry(failures, windowStart, blockedUntil);
            });
    }

    public void RecordSuccess(string key) => _entries.TryRemove(key, out _);

    private sealed record Entry(int Failures, DateTime WindowStartUtc, DateTime BlockedUntilUtc);
}
