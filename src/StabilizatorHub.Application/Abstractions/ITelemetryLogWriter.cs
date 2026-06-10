using StabilizatorHub.Domain.ValueObjects;

namespace StabilizatorHub.Application.Abstractions;

/// <summary>
/// Append-only telemetry log port. The Infrastructure implementation encrypts
/// every line (AES-256-GCM) and rotates files daily with retention cleanup.
/// </summary>
public interface ITelemetryLogWriter
{
    Task AppendAsync(TelemetrySample sample, double intervalEnergyWh, CancellationToken ct = default);
}
