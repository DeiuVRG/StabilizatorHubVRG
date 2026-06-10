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

    public async Task<IReadOnlyList<Device>> GetByOwnerAsync(string userId, CancellationToken ct = default) =>
        await _db.Devices
            .Where(d => d.OwnerUserId == userId)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> GetClaimableAsync(CancellationToken ct = default) =>
        await _db.Devices
            .Where(d => d.OwnerUserId == null && d.PairingCodeHash != null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Devices.OrderBy(d => d.CreatedAtUtc).ToListAsync(ct);

    public async Task AddAsync(Device device, CancellationToken ct = default) =>
        await _db.Devices.AddAsync(device, ct);
}
