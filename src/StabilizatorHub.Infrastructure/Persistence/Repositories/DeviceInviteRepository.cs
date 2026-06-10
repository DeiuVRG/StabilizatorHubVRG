using Microsoft.EntityFrameworkCore;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Infrastructure.Persistence.Repositories;

public sealed class DeviceInviteRepository : IDeviceInviteRepository
{
    private readonly AppDbContext _db;

    public DeviceInviteRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DeviceInvite>> GetUsableAsync(
        DateTime nowUtc, CancellationToken ct = default) =>
        await _db.DeviceInvites
            .Where(i => i.ExpiresAtUtc > nowUtc && i.UseCount < i.MaxUses)
            .ToListAsync(ct);

    public Task<int> CountUsableForDeviceAsync(
        string deviceId, DateTime nowUtc, CancellationToken ct = default) =>
        _db.DeviceInvites.CountAsync(
            i => i.DeviceId == deviceId && i.ExpiresAtUtc > nowUtc && i.UseCount < i.MaxUses, ct);

    public async Task AddAsync(DeviceInvite invite, CancellationToken ct = default) =>
        await _db.DeviceInvites.AddAsync(invite, ct);

    public async Task RemoveAllForDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        var invites = await _db.DeviceInvites
            .Where(i => i.DeviceId == deviceId)
            .ToListAsync(ct);

        _db.DeviceInvites.RemoveRange(invites);
    }

    public Task<int> DeleteUnusableAsync(DateTime nowUtc, CancellationToken ct = default) =>
        _db.DeviceInvites
            .Where(i => i.ExpiresAtUtc <= nowUtc || i.UseCount >= i.MaxUses)
            .ExecuteDeleteAsync(ct);
}
