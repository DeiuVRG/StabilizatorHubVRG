using Microsoft.AspNetCore.SignalR;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Web.Hubs;

namespace StabilizatorHub.Web.Realtime;

/// <summary>SignalR implementation of the real-time broadcast port.</summary>
public sealed class SignalRTelemetryBroadcaster : ITelemetryBroadcaster
{
    private readonly IHubContext<LiveHub> _hub;

    public SignalRTelemetryBroadcaster(IHubContext<LiveHub> hub)
    {
        _hub = hub;
    }

    public Task BroadcastTelemetryAsync(TelemetryDto telemetry, CancellationToken ct = default) =>
        _hub.Clients.Group(LiveHub.DeviceGroup(telemetry.DeviceId))
            .SendAsync("telemetry", telemetry, ct);

    public Task BroadcastDeviceStatusAsync(DeviceStatusDto status, CancellationToken ct = default) =>
        _hub.Clients.Group(LiveHub.DeviceGroup(status.DeviceId))
            .SendAsync("deviceStatus", status, ct);

    public Task BroadcastVoltageEventAsync(VoltageEventDto voltageEvent, CancellationToken ct = default) =>
        _hub.Clients.Group(LiveHub.DeviceGroup(voltageEvent.DeviceId))
            .SendAsync("voltageEvent", voltageEvent, ct);
}
