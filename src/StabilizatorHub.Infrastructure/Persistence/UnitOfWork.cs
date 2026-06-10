using StabilizatorHub.Application.Abstractions;

namespace StabilizatorHub.Infrastructure.Persistence;

/// <summary>Commits everything staged on the scoped DbContext as one transaction.</summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
