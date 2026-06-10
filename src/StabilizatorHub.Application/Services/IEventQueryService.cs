using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Application.Services;

/// <summary>Use case: list recent undervoltage/overvoltage episodes of an owned device.</summary>
public interface IEventQueryService
{
    Task<OperationResult<IReadOnlyList<VoltageEventDto>>> GetRecentAsync(
        string userId, string deviceId, int take, CancellationToken ct = default);
}
