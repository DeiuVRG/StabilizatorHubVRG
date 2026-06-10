namespace StabilizatorHub.Infrastructure.Logging;

/// <summary>Encrypted telemetry log configuration (appsettings: "TelemetryLog").</summary>
public sealed class TelemetryLogOptions
{
    public const string SectionName = "TelemetryLog";

    public bool Enabled { get; set; } = true;

    /// <summary>Directory for the encrypted daily files (resolved under the data directory when relative).</summary>
    public string Directory { get; set; } = "logs";

    /// <summary>Files older than this many days are deleted; 0 disables cleanup.</summary>
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Base64 AES-256 key (32 bytes). When empty, a key is generated on first
    /// run and stored in <see cref="KeyFileName"/> next to the database with
    /// owner-only permissions.
    /// </summary>
    public string? KeyBase64 { get; set; }

    public string KeyFileName { get; set; } = "telemetry-log.key";
}
