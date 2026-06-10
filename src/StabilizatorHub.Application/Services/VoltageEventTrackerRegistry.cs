using System.Collections.Concurrent;
using StabilizatorHub.Domain.Services;

namespace StabilizatorHub.Application.Services;

/// <summary>
/// Singleton holder of the per-device voltage trackers. Trackers carry in-memory
/// episode state between telemetry samples; scoped services fetch them here.
/// </summary>
public sealed class VoltageEventTrackerRegistry
{
    private readonly ConcurrentDictionary<string, VoltageEventTracker> _trackers =
        new(StringComparer.OrdinalIgnoreCase);

    public VoltageEventTracker? TryGet(string deviceId) =>
        _trackers.TryGetValue(deviceId, out var tracker) ? tracker : null;

    /// <summary>Registers a freshly hydrated tracker unless another one won the race.</summary>
    public VoltageEventTracker GetOrAdd(string deviceId, VoltageEventTracker candidate) =>
        _trackers.GetOrAdd(deviceId, candidate);
}
