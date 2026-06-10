namespace StabilizatorHub.Domain.Services;

/// <summary>
/// Server-side energy integration: energy = power x elapsed time.
/// Integrating on the server is robust against device reboots (the firmware
/// energy counter restarts from zero on every boot).
/// </summary>
public static class EnergyCalculator
{
    /// <summary>
    /// Default cap for the integration interval. If the device was silent for
    /// longer (offline, power cut), we must not credit the whole gap with the
    /// last known power value.
    /// </summary>
    public static readonly TimeSpan DefaultMaxGap = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Energy [Wh] consumed between <paramref name="previousUtc"/> and
    /// <paramref name="currentUtc"/> at constant <paramref name="powerWatts"/>.
    /// The interval is clamped to [0, maxGap]; null previous timestamp (first
    /// sample ever) yields 0.
    /// </summary>
    public static double IntervalWattHours(
        double powerWatts,
        DateTime? previousUtc,
        DateTime currentUtc,
        TimeSpan? maxGap = null)
    {
        if (previousUtc is null || powerWatts <= 0)
        {
            return 0;
        }

        var gapLimit = maxGap ?? DefaultMaxGap;
        var elapsed = currentUtc - previousUtc.Value;

        if (elapsed <= TimeSpan.Zero)
        {
            return 0;
        }

        if (elapsed > gapLimit)
        {
            elapsed = gapLimit;
        }

        return powerWatts * elapsed.TotalHours;
    }
}
