namespace StabilizatorHub.Application.Dtos;

/// <summary>Chart range requested by the client.</summary>
public enum HistoryRange
{
    /// <summary>Last 24 hours, hourly buckets.</summary>
    Day,

    /// <summary>Last 7 days, daily buckets.</summary>
    Week,

    /// <summary>Last 30 days, daily buckets.</summary>
    Month,

    /// <summary>Last 12 months, monthly buckets.</summary>
    Year
}

/// <summary>
/// One aggregated time bucket for the consumption charts. The label is already
/// rendered in the user's local time (e.g. "2026-06-11 14:00", "2026-06-11", "2026-06").
/// </summary>
public sealed record ConsumptionBucketDto(
    string Label,
    double EnergyKwh,
    double AvgPowerW,
    double MinVoltageIn,
    double MaxVoltageIn,
    int SampleCount);

/// <summary>Headline consumption numbers for the dashboard cards.</summary>
public sealed record ConsumptionSummaryDto(
    double TodayKwh,
    double Last7DaysKwh,
    double Last30DaysKwh);
