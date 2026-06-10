using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Domain.Abstractions;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Services;

public sealed class AuditService : IAuditService
{
    private readonly IAuditRepository _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AuditService(IAuditRepository audit, IUnitOfWork unitOfWork, IClock clock)
    {
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task LogAsync(
        string action,
        string? userId = null,
        string? userEmail = null,
        string? deviceId = null,
        string? details = null,
        string? ipAddress = null,
        CancellationToken ct = default)
    {
        await _audit.AddAsync(new AuditEntry
        {
            TimestampUtc = _clock.UtcNow,
            Action = action,
            UserId = userId,
            UserEmail = userEmail,
            DeviceId = deviceId,
            Details = details,
            IpAddress = ipAddress
        }, ct);

        await _unitOfWork.SaveChangesAsync(ct);
    }
}
