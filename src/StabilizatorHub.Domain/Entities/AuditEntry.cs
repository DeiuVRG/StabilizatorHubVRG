namespace StabilizatorHub.Domain.Entities;

/// <summary>
/// Security audit trail entry. Sensitive actions (login, claim, relay control,
/// update trigger) are recorded so they can be reviewed later.
/// </summary>
public class AuditEntry
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string? UserId { get; set; }

    public string? UserEmail { get; set; }

    public string? DeviceId { get; set; }

    /// <summary>Machine-readable action key, e.g. "auth.login", "device.claim", "device.control".</summary>
    public string Action { get; set; } = string.Empty;

    public string? Details { get; set; }

    public string? IpAddress { get; set; }
}
