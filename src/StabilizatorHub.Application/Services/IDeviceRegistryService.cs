namespace StabilizatorHub.Application.Services;

/// <summary>Use cases for the device lifecycle messages (status/info topics).</summary>
public interface IDeviceRegistryService
{
    /// <summary>Handles online/offline (including the broker Last Will message).</summary>
    Task HandleStatusAsync(string deviceId, bool online, CancellationToken ct = default);

    /// <summary>Handles the device announcement: pairing code (while unclaimed) and firmware version.</summary>
    Task HandleInfoAsync(string deviceId, string? pairingCode, string? firmwareVersion, CancellationToken ct = default);
}
