using Microsoft.EntityFrameworkCore;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Infrastructure.Persistence.Repositories;

public sealed class DeviceMembershipRepository : IDeviceMembershipRepository
{
    private readonly AppDbContext _db;

    public DeviceMembershipRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<DeviceMembership?> GetAsync(string deviceId, string userId, CancellationToken ct = default) =>
        _db.DeviceMemberships.FirstOrDefaultAsync(
            m => m.DeviceId == deviceId && m.UserId == userId, ct);

    public async Task<IReadOnlyList<DeviceMembership>> GetForDeviceAsync(
        string deviceId, CancellationToken ct = default) =>
        await _db.DeviceMemberships
            .Where(m => m.DeviceId == deviceId)
            .ToListAsync(ct);

    public Task<bool> AnyForDeviceAsync(string deviceId, CancellationToken ct = default) =>
        _db.DeviceMemberships.AnyAsync(m => m.DeviceId == deviceId, ct);

    public async Task AddAsync(DeviceMembership membership, CancellationToken ct = default) =>
        await _db.DeviceMemberships.AddAsync(membership, ct);

    public void Remove(DeviceMembership membership) =>
        _db.DeviceMemberships.Remove(membership);

    public async Task RemoveAllForDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        // Staged removal (not ExecuteDelete) so it commits atomically with the
        // rest of the release operation in the same unit of work.
        var memberships = await _db.DeviceMemberships
            .Where(m => m.DeviceId == deviceId)
            .ToListAsync(ct);

        _db.DeviceMemberships.RemoveRange(memberships);
    }
}
