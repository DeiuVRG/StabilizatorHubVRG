using StabilizatorHub.Application.Common;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Services;

/// <summary>
/// Central ownership check: every device-scoped use case goes through here, so
/// a user can never read or command somebody else's device. Returns the same
/// error for "missing" and "not owned" to avoid leaking device existence.
/// </summary>
public interface IDeviceAccessService
{
    Task<OperationResult<Device>> GetOwnedDeviceAsync(string userId, string deviceId, CancellationToken ct = default);
}
