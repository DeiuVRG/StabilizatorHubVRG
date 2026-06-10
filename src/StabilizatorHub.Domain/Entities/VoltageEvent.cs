namespace StabilizatorHub.Domain.Entities;

/// <summary>
/// An abnormal input-voltage episode (undervoltage/overvoltage). An event opens
/// when the input voltage crosses a limit and closes when it returns inside the
/// normal band (with hysteresis). While open, EndedAtUtc is null.
/// </summary>
public class VoltageEvent
{
    public long Id { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public VoltageEventType Type { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime? EndedAtUtc { get; set; }

    /// <summary>Worst voltage seen during the episode: minimum for undervoltage, maximum for overvoltage [V].</summary>
    public double ExtremeVoltage { get; set; }

    /// <summary>Number of telemetry samples that fell inside the episode.</summary>
    public int SampleCount { get; set; }

    public bool IsOpen => EndedAtUtc is null;
}
