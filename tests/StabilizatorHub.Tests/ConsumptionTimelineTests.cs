using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Application.Services;
using Xunit;

namespace StabilizatorHub.Tests;

public class ConsumptionTimelineTests
{
    // 14:35 UTC = 17:35 local for tz +180 (Romania, summer time).
    private static readonly DateTime NowUtc = new(2026, 6, 11, 14, 35, 0, DateTimeKind.Utc);

    [Fact]
    public void DayRange_Produces24HourlyLabels_EndingWithCurrentLocalHour()
    {
        var labels = ConsumptionTimeline.BuildExpectedLabels(HistoryRange.Day, NowUtc.AddMinutes(180)).ToList();

        Assert.Equal(24, labels.Count);
        Assert.Equal("2026-06-10 18:00", labels[0]);
        Assert.Equal("2026-06-11 17:00", labels[^1]);
    }

    [Fact]
    public void WeekRange_Produces7DailyLabels()
    {
        var labels = ConsumptionTimeline.BuildExpectedLabels(HistoryRange.Week, NowUtc).ToList();

        Assert.Equal(7, labels.Count);
        Assert.Equal("2026-06-05", labels[0]);
        Assert.Equal("2026-06-11", labels[^1]);
    }

    [Fact]
    public void YearRange_Produces12MonthlyLabels()
    {
        var labels = ConsumptionTimeline.BuildExpectedLabels(HistoryRange.Year, NowUtc).ToList();

        Assert.Equal(12, labels.Count);
        Assert.Equal("2025-07", labels[0]);
        Assert.Equal("2026-06", labels[^1]);
    }

    [Fact]
    public void FillGaps_KeepsExistingBucketsAndZeroFillsTheRest()
    {
        var existing = new List<ConsumptionBucketDto>
        {
            new("2026-06-11", 1.25, 320, 221, 233, 1440)
        };

        var filled = ConsumptionTimeline.FillGaps(HistoryRange.Week, 0, NowUtc, existing);

        Assert.Equal(7, filled.Count);
        Assert.Equal(1.25, filled[^1].EnergyKwh);     // real bucket preserved (today)
        Assert.All(filled.Take(6), b => Assert.Equal(0, b.EnergyKwh)); // gaps are zero
    }

    [Fact]
    public void RangeStartUtc_ConvertsLocalRangeStartBackToUtc()
    {
        // Local day starts 2026-06-11 00:00 (+180) -> 2026-06-10 21:00 UTC.
        var startUtc = ConsumptionTimeline.RangeStartUtc(HistoryRange.Week, 180, NowUtc);

        Assert.Equal(new DateTime(2026, 6, 4, 21, 0, 0), startUtc);
    }
}
