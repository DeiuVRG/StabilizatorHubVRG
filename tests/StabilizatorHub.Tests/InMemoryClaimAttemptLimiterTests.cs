using StabilizatorHub.Infrastructure.Security;
using StabilizatorHub.Tests.Support;
using Xunit;

namespace StabilizatorHub.Tests;

public class InMemoryClaimAttemptLimiterTests
{
    private readonly FakeClock _clock = new();

    [Fact]
    public void FreshKey_IsNotBlocked()
    {
        var limiter = new InMemoryClaimAttemptLimiter(_clock);

        Assert.False(limiter.IsBlocked("k"));
    }

    [Fact]
    public void FiveFailuresInWindow_BlockTheKey()
    {
        var limiter = new InMemoryClaimAttemptLimiter(_clock);

        for (var i = 0; i < 5; i++)
        {
            limiter.RecordFailure("k");
        }

        Assert.True(limiter.IsBlocked("k"));
        Assert.False(limiter.IsBlocked("other")); // isolation between keys
    }

    [Fact]
    public void Block_ExpiresAfterCoolDown()
    {
        var limiter = new InMemoryClaimAttemptLimiter(_clock);

        for (var i = 0; i < 5; i++)
        {
            limiter.RecordFailure("k");
        }

        _clock.Advance(TimeSpan.FromMinutes(16));

        Assert.False(limiter.IsBlocked("k"));
    }

    [Fact]
    public void Success_ResetsTheCounter()
    {
        var limiter = new InMemoryClaimAttemptLimiter(_clock);

        for (var i = 0; i < 4; i++)
        {
            limiter.RecordFailure("k");
        }

        limiter.RecordSuccess("k");
        limiter.RecordFailure("k");

        Assert.False(limiter.IsBlocked("k"));
    }

    [Fact]
    public void OldFailures_OutsideTheWindow_DoNotCount()
    {
        var limiter = new InMemoryClaimAttemptLimiter(_clock);

        for (var i = 0; i < 4; i++)
        {
            limiter.RecordFailure("k");
        }

        _clock.Advance(TimeSpan.FromMinutes(20)); // window expired
        limiter.RecordFailure("k");               // restarts at 1

        Assert.False(limiter.IsBlocked("k"));
    }
}
