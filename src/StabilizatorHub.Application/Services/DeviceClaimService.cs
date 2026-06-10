using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Common;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Domain.Abstractions;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Services;

public sealed class DeviceClaimService : IDeviceClaimService
{
    public const string InvalidCodeError =
        "Invalid code. Check the pairing code on the device display, or ask the device owner for a fresh invite code.";
    public const string TooManyAttemptsError = "Too many failed attempts. Try again in a few minutes.";
    public const string AlreadyMemberError = "You already have access to this device.";

    private const int InviteCodeLength = 8;
    private const int InviteMaxUses = 10;
    private const int MaxActiveInvitesPerDevice = 5;
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromHours(48);

    // 32 unambiguous characters - same alphabet the firmware uses.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly IDeviceRepository _devices;
    private readonly IDeviceMembershipRepository _memberships;
    private readonly IDeviceInviteRepository _invites;
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
        IDeviceMembershipRepository memberships,
        IDeviceInviteRepository invites,
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
        _memberships = memberships;
        _invites = invites;
        _access = access;
        _unitOfWork = unitOfWork;
        _hasher = hasher;
        _limiter = limiter;
        _commands = commands;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OperationResult<DeviceDto>> RedeemCodeAsync(
        string userId, string? userEmail, string code, string rateLimitKey,
        string? ipAddress, CancellationToken ct = default)
    {
        var normalized = DeviceRegistryService.NormalizePairingCode(code);

        if (normalized.Length is < 4 or > 16)
        {
            return OperationResult<DeviceDto>.Fail(InvalidCodeError);
        }

        if (_limiter.IsBlocked(rateLimitKey))
        {
            return OperationResult<DeviceDto>.Fail(TooManyAttemptsError);
        }

        // 1) Pairing code of an unclaimed device -> become the Owner.
        // Hashes are salted, so codes cannot be looked up directly; we verify
        // against each candidate (their number is small).
        var claimable = await _devices.GetClaimableAsync(ct);
        var device = claimable.FirstOrDefault(d => _hasher.Verify(normalized, d.PairingCodeHash!));

        if (device is not null)
        {
            return await ClaimAsOwnerAsync(device, userId, userEmail, rateLimitKey, ipAddress, ct);
        }

        // 2) Household invite code -> join an already claimed device as Member.
        var usableInvites = await _invites.GetUsableAsync(_clock.UtcNow, ct);
        var invite = usableInvites.FirstOrDefault(i => _hasher.Verify(normalized, i.CodeHash));

        if (invite is not null)
        {
            return await JoinByInviteAsync(invite, userId, userEmail, rateLimitKey, ipAddress, ct);
        }

        _limiter.RecordFailure(rateLimitKey);
        _logger.LogWarning("Failed code redemption attempt by user {UserId}", userId);
        await _audit.LogAsync("device.claim.failed", userId, userEmail,
            details: "Invalid pairing/invite code", ipAddress: ipAddress, ct: ct);

        return OperationResult<DeviceDto>.Fail(InvalidCodeError);
    }

    public async Task<OperationResult<DeviceInviteDto>> CreateInviteAsync(
        string userId, string? userEmail, string deviceId, string? ipAddress, CancellationToken ct = default)
    {
        var access = await _access.GetAccessibleDeviceAsync(userId, deviceId, requireOwner: true, ct);

        if (!access.Succeeded)
        {
            return OperationResult<DeviceInviteDto>.Fail(access.Error!);
        }

        var now = _clock.UtcNow;

        if (await _invites.CountUsableForDeviceAsync(deviceId, now, ct) >= MaxActiveInvitesPerDevice)
        {
            return OperationResult<DeviceInviteDto>.Fail(
                "Too many active invites for this device. Wait for one to expire.");
        }

        var code = GenerateCode(InviteCodeLength);
        var expiresAtUtc = now + InviteLifetime;

        await _invites.AddAsync(new DeviceInvite
        {
            DeviceId = deviceId,
            CodeHash = _hasher.Hash(code),
            CreatedByUserId = userId,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc,
            MaxUses = InviteMaxUses
        }, ct);

        await _audit.LogAsync("device.invite.created", userId, userEmail, deviceId,
            ipAddress: ipAddress, ct: ct);

        _logger.LogInformation("Invite created for device {DeviceId} by {UserId}", deviceId, userId);
        return OperationResult<DeviceInviteDto>.Ok(new DeviceInviteDto(code, expiresAtUtc, InviteMaxUses));
    }

    public async Task<OperationResult<IReadOnlyList<DeviceMemberDto>>> GetMembersAsync(
        string userId, string deviceId, CancellationToken ct = default)
    {
        var access = await _access.GetAccessibleDeviceAsync(userId, deviceId, requireOwner: true, ct);

        if (!access.Succeeded)
        {
            return OperationResult<IReadOnlyList<DeviceMemberDto>>.Fail(access.Error!);
        }

        var members = await _memberships.GetForDeviceAsync(deviceId, ct);

        IReadOnlyList<DeviceMemberDto> dtos = members
            .OrderBy(m => m.Role)
            .ThenBy(m => m.JoinedAtUtc)
            .Select(m => new DeviceMemberDto(m.UserId, null, DeviceDto.RoleName(m.Role), m.JoinedAtUtc))
            .ToList();

        return OperationResult<IReadOnlyList<DeviceMemberDto>>.Ok(dtos);
    }

