using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Application.Services;

/// <summary>Read-only device/telemetry queries for the API (always ownership-scoped).</summary>
public interface IDeviceQueryService
{
    Task<IReadOnlyList<DeviceDto>> GetMineAsync(string userId, CancellationToken ct = default);

    Task<OperationResult<TelemetryDto?>> GetLatestTelemetryAsync(
        string userId, string deviceId, CancellationToken ct = default);

    /// <summary>Recent samples for the live chart backfill.</summary>
    Task<OperationResult<IReadOnlyList<TelemetryDto>>> GetRecentTelemetryAsync(
        string userId, string deviceId, int minutes, CancellationToken ct = default);
}
