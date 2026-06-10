using StabilizatorHub.Application.Common;

namespace StabilizatorHub.Application.Services;

/// <summary>Use case: remote SSR (output relay) on/off, restricted to the device owner.</summary>
public interface IDeviceControlService
{
    Task<OperationResult> SetOutputAsync(
        string userId, string? userEmail, string deviceId, bool on,
        string? ipAddress, CancellationToken ct = default);
}
