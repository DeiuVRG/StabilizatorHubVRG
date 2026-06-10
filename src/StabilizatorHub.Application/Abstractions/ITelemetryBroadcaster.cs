using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Application.Abstractions;

/// <summary>
/// Real-time push port towards connected browsers (implemented with SignalR in
/// the Web layer). Messages are delivered only to the group of the device owner.
/// </summary>
public interface ITelemetryBroadcaster
{
    Task BroadcastTelemetryAsync(TelemetryDto telemetry, CancellationToken ct = default);

    Task BroadcastDeviceStatusAsync(DeviceStatusDto status, CancellationToken ct = default);

    Task BroadcastVoltageEventAsync(VoltageEventDto voltageEvent, CancellationToken ct = default);
}
