namespace StabilizatorHub.Infrastructure.Demo;

/// <summary>One synthetic measurement of the simulated stabilizer.</summary>
public readonly record struct DemoSample(
    double VoltageIn, double VoltageOut, double CurrentAmps, double PowerWatts);

/// <summary>
/// Deterministic, purely time-based signal model for the demo device - no
/// random state, so backfill and the live loop always agree and tests are
/// reproducible. Produces:
///  - input voltage around 230 V with a slow daily swing and smooth jitter,
///    two scheduled undervoltage dips per day and an overvoltage surge on
///    even days (so the events table has content);
///  - a regulated output voltage (~230 V) while the input stays plausible;
///  - a household daily load curve (night standby, morning and evening peaks).
/// </summary>
public static class DemoSignalGenerator
{
    /// <summary>Demo timestamps are interpreted at this fixed offset (Romania-like local day).</summary>
    public const int LocalOffsetMinutes = 180;

    public static DemoSample At(DateTime timestampUtc)
    {
        var local = timestampUtc.AddMinutes(LocalOffsetMinutes);
        var minuteOfDay = local.Hour * 60 + local.Minute + local.Second / 60.0;
        var t = timestampUtc.Ticks / (double)TimeSpan.TicksPerMinute; // smooth time axis

        var voltageIn = 230.0
                        + 3.5 * Math.Sin(2 * Math.PI * minuteOfDay / 1440.0)
                        + 1.4 * Math.Sin(t * 0.37)
                        + 0.9 * Math.Sin(t * 1.13 + 1.7);

        // Scheduled grid anomalies (kept inside fixed windows so they are easy
        // to point at during a presentation).
        if (minuteOfDay is >= 460 and < 472)        // 07:40-07:52 local
        {
            voltageIn -= 18;                         // ~212 V undervoltage
        }
        else if (minuteOfDay is >= 1150 and < 1165)  // 19:10-19:25 local
        {
            voltageIn -= 16;
        }
        else if (local.Day % 2 == 0 && minuteOfDay is >= 785 and < 793) // 13:05-13:13, even days
        {
            voltageIn += 12;                         // ~242 V overvoltage
        }

        // Servo regulation: output stays near the target with a small residue
        // proportional to the input deviation.
        var voltageOut = 230.0
                         + 0.05 * (voltageIn - 230.0)
                         + 0.4 * Math.Sin(t * 0.91 + 0.5);

        var powerWatts = BaseLoadWatts(minuteOfDay)
                         * (1 + 0.12 * Math.Sin(t * 0.23))
                         + ApplianceSpikeWatts(minuteOfDay, local.Day);

        if (powerWatts < 20)
        {
            powerWatts = 20;
        }

        var currentAmps = powerWatts / voltageOut;

        return new DemoSample(
            Math.Round(voltageIn, 1),
            Math.Round(voltageOut, 1),
            Math.Round(currentAmps, 2),
            Math.Round(powerWatts, 1));
    }

    /// <summary>Household daily curve, linearly interpolated between hourly anchors [W].</summary>
    private static double BaseLoadWatts(double minuteOfDay)
    {
        // hour:   0    3    6    8    10   13   16   18   20   22   24
        double[] hours = { 0, 3, 6, 8, 10, 13, 16, 18, 20, 22, 24 };
        double[] watts = { 70, 60, 90, 380, 220, 260, 200, 520, 680, 320, 70 };

        var hour = minuteOfDay / 60.0;

        for (var i = 1; i < hours.Length; i++)
        {
            if (hour <= hours[i])
            {
                var f = (hour - hours[i - 1]) / (hours[i] - hours[i - 1]);
                return watts[i - 1] + f * (watts[i] - watts[i - 1]);
            }
        }

        return watts[^1];
    }

    /// <summary>A heating appliance kicking in for half an hour around lunch and dinner.</summary>
    private static double ApplianceSpikeWatts(double minuteOfDay, int day)
    {
        var lunch = day % 2 == 0 ? 750 : 780;       // 12:30-13:00
        if (minuteOfDay is >= 750 and < 780)
        {
            return 800;
        }

        if (minuteOfDay >= lunch + 390 && minuteOfDay < lunch + 420) // around 19:00
        {
            return 1100;
        }

        return 0;
    }
}
