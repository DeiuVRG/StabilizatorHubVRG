using Microsoft.Extensions.Logging.Abstractions;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Domain.Entities;
using StabilizatorHub.Infrastructure.Security;
using StabilizatorHub.Tests.Support;
using Xunit;

namespace StabilizatorHub.Tests;

public class DeviceClaimServiceTests
{
    private readonly FakeDeviceRepository _devices = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly PairingCodeHasher _hasher = new();
    private readonly FakeClaimAttemptLimiter _limiter = new();
    private readonly FakeCommandPublisher _publisher = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeClock _clock = new();

    private DeviceClaimService NewService() => new(
        _devices,
        new DeviceAccessService(_devices),
        _unitOfWork,
        _hasher,
        _limiter,
        _publisher,
        _audit,
        _clock,
        NullLogger<DeviceClaimService>.Instance);

    private Device AddUnclaimedDevice(string id = "A1B2C3D4E5F6", string code = "7F3K9Q")
    {
        var device = new Device
        {
            Id = id,
            Name = id,
            PairingCodeHash = _hasher.Hash(code),
            CreatedAtUtc = _clock.UtcNow
        };

        _devices.Devices.Add(device);
        return device;
    }

    [Fact]
    public async Task Claim_WithCorrectCode_AssignsOwnerAndNotifiesDevice()
    {
        var device = AddUnclaimedDevice();
        var service = NewService();

        var result = await service.ClaimAsync("user-1", "u@x.ro", "7f3k9q ", "key", "1.2.3.4");

        Assert.True(result.Succeeded);
        Assert.Equal("user-1", device.OwnerUserId);
        Assert.Null(device.PairingCodeHash);             // the code is single-use
        Assert.Equal(_clock.UtcNow, device.ClaimedAtUtc);
        Assert.Contains(("A1B2C3D4E5F6", true), _publisher.ClaimedMessages);
        Assert.Contains("device.claim", _audit.Actions);
        Assert.Single(_limiter.Successes);
    }

    [Fact]
    public async Task Claim_WithWrongCode_FailsAndCountsTheAttempt()
    {
        AddUnclaimedDevice();
        var service = NewService();

        var result = await service.ClaimAsync("user-1", "u@x.ro", "WRONG1", "key", null);

        Assert.False(result.Succeeded);
        Assert.Equal(DeviceClaimService.InvalidCodeError, result.Error);
        Assert.Single(_limiter.Failures);
        Assert.Contains("device.claim.failed", _audit.Actions);
        Assert.Empty(_publisher.ClaimedMessages);
    }

    [Fact]
    public async Task Claim_WhenRateLimited_IsRejectedBeforeAnyVerification()
    {
        AddUnclaimedDevice();
        _limiter.Blocked = true;
        var service = NewService();

        var result = await service.ClaimAsync("user-1", null, "7F3K9Q", "key", null);

        Assert.False(result.Succeeded);
        Assert.Equal(DeviceClaimService.TooManyAttemptsError, result.Error);
    }

    [Fact]
    public async Task Claim_AnAlreadyClaimedDevice_IsImpossibleEvenWithTheOldCode()
    {
        var device = AddUnclaimedDevice();
        var service = NewService();
        await service.ClaimAsync("user-1", null, "7F3K9Q", "k1", null);

        // The legitimate owner exists; an attacker re-tries the same code.
        var result = await service.ClaimAsync("attacker", null, "7F3K9Q", "k2", null);

        Assert.False(result.Succeeded);
        Assert.Equal("user-1", device.OwnerUserId);
    }

    [Fact]
    public async Task Release_ByOwner_ClearsOwnershipAndPublishesUnclaim()
    {
        var device = AddUnclaimedDevice();
        var service = NewService();
        await service.ClaimAsync("user-1", null, "7F3K9Q", "k", null);

        var result = await service.ReleaseAsync("user-1", "u@x.ro", device.Id, null);

        Assert.True(result.Succeeded);
        Assert.Null(device.OwnerUserId);
        Assert.Contains((device.Id, false), _publisher.ClaimedMessages);
        Assert.Contains("device.release", _audit.Actions);
    }

    [Fact]
    public async Task Release_ByNonOwner_IsRejected()
    {
        var device = AddUnclaimedDevice();
        var service = NewService();
        await service.ClaimAsync("user-1", null, "7F3K9Q", "k", null);

        var result = await service.ReleaseAsync("intruder", null, device.Id, null);

        Assert.False(result.Succeeded);
        Assert.Equal("user-1", device.OwnerUserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_WithEmptyName_IsRejected(string name)
    {
        var device = AddUnclaimedDevice();
        device.OwnerUserId = "user-1";
        var service = NewService();

        var result = await service.RenameAsync("user-1", device.Id, name);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Rename_ByOwner_TrimsAndSaves()
    {
        var device = AddUnclaimedDevice();
        device.OwnerUserId = "user-1";
        var service = NewService();

        var result = await service.RenameAsync("user-1", device.Id, "  Boiler stabilizer  ");

        Assert.True(result.Succeeded);
        Assert.Equal("Boiler stabilizer", device.Name);
        Assert.True(_unitOfWork.SaveCount > 0);
    }
}
