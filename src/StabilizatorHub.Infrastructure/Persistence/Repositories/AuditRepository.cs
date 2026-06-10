using Microsoft.EntityFrameworkCore;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Infrastructure.Persistence.Repositories;

public sealed class AuditRepository : IAuditRepository
{
    private readonly AppDbContext _db;

    public AuditRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AuditEntry entry, CancellationToken ct = default) =>
        await _db.AuditEntries.AddAsync(entry, ct);

    public async Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int take, CancellationToken ct = default) =>
        await _db.AuditEntries
            .OrderByDescending(a => a.TimestampUtc)
            .Take(take)
            .ToListAsync(ct);
}
