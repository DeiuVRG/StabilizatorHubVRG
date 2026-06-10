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
    private readonly FakeDeviceMembershipRepository _memberships = new();
    private readonly FakeDeviceInviteRepository _invites = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly PairingCodeHasher _hasher = new();
    private readonly FakeClaimAttemptLimiter _limiter = new();
    private readonly FakeCommandPublisher _publisher = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeClock _clock = new();

    public DeviceClaimServiceTests()
    {
        _devices.Memberships = _memberships;
    }

    private DeviceClaimService NewService() => new(
        _devices,
        _memberships,
        _invites,
        new DeviceAccessService(_devices, _memberships),
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

    // ------------------------------------------------------------------
    // Pairing code -> Owner
    // ------------------------------------------------------------------

    [Fact]
    public async Task RedeemPairingCode_MakesTheUserOwnerAndNotifiesDevice()
    {
        var device = AddUnclaimedDevice();
        var service = NewService();

        var result = await service.RedeemCodeAsync("user-1", "u@x.ro", "7f3k9q ", "key", "1.2.3.4");

        Assert.True(result.Succeeded);
        Assert.Equal("owner", result.Value!.Role);
        Assert.Null(device.PairingCodeHash);             // the code is single-use
        Assert.Equal(_clock.UtcNow, device.ClaimedAtUtc);

        var membership = Assert.Single(_memberships.Items);
        Assert.Equal(DeviceRole.Owner, membership.Role);
        Assert.Equal("user-1", membership.UserId);

        Assert.Contains(("A1B2C3D4E5F6", true), _publisher.ClaimedMessages);
        Assert.Contains("device.claim", _audit.Actions);
        Assert.Single(_limiter.Successes);
    }

    [Fact]
    public async Task RedeemWrongCode_FailsAndCountsTheAttempt()
    {
        AddUnclaimedDevice();
        var service = NewService();

        var result = await service.RedeemCodeAsync("user-1", "u@x.ro", "WRONG1", "key", null);

        Assert.False(result.Succeeded);
        Assert.Equal(DeviceClaimService.InvalidCodeError, result.Error);
        Assert.Single(_limiter.Failures);
        Assert.Contains("device.claim.failed", _audit.Actions);
        Assert.Empty(_publisher.ClaimedMessages);
    }

    [Fact]
    public async Task Redeem_WhenRateLimited_IsRejectedBeforeAnyVerification()
    {
        AddUnclaimedDevice();
        _limiter.Blocked = true;
        var service = NewService();

        var result = await service.RedeemCodeAsync("user-1", null, "7F3K9Q", "key", null);

        Assert.False(result.Succeeded);
        Assert.Equal(DeviceClaimService.TooManyAttemptsError, result.Error);
    }

    [Fact]
    public async Task ClaimedDevice_CannotBeClaimedAgainEvenWithTheOldCode()
    {
        var device = AddUnclaimedDevice();
        var service = NewService();
        await service.RedeemCodeAsync("user-1", null, "7F3K9Q", "k1", null);

        var result = await service.RedeemCodeAsync("attacker", null, "7F3K9Q", "k2", null);

        Assert.False(result.Succeeded);
        Assert.Single(_memberships.Items);
        Assert.Equal("user-1", _memberships.Items[0].UserId);
        _ = device;
    }

    // ------------------------------------------------------------------
    // Invite code -> household Member
    // ------------------------------------------------------------------

    private async Task<(DeviceClaimService Service, Device Device, string InviteCode)> ClaimedDeviceWithInviteAsync()
    {
        var device = AddUnclaimedDevice();
        var service = NewService();
        await service.RedeemCodeAsync("owner-1", "owner@x.ro", "7F3K9Q", "k", null);

        var invite = await service.CreateInviteAsync("owner-1", "owner@x.ro", device.Id, null);
        Assert.True(invite.Succeeded);

        return (service, device, invite.Value!.Code);
    }

    [Fact]
    public async Task RedeemInviteCode_JoinsTheDeviceAsMember()
    {
        var (service, device, code) = await ClaimedDeviceWithInviteAsync();

        var result = await service.RedeemCodeAsync("member-1", "m@x.ro", code, "k2", null);

        Assert.True(result.Succeeded);
        Assert.Equal("member", result.Value!.Role);
        Assert.Equal(device.Id, result.Value.Id);
        Assert.Equal(2, _memberships.Items.Count);
        Assert.Contains("device.member.joined", _audit.Actions);
        Assert.Equal(1, _invites.Items[0].UseCount);
    }

    [Fact]
    public async Task SameInviteCode_WorksForSeveralHouseholdMembers()
    {
        var (service, _, code) = await ClaimedDeviceWithInviteAsync();

        Assert.True((await service.RedeemCodeAsync("member-1", null, code, "k1", null)).Succeeded);
        Assert.True((await service.RedeemCodeAsync("member-2", null, code, "k2", null)).Succeeded);

        Assert.Equal(3, _memberships.Items.Count); // owner + 2 members
    }

    [Fact]
    public async Task RedeemInvite_Twice_BySameUser_IsRejected()
    {
        var (service, _, code) = await ClaimedDeviceWithInviteAsync();
        await service.RedeemCodeAsync("member-1", null, code, "k1", null);

        var result = await service.RedeemCodeAsync("member-1", null, code, "k1", null);

        Assert.False(result.Succeeded);
        Assert.Equal(DeviceClaimService.AlreadyMemberError, result.Error);
    }

    [Fact]
    public async Task ExpiredInvite_NoLongerWorks()
    {
        var (service, _, code) = await ClaimedDeviceWithInviteAsync();

        _clock.Advance(TimeSpan.FromHours(49)); // invites live 48 h

        var result = await service.RedeemCodeAsync("member-1", null, code, "k1", null);

        Assert.False(result.Succeeded);
        Assert.Equal(DeviceClaimService.InvalidCodeError, result.Error);
    }

    [Fact]
    public async Task CreateInvite_ByPlainMember_IsRejected()
    {
        var (service, device, code) = await ClaimedDeviceWithInviteAsync();
        await service.RedeemCodeAsync("member-1", null, code, "k1", null);

        var result = await service.CreateInviteAsync("member-1", null, device.Id, null);

        Assert.False(result.Succeeded);
    }

    // ------------------------------------------------------------------
    // Member management
    // ------------------------------------------------------------------

    [Fact]
    public async Task Owner_CanRemoveAMember()
    {
        var (service, device, code) = await ClaimedDeviceWithInviteAsync();
        await service.RedeemCodeAsync("member-1", null, code, "k1", null);

        var result = await service.RemoveMemberAsync("owner-1", null, device.Id, "member-1", null);

        Assert.True(result.Succeeded);
        Assert.Single(_memberships.Items); // only the owner remains
        Assert.Contains("device.member.removed", _audit.Actions);
    }

    [Fact]
    public async Task Member_CanLeave_ButCannotRemoveOthers()
    {
        var (service, device, code) = await ClaimedDeviceWithInviteAsync();
        await service.RedeemCodeAsync("member-1", null, code, "k1", null);
        await service.RedeemCodeAsync("member-2", null, code, "k2", null);

        var removeOther = await service.RemoveMemberAsync("member-1", null, device.Id, "member-2", null);
        Assert.False(removeOther.Succeeded);

        var leave = await service.RemoveMemberAsync("member-1", null, device.Id, "member-1", null);
        Assert.True(leave.Succeeded);
        Assert.Equal(2, _memberships.Items.Count);
        Assert.Contains("device.member.left", _audit.Actions);
    }

    [Fact]
    public async Task Owner_CannotBeRemoved_OnlyReleased()
    {
        var (service, device, _) = await ClaimedDeviceWithInviteAsync();

        var result = await service.RemoveMemberAsync("owner-1", null, device.Id, "owner-1", null);

        Assert.False(result.Succeeded);
        Assert.Single(_memberships.Items);
    }

    // ------------------------------------------------------------------
    // Release
    // ------------------------------------------------------------------

    [Fact]
    public async Task Release_ByOwner_RemovesAllMembersAndInvites()
    {
        var (service, device, code) = await ClaimedDeviceWithInviteAsync();
        await service.RedeemCodeAsync("member-1", null, code, "k1", null);

        var result = await service.ReleaseAsync("owner-1", null, device.Id, null);

        Assert.True(result.Succeeded);
        Assert.Empty(_memberships.Items);
        Assert.Empty(_invites.Items);
        Assert.Null(device.ClaimedAtUtc);
        Assert.Contains((device.Id, false), _publisher.ClaimedMessages);
    }

    [Fact]
    public async Task Release_ByPlainMember_IsRejected()
    {
        var (service, device, code) = await ClaimedDeviceWithInviteAsync();
        await service.RedeemCodeAsync("member-1", null, code, "k1", null);

        var result = await service.ReleaseAsync("member-1", null, device.Id, null);

        Assert.False(result.Succeeded);
        Assert.Equal(2, _memberships.Items.Count);
    }

    // ------------------------------------------------------------------
    // Rename
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_WithEmptyName_IsRejected(string name)
    {
        var (service, device, _) = await ClaimedDeviceWithInviteAsync();

        var result = await service.RenameAsync("owner-1", device.Id, name);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Rename_ByOwner_TrimsAndSaves()
    {
        var (service, device, _) = await ClaimedDeviceWithInviteAsync();

        var result = await service.RenameAsync("owner-1", device.Id, "  Boiler stabilizer  ");

        Assert.True(result.Succeeded);
        Assert.Equal("Boiler stabilizer", device.Name);
        Assert.True(_unitOfWork.SaveCount > 0);
    }

    [Fact]
    public async Task Rename_ByPlainMember_IsRejected()
    {
        var (service, device, code) = await ClaimedDeviceWithInviteAsync();
        await service.RedeemCodeAsync("member-1", null, code, "k1", null);

        var result = await service.RenameAsync("member-1", device.Id, "New name");

        Assert.False(result.Succeeded);
    }
}
