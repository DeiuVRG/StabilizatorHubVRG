using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Domain.Abstractions;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Infrastructure.Persistence.Repositories;

public sealed class TelemetryRepository : ITelemetryRepository
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public TelemetryRepository(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task AddAsync(TelemetryReading reading, CancellationToken ct = default) =>
        await _db.Readings.AddAsync(reading, ct);

    public Task<TelemetryReading?> GetLatestAsync(string deviceId, CancellationToken ct = default) =>
        _db.Readings
            .Where(r => r.DeviceId == deviceId)
            .OrderByDescending(r => r.TimestampUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<TelemetryReading>> GetSinceAsync(
        string deviceId, DateTime sinceUtc, int maxCount, CancellationToken ct = default) =>
        await _db.Readings
            .Where(r => r.DeviceId == deviceId && r.TimestampUtc >= sinceUtc)
            .OrderBy(r => r.TimestampUtc)
            .Take(maxCount)
            .ToListAsync(ct);

    /// <summary>
    /// Buckets are computed directly in SQLite with strftime over the timestamp
    /// shifted into the user's local time, so a "day" matches the user's day.
    /// </summary>
    public async Task<IReadOnlyList<ConsumptionBucketDto>> GetConsumptionAsync(
        string deviceId, HistoryRange range, int tzOffsetMinutes, CancellationToken ct = default)
    {
        var format = range switch
        {
            HistoryRange.Day => "%Y-%m-%d %H:00",
            HistoryRange.Week or HistoryRange.Month => "%Y-%m-%d",
            HistoryRange.Year => "%Y-%m",
            _ => throw new ArgumentOutOfRangeException(nameof(range))
        };

        var fromUtc = ConsumptionTimeline.RangeStartUtc(range, tzOffsetMinutes, _clock.UtcNow);
        var tzModifier = FormattableString.Invariant($"{tzOffsetMinutes:+0;-0} minutes");

        const string sql = """
            SELECT strftime(@format, datetime(TimestampUtc, @tzModifier)) AS Label,
                   SUM(EnergyWh)   AS EnergyWh,
                   AVG(PowerWatts) AS AvgPowerW,
                   MIN(VoltageIn)  AS MinVin,
                   MAX(VoltageIn)  AS MaxVin,
                   COUNT(*)        AS Samples
            FROM Readings
            WHERE DeviceId = @deviceId AND TimestampUtc >= @fromUtc
            GROUP BY Label
            ORDER BY Label
            """;

        var rows = await _db.Database
            .SqlQueryRaw<BucketRow>(
                sql,
                new SqliteParameter("@format", format),
                new SqliteParameter("@tzModifier", tzModifier),
                new SqliteParameter("@deviceId", deviceId),
                new SqliteParameter("@fromUtc", FormatUtc(fromUtc)))
            .ToListAsync(ct);

        return rows
            .Select(r => new ConsumptionBucketDto(
                r.Label, Math.Round(r.EnergyWh / 1000.0, 4), Math.Round(r.AvgPowerW, 1),
                r.MinVin, r.MaxVin, r.Samples))
            .ToList();
    }

    public async Task<double> GetEnergyKwhAsync(
        string deviceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var wattHours = await _db.Readings
            .Where(r => r.DeviceId == deviceId && r.TimestampUtc >= fromUtc && r.TimestampUtc < toUtc)
            .SumAsync(r => (double?)r.EnergyWh, ct) ?? 0;

        return Math.Round(wattHours / 1000.0, 4);
    }

    public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default) =>
        _db.Readings
            .Where(r => r.TimestampUtc < cutoffUtc)
            .ExecuteDeleteAsync(ct);

    /// <summary>Matches the TEXT format EF Core uses to store DateTime in SQLite.</summary>
    private static string FormatUtc(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Row shape returned by the aggregation query (mapped by column name).</summary>
    private sealed class BucketRow
    {
        public string Label { get; set; } = string.Empty;
        public double EnergyWh { get; set; }
        public double AvgPowerW { get; set; }
        public double MinVin { get; set; }
        public double MaxVin { get; set; }
        public int Samples { get; set; }
    }
}
