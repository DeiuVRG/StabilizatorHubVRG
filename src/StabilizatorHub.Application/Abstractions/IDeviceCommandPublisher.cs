namespace StabilizatorHub.Application.Abstractions;

/// <summary>
/// Outbound messaging port towards devices (implemented over MQTT in
/// Infrastructure). The application layer never talks to the broker directly.
/// Methods return false instead of throwing when the broker is unreachable,
/// so callers decide whether delivery is critical for their use case.
/// </summary>
public interface IDeviceCommandPublisher
{
    /// <summary>Publishes the SSR on/off command on stabilizator/{deviceId}/comanda.</summary>
    Task<bool> PublishOutputCommandAsync(string deviceId, bool on, CancellationToken ct = default);

    /// <summary>
    /// Publishes the (retained) claim state on stabilizator/{deviceId}/claimed.
    /// On "false" the firmware generates a fresh pairing code, so a sold/released
    /// device can never be re-claimed with the old code.
    /// </summary>
    Task<bool> PublishClaimedAsync(string deviceId, bool claimed, CancellationToken ct = default);
}
