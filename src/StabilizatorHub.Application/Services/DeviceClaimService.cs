using Microsoft.Extensions.Logging;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Application.Services;

public sealed class DeviceClaimService : IDeviceClaimService
{
    public const string InvalidCodeError = "Invalid pairing code. Check the code shown on the device display.";
    public const string TooManyAttemptsError = "Too many failed attempts. Try again in a few minutes.";

    private readonly IDeviceRepository _devices;
    private readonly IDeviceAccessService _access;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPairingCodeHasher _hasher;
    private readonly IClaimAttemptLimiter _limiter;
    private readonly IDeviceCommandPublisher _commands;
    private readonly IAuditService _audit;
    private readonly IClock _clock;
    private readonly ILogger<DeviceClaimService> _logger;

    public DeviceClaimService(
        IDeviceRepository devices,
        IDeviceAccessService access,
        IUnitOfWork unitOfWork,
        IPairingCodeHasher hasher,
        IClaimAttemptLimiter limiter,
        IDeviceCommandPublisher commands,
        IAuditService audit,
        IClock clock,
        ILogger<DeviceClaimService> logger)
    {
        _devices = devices;
        _access = access;
        _unitOfWork = unitOfWork;
        _hasher = hasher;
        _limiter = limiter;
        _commands = commands;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OperationResult<DeviceDto>> ClaimAsync(
        string userId, string? userEmail, string pairingCode, string rateLimitKey,
        string? ipAddress, CancellationToken ct = default)
    {
        var code = DeviceRegistryService.NormalizePairingCode(pairingCode);

        if (code.Length is < 4 or > 16)
        {
            return OperationResult<DeviceDto>.Fail(InvalidCodeError);
        }

        if (_limiter.IsBlocked(rateLimitKey))
        {
            return OperationResult<DeviceDto>.Fail(TooManyAttemptsError);
        }

        // Hashes are salted, so the device cannot be looked up by code directly;
        // we verify against each claimable device (their number is small).
        var candidates = await _devices.GetClaimableAsync(ct);
        var device = candidates.FirstOrDefault(d => _hasher.Verify(code, d.PairingCodeHash!));

        if (device is null)
        {
            _limiter.RecordFailure(rateLimitKey);
            _logger.LogWarning("Failed claim attempt by user {UserId}", userId);
            await _audit.LogAsync("device.claim.failed", userId, userEmail,
                details: "Invalid pairing code", ipAddress: ipAddress, ct: ct);
            return OperationResult<DeviceDto>.Fail(InvalidCodeError);
        }

        device.OwnerUserId = userId;
        device.ClaimedAtUtc = _clock.UtcNow;
        device.PairingCodeHash = null;

        _limiter.RecordSuccess(rateLimitKey);

        // Commits the staged ownership change together with the audit entry.
        await _audit.LogAsync("device.claim", userId, userEmail, device.Id, ipAddress: ipAddress, ct: ct);

        // Retained message: the device learns it is claimed even if it is
        // temporarily offline right now. Delivery failure is not fatal - the
        // registry heals the claimed state when the device re-announces itself.
        if (!await _commands.PublishClaimedAsync(device.Id, claimed: true, ct))
        {
            _logger.LogWarning("Could not publish claimed state for {DeviceId}; will heal on next announce", device.Id);
        }

        _logger.LogInformation("Device {DeviceId} claimed by user {UserId}", device.Id, userId);
        return OperationResult<DeviceDto>.Ok(DeviceDto.FromEntity(device));
    }

    public async Task<OperationResult> ReleaseAsync(
        string userId, string? userEmail, string deviceId, string? ipAddress, CancellationToken ct = default)
    {
        var owned = await _access.GetOwnedDeviceAsync(userId, deviceId, ct);

        if (!owned.Succeeded)
        {
            return OperationResult.Fail(owned.Error!);
        }

        var device = owned.Value!;
        device.OwnerUserId = null;
        device.ClaimedAtUtc = null;
        device.PairingCodeHash = null; // a fresh code will arrive from the device

        await _audit.LogAsync("device.release", userId, userEmail, device.Id, ipAddress: ipAddress, ct: ct);

        if (!await _commands.PublishClaimedAsync(device.Id, claimed: false, ct))
        {
            _logger.LogWarning("Could not publish release for {DeviceId}; will heal on next announce", device.Id);
        }

        _logger.LogInformation("Device {DeviceId} released by user {UserId}", device.Id, userId);
        return OperationResult.Ok();
    }

    public async Task<OperationResult<DeviceDto>> RenameAsync(
        string userId, string deviceId, string newName, CancellationToken ct = default)
    {
        var name = newName.Trim();

        if (name.Length is < 1 or > 60)
        {
            return OperationResult<DeviceDto>.Fail("Name must be between 1 and 60 characters.");
        }

        var owned = await _access.GetOwnedDeviceAsync(userId, deviceId, ct);

        if (!owned.Succeeded)
        {
            return OperationResult<DeviceDto>.Fail(owned.Error!);
        }

        owned.Value!.Name = name;
        await _unitOfWork.SaveChangesAsync(ct);

        return OperationResult<DeviceDto>.Ok(DeviceDto.FromEntity(owned.Value));
    }
}
