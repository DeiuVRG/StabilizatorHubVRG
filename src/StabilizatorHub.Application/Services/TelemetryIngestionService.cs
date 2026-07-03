using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Application.Options;
using StabilizatorHub.Domain.Entities;
using StabilizatorHub.Domain.Services;
using StabilizatorHub.Domain.ValueObjects;

namespace StabilizatorHub.Application.Services;

/// <summary>
/// Orchestrates the ingestion pipeline for one telemetry sample:
/// device registration/refresh, server-side energy integration, persistence,
/// voltage-event detection, encrypted log append and real-time broadcast.
/// </summary>
public sealed class TelemetryIngestionService : ITelemetryIngestionService
{
    private readonly IDeviceRepository _devices;
    private readonly ITelemetryRepository _telemetry;
    private readonly IVoltageEventRepository _events;
    private readonly IUnitOfWork _unitOfWork;
    private readonly VoltageEventTrackerRegistry _trackers;
    private readonly ITelemetryBroadcaster _broadcaster;
    private readonly ITelemetryLogWriter _logWriter;
    private readonly TelemetryOptions _telemetryOptions;
    private readonly VoltageMonitorOptions _voltageOptions;
    private readonly ILogger<TelemetryIngestionService> _logger;

    public TelemetryIngestionService(
        IDeviceRepository devices,
        ITelemetryRepository telemetry,
        IVoltageEventRepository events,
        IUnitOfWork unitOfWork,
        VoltageEventTrackerRegistry trackers,
        ITelemetryBroadcaster broadcaster,
        ITelemetryLogWriter logWriter,
        IOptions<TelemetryOptions> telemetryOptions,
        IOptions<VoltageMonitorOptions> voltageOptions,
        ILogger<TelemetryIngestionService> logger)
    {
        _devices = devices;
        _telemetry = telemetry;
        _events = events;
        _unitOfWork = unitOfWork;
        _trackers = trackers;
        _broadcaster = broadcaster;
        _logWriter = logWriter;
        _telemetryOptions = telemetryOptions.Value;
        _voltageOptions = voltageOptions.Value;
        _logger = logger;
    }

    public async Task IngestAsync(TelemetrySample sample, CancellationToken ct = default)
    {
        var device = await GetOrRegisterDeviceAsync(sample, ct);
        var cameOnline = !device.IsOnline;

        device.IsOnline = true;
        device.LastSeenUtc = sample.TimestampUtc;

        if (sample.FirmwareVersion is not null)
        {
            device.FirmwareVersion = sample.FirmwareVersion;
        }

        if (sample.OutputOn is not null)
        {
            device.OutputOn = sample.OutputOn.Value;
        }

        // Store at most one reading per minute. Extra frames (relay command echo,
        // reconnect) still refresh the live UI below but are not written to
        // history, keeping the stored series at ~1-minute resolution. Energy is
        // integrated over the gap since the last STORED reading, so nothing is
        // lost when intermediate frames are skipped.
        var shouldPersist = device.LastTelemetryUtc is null
            || sample.TimestampUtc - device.LastTelemetryUtc.Value
               >= TimeSpan.FromSeconds(_telemetryOptions.MinPersistIntervalSeconds);

        var energyWh = shouldPersist
            ? EnergyCalculator.IntervalWattHours(
                sample.PowerWatts,
                device.LastTelemetryUtc,
                sample.TimestampUtc,
                TimeSpan.FromMinutes(_telemetryOptions.MaxEnergyGapMinutes))
            : 0;

        var reading = new TelemetryReading
        {
            DeviceId = device.Id,
            TimestampUtc = sample.TimestampUtc,
            VoltageIn = sample.VoltageIn,
            VoltageOut = sample.VoltageOut,
            CurrentAmps = sample.CurrentAmps,
            PowerWatts = sample.PowerWatts,
            EnergyWh = energyWh,
            OutputOn = device.OutputOn
        };

        if (shouldPersist)
        {
            await _telemetry.AddAsync(reading, ct);
            device.LastTelemetryUtc = sample.TimestampUtc;
        }

        var changedEvents = await ApplyVoltageTransitionsAsync(device.Id, sample, ct);

        await _unitOfWork.SaveChangesAsync(ct);

        // The live broadcast always fires (instant relay feedback + live tiles);
        // the encrypted history log follows the same 1-minute cadence as the DB.
        if (shouldPersist)
        {
            await AppendEncryptedLogAsync(sample, energyWh, ct);
        }

        await BroadcastAsync(device, reading, changedEvents, cameOnline, ct);
    }

