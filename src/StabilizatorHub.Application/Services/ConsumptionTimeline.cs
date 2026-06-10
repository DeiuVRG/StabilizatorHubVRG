using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Application.Services;

/// <summary>
/// Pure helper that turns sparse SQL aggregation results into a dense, ordered
/// timeline (missing buckets become zero), so the charts always show the full
/// requested range. Labels must match the SQL strftime formats exactly.
/// </summary>
public static class ConsumptionTimeline
{
    public static IReadOnlyList<ConsumptionBucketDto> FillGaps(
        HistoryRange range,
        int tzOffsetMinutes,
        DateTime nowUtc,
        IReadOnlyList<ConsumptionBucketDto> actualBuckets)
    {
        var byLabel = actualBuckets.ToDictionary(b => b.Label, StringComparer.Ordinal);

        return BuildExpectedLabels(range, nowUtc.AddMinutes(tzOffsetMinutes))
            .Select(label => byLabel.TryGetValue(label, out var bucket)
                ? bucket
                : new ConsumptionBucketDto(label, 0, 0, 0, 0, 0))
            .ToList();
    }

    public static IEnumerable<string> BuildExpectedLabels(HistoryRange range, DateTime localNow)
    {
        switch (range)
        {
            case HistoryRange.Day:
            {
                var currentHour = new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0);
                for (var i = 23; i >= 0; i--)
                {
                    yield return currentHour.AddHours(-i).ToString("yyyy-MM-dd HH:00");
                }

                break;
            }
            case HistoryRange.Week:
            {
                for (var i = 6; i >= 0; i--)
                {
                    yield return localNow.Date.AddDays(-i).ToString("yyyy-MM-dd");
                }

                break;
            }
            case HistoryRange.Month:
            {
                for (var i = 29; i >= 0; i--)
                {
                    yield return localNow.Date.AddDays(-i).ToString("yyyy-MM-dd");
                }

                break;
            }
            case HistoryRange.Year:
            {
                var currentMonth = new DateTime(localNow.Year, localNow.Month, 1);
                for (var i = 11; i >= 0; i--)
                {
                    yield return currentMonth.AddMonths(-i).ToString("yyyy-MM");
                }

                break;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(range), range, "Unknown history range");
        }
    }

    /// <summary>UTC instant where the requested range starts (used to bound the SQL query).</summary>
    public static DateTime RangeStartUtc(HistoryRange range, int tzOffsetMinutes, DateTime nowUtc)
    {
        var localNow = nowUtc.AddMinutes(tzOffsetMinutes);

        var localStart = range switch
        {
            HistoryRange.Day => new DateTime(localNow.Year, localNow.Month, localNow.Day, localNow.Hour, 0, 0).AddHours(-23),
            HistoryRange.Week => localNow.Date.AddDays(-6),
            HistoryRange.Month => localNow.Date.AddDays(-29),
            HistoryRange.Year => new DateTime(localNow.Year, localNow.Month, 1).AddMonths(-11),
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, "Unknown history range")
        };

        return localStart.AddMinutes(-tzOffsetMinutes);
    }
}
