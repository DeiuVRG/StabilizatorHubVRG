namespace StabilizatorHub.Application.Options;

/// <summary>Telemetry processing configuration (appsettings: "Telemetry").</summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>
    /// A device is considered offline when no telemetry arrived for this long.
    /// Default = 3 missed 60-second samples. This is a safety net on top of the
    /// MQTT Last Will message.
    /// </summary>
    public int OfflineAfterSeconds { get; set; } = 180;

    /// <summary>Maximum gap credited when integrating energy between samples.</summary>
    public int MaxEnergyGapMinutes { get; set; } = 5;

    /// <summary>
    /// Minimum spacing between STORED readings per device. The device pushes one
    /// frame per minute plus extra frames on relay commands / reconnect - those
    /// extras still update the live UI but are not written to history, so the
    /// stored series stays at ~1-minute resolution. Slightly below 60 s so the
    /// regular per-minute frame is never dropped by network jitter.
    /// </summary>
    public int MinPersistIntervalSeconds { get; set; } = 55;

    /// <summary>Raw readings older than this are pruned daily; 0 keeps everything.</summary>
    public int RawRetentionDays { get; set; } = 0;
}
