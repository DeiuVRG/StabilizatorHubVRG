using StabilizatorHub.Application.Common;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Services;

/// <summary>A device together with the caller's role on it.</summary>
public sealed record DeviceAccess(Device Device, DeviceRole Role);

/// <summary>
/// Central access check: every device-scoped use case goes through here, so a
/// user can never read or command a device they are not a member of. Returns
/// the same error for "missing" and "no access" to avoid leaking existence.
/// </summary>
public interface IDeviceAccessService
{
    /// <param name="requireOwner">When true, plain members are rejected (owner-only operations).</param>
    Task<OperationResult<DeviceAccess>> GetAccessibleDeviceAsync(
        string userId, string deviceId, bool requireOwner = false, CancellationToken ct = default);
}
