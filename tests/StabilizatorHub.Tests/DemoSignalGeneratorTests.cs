using StabilizatorHub.Infrastructure.Demo;
using Xunit;

namespace StabilizatorHub.Tests;

public class DemoSignalGeneratorTests
{
    [Fact]
    public void Samples_StayInsidePlausibleRanges_OverAFullDay()
    {
        var day = new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc);

        for (var minute = 0; minute < 24 * 60; minute++)
        {
            var sample = DemoSignalGenerator.At(day.AddMinutes(minute));

            Assert.InRange(sample.VoltageIn, 180, 260);
            Assert.InRange(sample.VoltageOut, 225, 235); // regulated output
            Assert.InRange(sample.PowerWatts, 20, 2500);
            Assert.InRange(sample.CurrentAmps, 0, 15);
        }
    }

    [Fact]
    public void MorningDipWindow_ProducesAnUndervoltageSample()
    {
        // 07:45 local = 04:45 UTC at the fixed +180 min demo offset.
        var inDip = DemoSignalGenerator.At(new DateTime(2026, 6, 11, 4, 45, 0, DateTimeKind.Utc));

        Assert.True(inDip.VoltageIn <= 215, $"expected undervoltage, got {inDip.VoltageIn} V");
    }

    [Fact]
    public void SurgeWindow_OnEvenDays_ProducesAnOvervoltageSample()
    {
        // 13:08 local on an even day = 10:08 UTC.
        var inSurge = DemoSignalGenerator.At(new DateTime(2026, 6, 12, 10, 8, 0, DateTimeKind.Utc));

        Assert.True(inSurge.VoltageIn >= 240, $"expected overvoltage, got {inSurge.VoltageIn} V");
    }

    [Fact]
    public void Generator_IsDeterministic()
    {
        var t = new DateTime(2026, 6, 11, 14, 30, 0, DateTimeKind.Utc);

        Assert.Equal(DemoSignalGenerator.At(t), DemoSignalGenerator.At(t));
    }

    [Fact]
    public void QuietNightSample_IsNormalGridAndLowLoad()
    {
        // 03:00 local = 00:00 UTC: no dip windows, standby consumption.
        var night = DemoSignalGenerator.At(new DateTime(2026, 6, 11, 0, 0, 0, DateTimeKind.Utc));

        Assert.InRange(night.VoltageIn, 216, 239);
        Assert.True(night.PowerWatts < 200, $"expected standby load, got {night.PowerWatts} W");
    }
}
