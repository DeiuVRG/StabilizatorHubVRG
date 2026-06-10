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
    DateTime? ClaimedAtUtc,
    string Role)
{
    public static DeviceDto FromEntity(Device d, DeviceRole role) => new(
        d.Id, d.Name, d.IsOnline, d.OutputOn, d.FirmwareVersion, d.LastSeenUtc, d.ClaimedAtUtc,
        RoleName(role));

    public static string RoleName(DeviceRole role) =>
        role == DeviceRole.Owner ? "owner" : "member";
}

/// <summary>Household invite, returned exactly once with the code in clear.</summary>
public sealed record DeviceInviteDto(string Code, DateTime ExpiresAtUtc, int MaxUses);

/// <summary>One member of a device (the email is filled in by the API layer).</summary>
public sealed record DeviceMemberDto(string UserId, string? Email, string Role, DateTime JoinedAtUtc);

/// <summary>Device status change pushed in real time.</summary>
public sealed record DeviceStatusDto(string DeviceId, bool IsOnline, bool OutputOn, DateTime? LastSeenUtc);

/// <summary>Admin overview of a device (membership count, never the pairing code).</summary>
public sealed record AdminDeviceDto(
    string Id,
    string Name,
    bool IsOnline,
    bool IsClaimed,
    int MemberCount,
    string? FirmwareVersion,
    DateTime? LastSeenUtc,
    DateTime CreatedAtUtc)
{
    public static AdminDeviceDto FromEntity(Device d, int memberCount) => new(
        d.Id, d.Name, d.IsOnline, memberCount > 0,
        memberCount, d.FirmwareVersion, d.LastSeenUtc, d.CreatedAtUtc);
}
