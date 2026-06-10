namespace StabilizatorHub.Domain.Entities;

/// <summary>Access level of a user on a device.</summary>
public enum DeviceRole
{
    /// <summary>First claimer: full control (rename, invites, members, release).</summary>
    Owner = 1,

    /// <summary>Household member: sees all data and can switch the output relay.</summary>
    Member = 2
}

/// <summary>
/// Grants one user access to one device. A device can have many members
/// (a whole household) but exactly one Owner - the person who claimed it
/// with the pairing code.
/// </summary>
public class DeviceMembership
{
    public long Id { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public DeviceRole Role { get; set; }

    public DateTime JoinedAtUtc { get; set; }
}
