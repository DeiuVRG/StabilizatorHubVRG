namespace StabilizatorHub.Domain.ValueObjects;

/// <summary>
/// Immutable telemetry sample as received from a device, before persistence.
/// Optional fields may be missing in older firmware payloads.
/// </summary>
public sealed record TelemetrySample(
    string DeviceId,
    DateTime TimestampUtc,
    double VoltageIn,
    double VoltageOut,
    double CurrentAmps,
    double PowerWatts,
    double? DeviceEnergyKwh = null,
    bool? OutputOn = null,
    string? FirmwareVersion = null);
