namespace StabilizatorHub.Application.Services;

/// <summary>
/// Records a security audit entry. NOTE: the implementation commits the current
/// unit of work, so call it as the final step of an operation.
/// </summary>
public interface IAuditService
{
    Task LogAsync(
        string action,
        string? userId = null,
        string? userEmail = null,
        string? deviceId = null,
        string? details = null,
        string? ipAddress = null,
        CancellationToken ct = default);
}
