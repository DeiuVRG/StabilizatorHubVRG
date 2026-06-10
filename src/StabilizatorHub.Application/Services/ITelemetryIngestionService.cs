using StabilizatorHub.Domain.ValueObjects;

namespace StabilizatorHub.Application.Services;

/// <summary>Use case: process one telemetry sample received from a device.</summary>
public interface ITelemetryIngestionService
{
    Task IngestAsync(TelemetrySample sample, CancellationToken ct = default);
}
