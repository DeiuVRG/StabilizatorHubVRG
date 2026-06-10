using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Dtos;

/// <summary>Device shaped for the API clients (never exposes the pairing hash).</summary>
public sealed record DeviceDto(
    string Id,
    string Name,
    bool IsOnline,
    bool OutputOn,
    string? FirmwareVersion,
    DateTime? LastSeenUtc,
    DateTime? ClaimedAtUtc)
{
    public static DeviceDto FromEntity(Device d) => new(
        d.Id, d.Name, d.IsOnline, d.OutputOn, d.FirmwareVersion, d.LastSeenUtc, d.ClaimedAtUtc);
}

/// <summary>Device status change pushed in real time.</summary>
public sealed record DeviceStatusDto(string DeviceId, bool IsOnline, bool OutputOn, DateTime? LastSeenUtc);

/// <summary>Admin overview of a device (includes ownership info, never the pairing code).</summary>
public sealed record AdminDeviceDto(
    string Id,
    string Name,
    bool IsOnline,
    bool IsClaimed,
    string? OwnerUserId,
    string? FirmwareVersion,
    DateTime? LastSeenUtc,
    DateTime CreatedAtUtc)
{
    public static AdminDeviceDto FromEntity(Device d) => new(
        d.Id, d.Name, d.IsOnline, d.IsClaimed, d.OwnerUserId, d.FirmwareVersion, d.LastSeenUtc, d.CreatedAtUtc);
}
