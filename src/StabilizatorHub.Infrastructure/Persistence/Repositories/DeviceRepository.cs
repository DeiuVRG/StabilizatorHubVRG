using Microsoft.EntityFrameworkCore;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Infrastructure.Persistence.Repositories;

public sealed class DeviceRepository : IDeviceRepository
{
    private readonly AppDbContext _db;

    public DeviceRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default) =>
        _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);

    public async Task<IReadOnlyList<DeviceWithRole>> GetForMemberAsync(
        string userId, CancellationToken ct = default) =>
        await _db.DeviceMemberships
            .Where(m => m.UserId == userId)
            .Join(_db.Devices, m => m.DeviceId, d => d.Id,
                (m, d) => new DeviceWithRole(d, m.Role))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> GetClaimableAsync(CancellationToken ct = default) =>
        await _db.Devices
            .Where(d => d.PairingCodeHash != null
                        && !_db.DeviceMemberships.Any(m => m.DeviceId == d.Id))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Devices.OrderBy(d => d.CreatedAtUtc).ToListAsync(ct);

    public async Task<IReadOnlyList<DeviceWithMemberCount>> GetAllWithMemberCountAsync(
        CancellationToken ct = default) =>
        await _db.Devices
            .OrderBy(d => d.CreatedAtUtc)
            .Select(d => new DeviceWithMemberCount(
                d, _db.DeviceMemberships.Count(m => m.DeviceId == d.Id)))
            .ToListAsync(ct);

    public async Task AddAsync(Device device, CancellationToken ct = default) =>
        await _db.Devices.AddAsync(device, ct);
}
