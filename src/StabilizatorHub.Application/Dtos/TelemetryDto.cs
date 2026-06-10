using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Dtos;

/// <summary>Telemetry sample shaped for the API/SignalR clients.</summary>
public sealed record TelemetryDto(
    string DeviceId,
    DateTime TimestampUtc,
    double VoltageIn,
    double VoltageOut,
    double CurrentAmps,
    double PowerWatts,
    double EnergyWh,
    bool OutputOn)
{
    public static TelemetryDto FromReading(TelemetryReading r) => new(
        r.DeviceId, r.TimestampUtc, r.VoltageIn, r.VoltageOut,
        r.CurrentAmps, r.PowerWatts, r.EnergyWh, r.OutputOn);
}
