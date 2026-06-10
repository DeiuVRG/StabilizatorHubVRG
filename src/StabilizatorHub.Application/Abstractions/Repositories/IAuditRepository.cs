using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Abstractions.Repositories;

/// <summary>Persistence port for the security audit trail.</summary>
public interface IAuditRepository
{
    Task AddAsync(AuditEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<AuditEntry>> GetRecentAsync(int take, CancellationToken ct = default);
}
