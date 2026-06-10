using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Application.Services;

public sealed class DeviceQueryService : IDeviceQueryService
{
    private const int MaxRecentSamples = 1500;

    private readonly IDeviceRepository _devices;
    private readonly ITelemetryRepository _telemetry;
    private readonly IDeviceAccessService _access;
    private readonly IClock _clock;

    public DeviceQueryService(
        IDeviceRepository devices,
        ITelemetryRepository telemetry,
        IDeviceAccessService access,
        IClock clock)
    {
        _devices = devices;
        _telemetry = telemetry;
        _access = access;
        _clock = clock;
    }

    public async Task<IReadOnlyList<DeviceDto>> GetMineAsync(string userId, CancellationToken ct = default)
    {
        var devices = await _devices.GetByOwnerAsync(userId, ct);
        return devices.Select(DeviceDto.FromEntity).ToList();
    }

    public async Task<OperationResult<TelemetryDto?>> GetLatestTelemetryAsync(
        string userId, string deviceId, CancellationToken ct = default)
    {
        var owned = await _access.GetOwnedDeviceAsync(userId, deviceId, ct);

        if (!owned.Succeeded)
        {
            return OperationResult<TelemetryDto?>.Fail(owned.Error!);
        }

        var latest = await _telemetry.GetLatestAsync(deviceId, ct);
        return OperationResult<TelemetryDto?>.Ok(latest is null ? null : TelemetryDto.FromReading(latest));
    }

    public async Task<OperationResult<IReadOnlyList<TelemetryDto>>> GetRecentTelemetryAsync(
        string userId, string deviceId, int minutes, CancellationToken ct = default)
    {
        var owned = await _access.GetOwnedDeviceAsync(userId, deviceId, ct);

        if (!owned.Succeeded)
        {
            return OperationResult<IReadOnlyList<TelemetryDto>>.Fail(owned.Error!);
        }

        var sinceUtc = _clock.UtcNow.AddMinutes(-Math.Clamp(minutes, 1, 24 * 60));
        var readings = await _telemetry.GetSinceAsync(deviceId, sinceUtc, MaxRecentSamples, ct);

        IReadOnlyList<TelemetryDto> dtos = readings.Select(TelemetryDto.FromReading).ToList();
        return OperationResult<IReadOnlyList<TelemetryDto>>.Ok(dtos);
    }
}
