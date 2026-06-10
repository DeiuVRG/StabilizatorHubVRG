using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Application.Services;

public sealed class EventQueryService : IEventQueryService
{
    private readonly IDeviceAccessService _access;
    private readonly IVoltageEventRepository _events;

    public EventQueryService(IDeviceAccessService access, IVoltageEventRepository events)
    {
        _access = access;
        _events = events;
    }

    public async Task<OperationResult<IReadOnlyList<VoltageEventDto>>> GetRecentAsync(
        string userId, string deviceId, int take, CancellationToken ct = default)
    {
        var access = await _access.GetAccessibleDeviceAsync(userId, deviceId, ct: ct);

        if (!access.Succeeded)
        {
            return OperationResult<IReadOnlyList<VoltageEventDto>>.Fail(access.Error!);
        }

        var events = await _events.GetRecentAsync(deviceId, Math.Clamp(take, 1, 200), ct);
        IReadOnlyList<VoltageEventDto> dtos = events.Select(VoltageEventDto.FromEntity).ToList();

        return OperationResult<IReadOnlyList<VoltageEventDto>>.Ok(dtos);
    }
}
