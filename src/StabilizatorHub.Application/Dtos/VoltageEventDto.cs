using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Dtos;

/// <summary>Undervoltage/overvoltage episode shaped for the API/SignalR clients.</summary>
public sealed record VoltageEventDto(
    long Id,
    string DeviceId,
    string Type,
    DateTime StartedAtUtc,
    DateTime? EndedAtUtc,
    double ExtremeVoltage,
    int SampleCount,
    bool IsOpen)
{
    public static VoltageEventDto FromEntity(VoltageEvent e) => new(
        e.Id,
        e.DeviceId,
        e.Type == VoltageEventType.Undervoltage ? "undervoltage" : "overvoltage",
        e.StartedAtUtc,
        e.EndedAtUtc,
        e.ExtremeVoltage,
        e.SampleCount,
        e.IsOpen);
}
