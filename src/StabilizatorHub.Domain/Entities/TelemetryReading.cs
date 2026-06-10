namespace StabilizatorHub.Domain.Entities;

/// <summary>
/// One telemetry sample persisted for history/charts.
/// The ESP32 publishes a sample every 60 seconds.
/// </summary>
public class TelemetryReading
{
    public long Id { get; set; }

    public string DeviceId { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    /// <summary>Stabilizer input (mains) RMS voltage [V].</summary>
    public double VoltageIn { get; set; }

    /// <summary>Stabilizer output RMS voltage [V].</summary>
    public double VoltageOut { get; set; }

    /// <summary>Load RMS current [A].</summary>
    public double CurrentAmps { get; set; }

    /// <summary>Apparent power drawn by the connected load [W].</summary>
    public double PowerWatts { get; set; }

    /// <summary>
    /// Energy consumed in the interval since the previous sample [Wh],
    /// integrated server-side (robust against device reboots).
    /// </summary>
    public double EnergyWh { get; set; }

    /// <summary>SSR (output relay) state at the time of the sample.</summary>
    public bool OutputOn { get; set; }
}
