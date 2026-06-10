using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;

namespace StabilizatorHub.Application.Services;

/// <summary>
/// Device ownership use cases. A claim succeeds only with the pairing code
/// shown on the device OLED, which proves physical possession: nobody else can
/// attach somebody's stabilizer to their own account.
/// </summary>
public interface IDeviceClaimService
{
    /// <param name="rateLimitKey">Caller identity for brute-force limiting (user id or client IP).</param>
    Task<OperationResult<DeviceDto>> ClaimAsync(
        string userId, string? userEmail, string pairingCode, string rateLimitKey,
        string? ipAddress, CancellationToken ct = default);

    /// <summary>Detaches the device from the account; the firmware then generates a fresh pairing code.</summary>
    Task<OperationResult> ReleaseAsync(
        string userId, string? userEmail, string deviceId, string? ipAddress, CancellationToken ct = default);

    Task<OperationResult<DeviceDto>> RenameAsync(
        string userId, string deviceId, string newName, CancellationToken ct = default);
}
