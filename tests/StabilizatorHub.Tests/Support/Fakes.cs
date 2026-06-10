using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Tests.Support;

public sealed class FakeDeviceRepository : IDeviceRepository
{
    public List<Device> Devices { get; } = new();

    public FakeDeviceMembershipRepository? Memberships { get; set; }

    public Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default) =>
        Task.FromResult(Devices.FirstOrDefault(d => d.Id == deviceId));

    public Task<IReadOnlyList<DeviceWithRole>> GetForMemberAsync(string userId, CancellationToken ct = default)
    {
        IReadOnlyList<DeviceWithRole> result = (Memberships?.Items ?? new List<DeviceMembership>())
            .Where(m => m.UserId == userId)
            .Join(Devices, m => m.DeviceId, d => d.Id, (m, d) => new DeviceWithRole(d, m.Role))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Device>> GetClaimableAsync(CancellationToken ct = default)
    {
        var memberDeviceIds = (Memberships?.Items ?? new List<DeviceMembership>())
            .Select(m => m.DeviceId)
            .ToHashSet();

        IReadOnlyList<Device> result = Devices
            .Where(d => d.PairingCodeHash != null && !memberDeviceIds.Contains(d.Id))
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Device>>(Devices.ToList());

    public Task<IReadOnlyList<DeviceWithMemberCount>> GetAllWithMemberCountAsync(CancellationToken ct = default)
    {
        IReadOnlyList<DeviceWithMemberCount> result = Devices
            .Select(d => new DeviceWithMemberCount(
                d, (Memberships?.Items ?? new List<DeviceMembership>()).Count(m => m.DeviceId == d.Id)))
            .ToList();

        return Task.FromResult(result);
    }

    public Task AddAsync(Device device, CancellationToken ct = default)
    {
        Devices.Add(device);
        return Task.CompletedTask;
    }
}

public sealed class FakeDeviceMembershipRepository : IDeviceMembershipRepository
{
    public List<DeviceMembership> Items { get; } = new();

    public Task<DeviceMembership?> GetAsync(string deviceId, string userId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(m => m.DeviceId == deviceId && m.UserId == userId));

    public Task<IReadOnlyList<DeviceMembership>> GetForDeviceAsync(string deviceId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeviceMembership>>(Items.Where(m => m.DeviceId == deviceId).ToList());

    public Task<bool> AnyForDeviceAsync(string deviceId, CancellationToken ct = default) =>
        Task.FromResult(Items.Any(m => m.DeviceId == deviceId));

    public Task AddAsync(DeviceMembership membership, CancellationToken ct = default)
    {
        Items.Add(membership);
        return Task.CompletedTask;
    }

    public void Remove(DeviceMembership membership) => Items.Remove(membership);

    public Task RemoveAllForDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        Items.RemoveAll(m => m.DeviceId == deviceId);
        return Task.CompletedTask;
    }
}

public sealed class FakeDeviceInviteRepository : IDeviceInviteRepository
{
    public List<DeviceInvite> Items { get; } = new();

    public Task<IReadOnlyList<DeviceInvite>> GetUsableAsync(DateTime nowUtc, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DeviceInvite>>(Items.Where(i => i.IsUsable(nowUtc)).ToList());

    public Task<int> CountUsableForDeviceAsync(string deviceId, DateTime nowUtc, CancellationToken ct = default) =>
        Task.FromResult(Items.Count(i => i.DeviceId == deviceId && i.IsUsable(nowUtc)));

    public Task AddAsync(DeviceInvite invite, CancellationToken ct = default)
    {
        Items.Add(invite);
        return Task.CompletedTask;
    }

    public Task RemoveAllForDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        Items.RemoveAll(i => i.DeviceId == deviceId);
        return Task.CompletedTask;
    }

    public Task<int> DeleteUnusableAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var removed = Items.RemoveAll(i => !i.IsUsable(nowUtc));
        return Task.FromResult(removed);
    }
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

public sealed class FakeCommandPublisher : IDeviceCommandPublisher
{
    public List<(string DeviceId, bool On)> OutputCommands { get; } = new();

    public List<(string DeviceId, bool Claimed)> ClaimedMessages { get; } = new();

    public bool DeliverySucceeds { get; set; } = true;

    public Task<bool> PublishOutputCommandAsync(string deviceId, bool on, CancellationToken ct = default)
    {
        OutputCommands.Add((deviceId, on));
        return Task.FromResult(DeliverySucceeds);
    }

    public Task<bool> PublishClaimedAsync(string deviceId, bool claimed, CancellationToken ct = default)
    {
        ClaimedMessages.Add((deviceId, claimed));
        return Task.FromResult(DeliverySucceeds);
    }
}

public sealed class FakeClaimAttemptLimiter : IClaimAttemptLimiter
{
    public bool Blocked { get; set; }

    public List<string> Failures { get; } = new();

    public List<string> Successes { get; } = new();

    public bool IsBlocked(string key) => Blocked;

    public void RecordFailure(string key) => Failures.Add(key);

    public void RecordSuccess(string key) => Successes.Add(key);
}

public sealed class FakeAuditService : StabilizatorHub.Application.Services.IAuditService
{
    public List<string> Actions { get; } = new();

    public Task LogAsync(
        string action, string? userId = null, string? userEmail = null, string? deviceId = null,
        string? details = null, string? ipAddress = null, CancellationToken ct = default)
    {
        Actions.Add(action);
        return Task.CompletedTask;
    }
}
