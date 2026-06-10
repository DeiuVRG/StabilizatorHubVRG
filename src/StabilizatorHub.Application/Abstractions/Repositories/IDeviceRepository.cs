using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Abstractions.Repositories;

/// <summary>A device together with the role the asking user has on it.</summary>
public sealed record DeviceWithRole(Device Device, DeviceRole Role);

/// <summary>A device together with how many users have access to it (admin view).</summary>
public sealed record DeviceWithMemberCount(Device Device, int MemberCount);

/// <summary>Persistence port for <see cref="Device"/> aggregates.</summary>
public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Devices the user is a member of (any role), with that role.</summary>
    Task<IReadOnlyList<DeviceWithRole>> GetForMemberAsync(string userId, CancellationToken ct = default);

    /// <summary>Unclaimed devices (no members) that have announced a pairing code.</summary>
    Task<IReadOnlyList<Device>> GetClaimableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DeviceWithMemberCount>> GetAllWithMemberCountAsync(CancellationToken ct = default);

    Task AddAsync(Device device, CancellationToken ct = default);
}
