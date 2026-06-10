using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Abstractions.Repositories;

/// <summary>Persistence port for telemetry readings and consumption aggregation.</summary>
public interface ITelemetryRepository
{
    Task AddAsync(TelemetryReading reading, CancellationToken ct = default);

    Task<TelemetryReading?> GetLatestAsync(string deviceId, CancellationToken ct = default);

    Task<IReadOnlyList<TelemetryReading>> GetSinceAsync(
        string deviceId, DateTime sinceUtc, int maxCount, CancellationToken ct = default);

    /// <summary>
    /// Aggregates energy/voltage per time bucket. Bucket labels are computed in
    /// the user's local time, given as an offset in minutes from UTC.
    /// </summary>
    Task<IReadOnlyList<ConsumptionBucketDto>> GetConsumptionAsync(
        string deviceId, HistoryRange range, int tzOffsetMinutes, CancellationToken ct = default);

    /// <summary>Total energy [kWh] between two UTC instants.</summary>
    Task<double> GetEnergyKwhAsync(
        string deviceId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Deletes raw readings older than the cutoff; returns the number of rows removed.</summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default);
}
