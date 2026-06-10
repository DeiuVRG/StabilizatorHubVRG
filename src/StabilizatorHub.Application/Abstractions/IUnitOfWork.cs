namespace StabilizatorHub.Application.Abstractions;

/// <summary>
/// Commits all changes staged through the repositories in the current scope
/// as a single transaction. Keeps repositories free of persistence timing
/// concerns (Single Responsibility).
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}
