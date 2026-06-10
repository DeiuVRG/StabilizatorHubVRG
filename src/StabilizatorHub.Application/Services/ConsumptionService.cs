using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Application.Services;

public sealed class ConsumptionService : IConsumptionService
{
    /// <summary>Time zones span UTC-12..UTC+14; anything outside is a client bug.</summary>
    private const int MaxTzOffsetMinutes = 14 * 60;

    private readonly IDeviceAccessService _access;
    private readonly ITelemetryRepository _telemetry;
    private readonly IClock _clock;

    public ConsumptionService(IDeviceAccessService access, ITelemetryRepository telemetry, IClock clock)
    {
        _access = access;
        _telemetry = telemetry;
        _clock = clock;
    }

    public async Task<OperationResult<IReadOnlyList<ConsumptionBucketDto>>> GetHistoryAsync(
        string userId, string deviceId, HistoryRange range, int tzOffsetMinutes, CancellationToken ct = default)
    {
        var access = await _access.GetAccessibleDeviceAsync(userId, deviceId, ct: ct);

        if (!access.Succeeded)
        {
            return OperationResult<IReadOnlyList<ConsumptionBucketDto>>.Fail(access.Error!);
        }

        var tz = Math.Clamp(tzOffsetMinutes, -MaxTzOffsetMinutes, MaxTzOffsetMinutes);

        var buckets = await _telemetry.GetConsumptionAsync(deviceId, range, tz, ct);
        var filled = ConsumptionTimeline.FillGaps(range, tz, _clock.UtcNow, buckets);

        return OperationResult<IReadOnlyList<ConsumptionBucketDto>>.Ok(filled);
    }

    public async Task<OperationResult<ConsumptionSummaryDto>> GetSummaryAsync(
        string userId, string deviceId, int tzOffsetMinutes, CancellationToken ct = default)
    {
        var access = await _access.GetAccessibleDeviceAsync(userId, deviceId, ct: ct);

        if (!access.Succeeded)
        {
            return OperationResult<ConsumptionSummaryDto>.Fail(access.Error!);
        }

        var tz = Math.Clamp(tzOffsetMinutes, -MaxTzOffsetMinutes, MaxTzOffsetMinutes);
        var nowUtc = _clock.UtcNow;
        var todayStartUtc = nowUtc.AddMinutes(tz).Date.AddMinutes(-tz);

        var summary = new ConsumptionSummaryDto(
            TodayKwh: await _telemetry.GetEnergyKwhAsync(deviceId, todayStartUtc, nowUtc, ct),
            Last7DaysKwh: await _telemetry.GetEnergyKwhAsync(deviceId, nowUtc.AddDays(-7), nowUtc, ct),
            Last30DaysKwh: await _telemetry.GetEnergyKwhAsync(deviceId, nowUtc.AddDays(-30), nowUtc, ct));

        return OperationResult<ConsumptionSummaryDto>.Ok(summary);
    }
}
