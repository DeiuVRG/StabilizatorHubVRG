using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Common;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Services;

public sealed class DeviceAccessService : IDeviceAccessService
{
    public const string NotFoundError = "Device not found.";

    private readonly IDeviceRepository _devices;

    public DeviceAccessService(IDeviceRepository devices)
    {
        _devices = devices;
    }

    public async Task<OperationResult<Device>> GetOwnedDeviceAsync(
        string userId, string deviceId, CancellationToken ct = default)
    {
        var device = await _devices.GetByIdAsync(deviceId, ct);

        if (device is null || !device.IsOwnedBy(userId))
        {
            return OperationResult<Device>.Fail(NotFoundError);
        }

        return OperationResult<Device>.Ok(device);
    }
}