    public async Task<OperationResult> RemoveMemberAsync(
        string callerUserId, string? callerEmail, string deviceId, string targetUserId,
        string? ipAddress, CancellationToken ct = default)
    {
        var access = await _access.GetAccessibleDeviceAsync(callerUserId, deviceId, requireOwner: false, ct);

        if (!access.Succeeded)
        {
            return OperationResult.Fail(access.Error!);
        }

        var removingSelf = string.Equals(callerUserId, targetUserId, StringComparison.Ordinal);

        if (!removingSelf && access.Value!.Role != DeviceRole.Owner)
        {
            return OperationResult.Fail(DeviceAccessService.NotFoundError);
        }

        var target = await _memberships.GetAsync(deviceId, targetUserId, ct);

        if (target is null)
        {
            return OperationResult.Fail("That user is not a member of this device.");
        }

        if (target.Role == DeviceRole.Owner)
        {
            return OperationResult.Fail("The owner cannot be removed. Use release instead.");
        }

        _memberships.Remove(target);
        await _audit.LogAsync(removingSelf ? "device.member.left" : "device.member.removed",
            callerUserId, callerEmail, deviceId, details: $"target={targetUserId}",
            ipAddress: ipAddress, ct: ct);

        return OperationResult.Ok();
    }

    public async Task<OperationResult> ReleaseAsync(
        string userId, string? userEmail, string deviceId, string? ipAddress, CancellationToken ct = default)
    {
        var access = await _access.GetAccessibleDeviceAsync(userId, deviceId, requireOwner: true, ct);

        if (!access.Succeeded)
        {
            return OperationResult.Fail(access.Error!);
        }

        var device = access.Value!.Device;
        device.ClaimedAtUtc = null;
        device.PairingCodeHash = null; // a fresh code will arrive from the device

        await _memberships.RemoveAllForDeviceAsync(deviceId, ct);
        await _invites.RemoveAllForDeviceAsync(deviceId, ct);

        await _audit.LogAsync("device.release", userId, userEmail, device.Id, ipAddress: ipAddress, ct: ct);

        if (!await _commands.PublishClaimedAsync(device.Id, claimed: false, ct))
        {
            _logger.LogWarning("Could not publish release for {DeviceId}; will heal on next announce", device.Id);
        }

        _logger.LogInformation("Device {DeviceId} released by owner {UserId}", device.Id, userId);
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

        var access = await _access.GetAccessibleDeviceAsync(userId, deviceId, requireOwner: true, ct);

        if (!access.Succeeded)
        {
            return OperationResult<DeviceDto>.Fail(access.Error!);
        }

        access.Value!.Device.Name = name;
        await _unitOfWork.SaveChangesAsync(ct);

        return OperationResult<DeviceDto>.Ok(DeviceDto.FromEntity(access.Value.Device, access.Value.Role));
    }

    private async Task<OperationResult<DeviceDto>> ClaimAsOwnerAsync(
        Device device, string userId, string? userEmail, string rateLimitKey,
        string? ipAddress, CancellationToken ct)
    {
        device.ClaimedAtUtc = _clock.UtcNow;
        device.PairingCodeHash = null; // the pairing code is single-use

        await _memberships.AddAsync(new DeviceMembership
        {
            DeviceId = device.Id,
            UserId = userId,
            Role = DeviceRole.Owner,
            JoinedAtUtc = _clock.UtcNow
        }, ct);

        _limiter.RecordSuccess(rateLimitKey);

        // Commits the staged changes together with the audit entry.
        await _audit.LogAsync("device.claim", userId, userEmail, device.Id, ipAddress: ipAddress, ct: ct);

        // Retained message: the device learns it is claimed even if it is
        // temporarily offline right now. Delivery failure is not fatal - the
        // registry heals the claimed state when the device re-announces itself.
        if (!await _commands.PublishClaimedAsync(device.Id, claimed: true, ct))
        {
            _logger.LogWarning("Could not publish claimed state for {DeviceId}; will heal on next announce", device.Id);
        }

        _logger.LogInformation("Device {DeviceId} claimed by user {UserId} (owner)", device.Id, userId);
        return OperationResult<DeviceDto>.Ok(DeviceDto.FromEntity(device, DeviceRole.Owner));
    }

    private async Task<OperationResult<DeviceDto>> JoinByInviteAsync(
        DeviceInvite invite, string userId, string? userEmail, string rateLimitKey,
        string? ipAddress, CancellationToken ct)
    {
        var device = await _devices.GetByIdAsync(invite.DeviceId, ct);

        if (device is null)
        {
            return OperationResult<DeviceDto>.Fail(InvalidCodeError);
        }

        if (await _memberships.GetAsync(device.Id, userId, ct) is not null)
        {
            return OperationResult<DeviceDto>.Fail(AlreadyMemberError);
        }

        invite.UseCount++;

        await _memberships.AddAsync(new DeviceMembership
        {
            DeviceId = device.Id,
            UserId = userId,
            Role = DeviceRole.Member,
            JoinedAtUtc = _clock.UtcNow
        }, ct);

        _limiter.RecordSuccess(rateLimitKey);
        await _audit.LogAsync("device.member.joined", userId, userEmail, device.Id,
            ipAddress: ipAddress, ct: ct);

        _logger.LogInformation("User {UserId} joined device {DeviceId} via invite", userId, device.Id);
        return OperationResult<DeviceDto>.Ok(DeviceDto.FromEntity(device, DeviceRole.Member));
    }

    private static string GenerateCode(int length)
    {
        var chars = new char[length];

        for (var i = 0; i < length; i++)
        {
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        }

        return new string(chars);
    }
}