    private async Task<Device> GetOrRegisterDeviceAsync(TelemetrySample sample, CancellationToken ct)
    {
        var device = await _devices.GetByIdAsync(sample.DeviceId, ct);

        if (device is not null)
        {
            return device;
        }

        device = new Device
        {
            Id = sample.DeviceId,
            Name = sample.DeviceId,
            CreatedAtUtc = sample.TimestampUtc
        };

        await _devices.AddAsync(device, ct);
        _logger.LogInformation("Registered new unclaimed device {DeviceId} from telemetry", device.Id);
        return device;
    }

    /// <summary>
    /// Runs the per-device voltage state machine and stages the resulting
    /// entity changes. Returns the events worth broadcasting (started/ended).
    /// </summary>
    private async Task<IReadOnlyList<VoltageEvent>> ApplyVoltageTransitionsAsync(
        string deviceId, TelemetrySample sample, CancellationToken ct)
    {
        var tracker = _trackers.TryGet(deviceId);

        if (tracker is null)
        {
            tracker = new VoltageEventTracker(_voltageOptions.ToThresholds());

            var openEvent = await _events.GetOpenAsync(deviceId, ct);
            if (openEvent is not null)
            {
                tracker.Restore(openEvent.Type, openEvent.StartedAtUtc, openEvent.ExtremeVoltage, openEvent.SampleCount);
            }

            tracker = _trackers.GetOrAdd(deviceId, tracker);
        }

        var transitions = tracker.Process(sample.VoltageIn, sample.TimestampUtc);

        if (transitions.Count == 0)
        {
            return Array.Empty<VoltageEvent>();
        }

        var changed = new List<VoltageEvent>();

        foreach (var transition in transitions)
        {
            switch (transition)
            {
                case VoltageEventEnded ended:
                {
                    var open = await _events.GetOpenAsync(deviceId, ct);
                    if (open is not null)
                    {
                        open.EndedAtUtc = ended.EndedAtUtc;
                        open.ExtremeVoltage = ended.ExtremeVoltage;
                        open.SampleCount = ended.SampleCount;
                        changed.Add(open);
                    }

                    break;
                }
                case VoltageEventStarted started:
                {
                    var voltageEvent = new VoltageEvent
                    {
                        DeviceId = deviceId,
                        Type = started.Type,
                        StartedAtUtc = started.StartedAtUtc,
                        ExtremeVoltage = started.Voltage,
                        SampleCount = 1
                    };

                    await _events.AddAsync(voltageEvent, ct);
                    changed.Add(voltageEvent);
                    _logger.LogWarning(
                        "Voltage event {Type} started on {DeviceId} at {Voltage} V",
                        started.Type, deviceId, started.Voltage);
                    break;
                }
                case VoltageEventProgressed progressed:
                {
                    var open = await _events.GetOpenAsync(deviceId, ct);
                    if (open is not null)
                    {
                        open.ExtremeVoltage = progressed.ExtremeVoltage;
                        open.SampleCount = progressed.SampleCount;
                    }

                    break;
                }
            }
        }

        return changed;
    }

    /// <summary>The encrypted file log must never break ingestion - failures are logged and swallowed.</summary>
    private async Task AppendEncryptedLogAsync(TelemetrySample sample, double energyWh, CancellationToken ct)
    {
        try
        {
            await _logWriter.AppendAsync(sample, energyWh, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append telemetry to the encrypted log");
        }
    }

    private async Task BroadcastAsync(
        Device device,
        TelemetryReading reading,
        IReadOnlyList<VoltageEvent> changedEvents,
        bool cameOnline,
        CancellationToken ct)
    {
        try
        {
            await _broadcaster.BroadcastTelemetryAsync(TelemetryDto.FromReading(reading), ct);

            if (cameOnline)
            {
                await _broadcaster.BroadcastDeviceStatusAsync(
                    new DeviceStatusDto(device.Id, device.IsOnline, device.OutputOn, device.LastSeenUtc), ct);
            }

            foreach (var voltageEvent in changedEvents)
            {
                await _broadcaster.BroadcastVoltageEventAsync(VoltageEventDto.FromEntity(voltageEvent), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Real-time broadcast failed for device {DeviceId}", device.Id);
        }
    }
}
