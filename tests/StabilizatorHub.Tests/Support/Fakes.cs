using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Tests.Support;

public sealed class FakeDeviceRepository : IDeviceRepository
{
    public List<Device> Devices { get; } = new();

    public Task<Device?> GetByIdAsync(string deviceId, CancellationToken ct = default) =>
        Task.FromResult(Devices.FirstOrDefault(d => d.Id == deviceId));

    public Task<IReadOnlyList<Device>> GetByOwnerAsync(string userId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Device>>(Devices.Where(d => d.OwnerUserId == userId).ToList());

    public Task<IReadOnlyList<Device>> GetClaimableAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Device>>(
            Devices.Where(d => d.OwnerUserId == null && d.PairingCodeHash != null).ToList());

    public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Device>>(Devices.ToList());

    public Task AddAsync(Device device, CancellationToken ct = default)
    {
        Devices.Add(device);
        return Task.CompletedTask;
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
