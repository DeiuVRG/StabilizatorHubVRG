using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Common;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Services;

public sealed class DeviceAccessService : IDeviceAccessService
{
    public const string NotFoundError = "Device not found.";

    private readonly IDeviceRepository _devices;
    private readonly IDeviceMembershipRepository _memberships;

    public DeviceAccessService(IDeviceRepository devices, IDeviceMembershipRepository memberships)
    {
        _devices = devices;
        _memberships = memberships;
    }

    public async Task<OperationResult<DeviceAccess>> GetAccessibleDeviceAsync(
        string userId, string deviceId, bool requireOwner = false, CancellationToken ct = default)
    {
        var device = await _devices.GetByIdAsync(deviceId, ct);

        if (device is null)
        {
            return OperationResult<DeviceAccess>.Fail(NotFoundError);
        }

        var membership = await _memberships.GetAsync(deviceId, userId, ct);

        if (membership is null || (requireOwner && membership.Role != DeviceRole.Owner))
        {
            return OperationResult<DeviceAccess>.Fail(NotFoundError);
        }

        return OperationResult<DeviceAccess>.Ok(new DeviceAccess(device, membership.Role));
    }
}
