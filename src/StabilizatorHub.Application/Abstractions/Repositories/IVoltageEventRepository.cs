using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Abstractions.Repositories;

/// <summary>Persistence port for undervoltage/overvoltage episodes.</summary>
public interface IVoltageEventRepository
{
    /// <summary>The currently open episode of a device, if any (at most one can be open).</summary>
    Task<VoltageEvent?> GetOpenAsync(string deviceId, CancellationToken ct = default);

    Task<IReadOnlyList<VoltageEvent>> GetOpenForAllDevicesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<VoltageEvent>> GetRecentAsync(string deviceId, int take, CancellationToken ct = default);

    Task AddAsync(VoltageEvent voltageEvent, CancellationToken ct = default);
}
