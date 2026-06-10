using StabilizatorHub.Domain.Services;
using Xunit;

namespace StabilizatorHub.Tests;

public class EnergyCalculatorTests
{
    private static readonly DateTime T0 = new(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SixtySecondsAt600W_Gives10Wh()
    {
        var wh = EnergyCalculator.IntervalWattHours(600, T0, T0.AddSeconds(60));

        Assert.Equal(10.0, wh, precision: 6);
    }

    [Fact]
    public void FirstSampleEver_GivesZero()
    {
        Assert.Equal(0, EnergyCalculator.IntervalWattHours(600, previousUtc: null, T0));
    }

    [Fact]
    public void LongOfflineGap_IsClampedToMaxGap()
    {
        // 2 hours of silence must not be credited with the last known power.
        var wh = EnergyCalculator.IntervalWattHours(600, T0, T0.AddHours(2), maxGap: TimeSpan.FromMinutes(5));

        Assert.Equal(600 * 5.0 / 60.0, wh, precision: 6); // exactly 5 minutes worth
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonPositivePower_GivesZero(double power)
    {
        Assert.Equal(0, EnergyCalculator.IntervalWattHours(power, T0, T0.AddMinutes(1)));
    }

    [Fact]
    public void NonMonotonicTimestamps_GiveZero()
    {
        Assert.Equal(0, EnergyCalculator.IntervalWattHours(600, T0.AddMinutes(1), T0));
    }
}
