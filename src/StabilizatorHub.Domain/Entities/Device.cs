namespace StabilizatorHub.Domain.Entities;

/// <summary>
/// A physical stabilizer unit (ESP32). The id is the device MAC address without
/// separators (e.g. "A1B2C3D4E5F6") - globally unique and burned into the chip.
/// A device starts unclaimed; a user becomes its owner by proving physical
/// possession with the pairing code shown on the device OLED.
/// </summary>
public class Device
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Friendly name chosen by the owner (defaults to the id).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Identity user id of the owner; null while the device is unclaimed.</summary>
    public string? OwnerUserId { get; set; }

    public DateTime? ClaimedAtUtc { get; set; }

    /// <summary>
    /// Hash of the current pairing code (never stored in clear).
    /// Present only while the device is unclaimed.
    /// </summary>
    public string? PairingCodeHash { get; set; }

    public string? FirmwareVersion { get; set; }

    /// <summary>Last known state reported via MQTT status/telemetry.</summary>
    public bool IsOnline { get; set; }

    /// <summary>Last known SSR (output relay) state.</summary>
    public bool OutputOn { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Last MQTT activity of any kind (status/info/telemetry).</summary>
    public DateTime? LastSeenUtc { get; set; }

    /// <summary>Timestamp of the last telemetry sample (used for energy integration and offline detection).</summary>
    public DateTime? LastTelemetryUtc { get; set; }

    public bool IsClaimed => OwnerUserId is not null;

    public bool IsOwnedBy(string userId) =>
        OwnerUserId is not null && string.Equals(OwnerUserId, userId, StringComparison.Ordinal);
}
