using StabilizatorHub.Domain.ValueObjects;

namespace StabilizatorHub.Application.Options;

/// <summary>Configuration for input-voltage event detection (appsettings: "VoltageMonitor").</summary>
public sealed class VoltageMonitorOptions
{
    public const string SectionName = "VoltageMonitor";

    public double UndervoltageLimit { get; set; } = 215.0;

    public double OvervoltageLimit { get; set; } = 240.0;

    public double HysteresisVolts { get; set; } = 2.0;

    public VoltageThresholds ToThresholds() => new()
    {
        UndervoltageLimit = UndervoltageLimit,
        OvervoltageLimit = OvervoltageLimit,
        HysteresisVolts = HysteresisVolts
    };
}
