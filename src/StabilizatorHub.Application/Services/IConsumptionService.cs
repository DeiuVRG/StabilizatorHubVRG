using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Application.Services;

/// <summary>Use cases for the consumption charts and dashboard summary.</summary>
public interface IConsumptionService
{
    Task<OperationResult<IReadOnlyList<ConsumptionBucketDto>>> GetHistoryAsync(
        string userId, string deviceId, HistoryRange range, int tzOffsetMinutes, CancellationToken ct = default);

    Task<OperationResult<ConsumptionSummaryDto>> GetSummaryAsync(
        string userId, string deviceId, int tzOffsetMinutes, CancellationToken ct = default);
}
