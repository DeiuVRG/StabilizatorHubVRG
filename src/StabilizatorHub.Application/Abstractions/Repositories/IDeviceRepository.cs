using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Abstractions.Repositories;

/// <summary>Persistence port for <see cref="Device"/> aggregates.</summary>
public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default);

    Task<IReadOnlyList<Device>> GetByOwnerAsync(string userId, CancellationToken ct = default);

    /// <summary>Unclaimed devices that have announced a pairing code (claim candidates).</summary>
    Task<IReadOnlyList<Device>> GetClaimableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(Device device, CancellationToken ct = default);
}
