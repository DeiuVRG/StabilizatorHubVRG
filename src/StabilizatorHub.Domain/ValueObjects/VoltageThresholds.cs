namespace StabilizatorHub.Domain.ValueObjects;

/// <summary>
/// Input-voltage limits for event detection. Defaults follow the project
/// requirement (215 V / 240 V); for reference, EN 50160 allows 230 V +/-10%.
/// Hysteresis prevents event flapping when the voltage oscillates around a limit.
/// </summary>
public sealed record VoltageThresholds
{
    public double UndervoltageLimit { get; init; } = 215.0;

    public double OvervoltageLimit { get; init; } = 240.0;

    /// <summary>An open event closes only after the voltage re-enters the normal band by this margin [V].</summary>
    public double HysteresisVolts { get; init; } = 2.0;

    public static VoltageThresholds Default { get; } = new();

    public bool IsUndervoltage(double voltage) => voltage <= UndervoltageLimit;

    public bool IsOvervoltage(double voltage) => voltage >= OvervoltageLimit;
}
