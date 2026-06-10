namespace StabilizatorHub.Domain.Entities;

/// <summary>
/// Invitation created by a device owner so other household members can join
/// the same device with their own accounts. The code itself is never stored -
/// only its hash; it expires and has a bounded number of uses.
/// </summary>
public class DeviceInvite
{
    public long Id { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public int MaxUses { get; set; }

    public int UseCount { get; set; }

    public bool IsUsable(DateTime nowUtc) => nowUtc < ExpiresAtUtc && UseCount < MaxUses;
}
