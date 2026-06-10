using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Abstractions.Repositories;

/// <summary>Persistence port for user-device access grants.</summary>
public interface IDeviceMembershipRepository
{
    Task<DeviceMembership?> GetAsync(string deviceId, string userId, CancellationToken ct = default);

    Task<IReadOnlyList<DeviceMembership>> GetForDeviceAsync(string deviceId, CancellationToken ct = default);

    /// <summary>True when the device has at least one member (i.e. it is claimed).</summary>
    Task<bool> AnyForDeviceAsync(string deviceId, CancellationToken ct = default);

    Task AddAsync(DeviceMembership membership, CancellationToken ct = default);

    void Remove(DeviceMembership membership);

    Task RemoveAllForDeviceAsync(string deviceId, CancellationToken ct = default);
}
