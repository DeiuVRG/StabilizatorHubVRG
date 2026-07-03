using Microsoft.Extensions.Logging;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Domain.Abstractions;
using StabilizatorHub.Domain.Entities;

namespace StabilizatorHub.Application.Services;

public sealed class DeviceRegistryService : IDeviceRegistryService
{
    private readonly IDeviceRepository _devices;
    private readonly IDeviceMembershipRepository _memberships;
    private readonly IVoltageEventRepository _events;
    private readonly IUnitOfWork _unitOfWork;
    private readonly VoltageEventTrackerRegistry _trackers;
    private readonly ITelemetryBroadcaster _broadcaster;
    private readonly IDeviceCommandPublisher _commands;
    private readonly IPairingCodeHasher _hasher;
    private readonly IClock _clock;
    private readonly ILogger<DeviceRegistryService> _logger;

    public DeviceRegistryService(
        IDeviceRepository devices,
        IDeviceMembershipRepository memberships,
        IVoltageEventRepository events,
        IUnitOfWork unitOfWork,
        VoltageEventTrackerRegistry trackers,
        ITelemetryBroadcaster broadcaster,
        IDeviceCommandPublisher commands,
        IPairingCodeHasher hasher,
        IClock clock,
        ILogger<DeviceRegistryService> logger)
    {
        _devices = devices;
        _memberships = memberships;
        _events = events;
        _unitOfWork = unitOfWork;
        _trackers = trackers;
        _broadcaster = broadcaster;
        _commands = commands;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleStatusAsync(string deviceId, bool online, CancellationToken ct = default)
    {
        var device = await GetOrRegisterAsync(deviceId, ct);

        var changed = device.IsOnline != online;
        device.IsOnline = online;
        device.LastSeenUtc = _clock.UtcNow;

        if (!online)
        {
            // An offline device cannot have its output energized, and the
            // firmware always boots with the SSR OFF (safe default). Reset the
            // stored state so the UI toggle shows OFF the moment the device
            // drops - it no longer stays stuck on a stale "on" until the next
            // telemetry frame confirms it.
            device.OutputOn = false;
            await CloseOpenVoltageEventAsync(device, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        if (changed)
        {
            _logger.LogInformation("Device {DeviceId} is now {Status}", deviceId, online ? "online" : "offline");
            await _broadcaster.BroadcastDeviceStatusAsync(
                new DeviceStatusDto(device.Id, device.IsOnline, device.OutputOn, device.LastSeenUtc), ct);
        }
    }

    public async Task HandleInfoAsync(
        string deviceId, string? pairingCode, string? firmwareVersion, CancellationToken ct = default)
    {
        var device = await GetOrRegisterAsync(deviceId, ct);
        device.LastSeenUtc = _clock.UtcNow;

        if (firmwareVersion is not null)
        {
            device.FirmwareVersion = firmwareVersion;
        }

        if (!string.IsNullOrWhiteSpace(pairingCode))
        {
            if (await _memberships.AnyForDeviceAsync(device.Id, ct))
            {
                // The firmware lost its claimed flag (e.g. flash wipe) - heal it
                // by re-publishing the retained claim state. The code is NOT stored:
                // a claimed device must not be claimable by anyone else.
                await _commands.PublishClaimedAsync(device.Id, claimed: true, ct);
            }
            else
            {
                device.PairingCodeHash = _hasher.Hash(NormalizePairingCode(pairingCode));
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    internal static string NormalizePairingCode(string code) =>
        code.Trim().ToUpperInvariant();

    private async Task<Device> GetOrRegisterAsync(string deviceId, CancellationToken ct)
    {
        var device = await _devices.GetByIdAsync(deviceId, ct);

        if (device is not null)
        {
            return device;
        }

        device = new Device
        {
            Id = deviceId,
            Name = deviceId,
            CreatedAtUtc = _clock.UtcNow
        };

        await _devices.AddAsync(device, ct);
        _logger.LogInformation("Registered new unclaimed device {DeviceId}", deviceId);
        return device;
    }

    /// <summary>An offline device cannot keep an episode open - close it at the last known sample time.</summary>
    private async Task CloseOpenVoltageEventAsync(Device device, CancellationToken ct)
    {
        var closedAtUtc = device.LastTelemetryUtc ?? _clock.UtcNow;

        var ended = _trackers.TryGet(device.Id)?.CloseAt(closedAtUtc);
        var open = await _events.GetOpenAsync(device.Id, ct);

        if (open is not null)
        {
            open.EndedAtUtc = ended?.EndedAtUtc ?? closedAtUtc;
            await _broadcaster.BroadcastVoltageEventAsync(VoltageEventDto.FromEntity(open), ct);
        }
    }
}
