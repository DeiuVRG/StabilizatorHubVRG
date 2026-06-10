using Microsoft.EntityFrameworkCore;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Infrastructure.Persistence.Repositories;

public sealed class VoltageEventRepository : IVoltageEventRepository
{
    private readonly AppDbContext _db;

    public VoltageEventRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<VoltageEvent?> GetOpenAsync(string deviceId, CancellationToken ct = default) =>
        _db.VoltageEvents
            .Where(e => e.DeviceId == deviceId && e.EndedAtUtc == null)
            .OrderByDescending(e => e.StartedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<VoltageEvent>> GetOpenForAllDevicesAsync(CancellationToken ct = default) =>
        await _db.VoltageEvents
            .Where(e => e.EndedAtUtc == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<VoltageEvent>> GetRecentAsync(
        string deviceId, int take, CancellationToken ct = default) =>
        await _db.VoltageEvents
            .Where(e => e.DeviceId == deviceId)
            .OrderByDescending(e => e.StartedAtUtc)
            .Take(take)
            .ToListAsync(ct);

    public async Task AddAsync(VoltageEvent voltageEvent, CancellationToken ct = default) =>
        await _db.VoltageEvents.AddAsync(voltageEvent, ct);
}
