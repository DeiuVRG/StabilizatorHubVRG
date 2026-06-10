using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Abstractions.Repositories;

/// <summary>Persistence port for household invite codes.</summary>
public interface IDeviceInviteRepository
{
    /// <summary>All invites that are still usable right now (codes are verified against their hashes).</summary>
    Task<IReadOnlyList<DeviceInvite>> GetUsableAsync(DateTime nowUtc, CancellationToken ct = default);

    Task<int> CountUsableForDeviceAsync(string deviceId, DateTime nowUtc, CancellationToken ct = default);

    Task AddAsync(DeviceInvite invite, CancellationToken ct = default);

    Task RemoveAllForDeviceAsync(string deviceId, CancellationToken ct = default);

    /// <summary>Removes expired/exhausted invites; returns how many were deleted.</summary>
    Task<int> DeleteUnusableAsync(DateTime nowUtc, CancellationToken ct = default);
}
