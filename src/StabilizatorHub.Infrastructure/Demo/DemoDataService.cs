using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Options;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Domain.Abstractions;
using StabilizatorHub.Domain.Entities;
using StabilizatorHub.Domain.Services;
using StabilizatorHub.Domain.ValueObjects;
using StabilizatorHub.Infrastructure.Persistence;

namespace StabilizatorHub.Infrastructure.Demo;

/// <summary>
/// Powers the demo mode:
///  - seeds the read-only demo account, the simulated device, its Member
///    grant and (once) a configurable history of readings + voltage events;
///  - then feeds one synthetic sample per minute through the REAL ingestion
///    pipeline, so the demo dashboard is live (SignalR, charts, events) and
///    behaves exactly like a physical stabilizer.
/// </summary>
public sealed class DemoDataService : BackgroundService
{
    private static readonly TimeSpan LiveInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan BackfillStep = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DemoOptions _options;
    private readonly VoltageMonitorOptions _voltageOptions;
    private readonly IClock _clock;
    private readonly ILogger<DemoDataService> _logger;

    public DemoDataService(
        IServiceScopeFactory scopeFactory,
        IOptions<DemoOptions> options,
        IOptions<VoltageMonitorOptions> voltageOptions,
        IClock clock,
        ILogger<DemoDataService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _voltageOptions = voltageOptions.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        try
        {
            await SeedAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demo seeding failed - demo mode unavailable");
            return;
        }

        _logger.LogInformation("Demo mode active: device {DeviceId} emits one sample per minute",
            _options.DeviceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(LiveInterval, stoppingToken);

                var now = _clock.UtcNow;
                var sample = DemoSignalGenerator.At(now);

                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ITelemetryIngestionService>()
                    .IngestAsync(ToTelemetrySample(sample, now), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Demo telemetry tick failed");
            }
        }
    }

    private async Task SeedAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var services = scope.ServiceProvider;

        var devices = services.GetRequiredService<IDeviceRepository>();
        var memberships = services.GetRequiredService<IDeviceMembershipRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var now = _clock.UtcNow;

        // 1) Simulated device (never claimable: it has no pairing code).
        var device = await devices.GetByIdAsync(_options.DeviceId, ct);

        if (device is null)
        {
            device = new Device
            {
                Id = _options.DeviceId,
                Name = _options.DeviceName,
                FirmwareVersion = "demo",
                CreatedAtUtc = now,
                ClaimedAtUtc = now,
                OutputOn = true
            };
            await devices.AddAsync(device, ct);
        }

        // 2) Read-only demo account. The password is random and never shared:
        // the demo endpoint signs the session in directly.
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var demoUser = await userManager.FindByEmailAsync(_options.Email);

        if (demoUser is null)
        {
            demoUser = new ApplicationUser
            {
                UserName = _options.Email,
                Email = _options.Email,
                CreatedAtUtc = now
            };

            var password = $"Dm9x{Guid.NewGuid():N}Z";
            var created = await userManager.CreateAsync(demoUser, password);

            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    "Could not create the demo account: " +
                    string.Join("; ", created.Errors.Select(e => e.Description)));
            }
        }

        // 3) Member grant (not Owner: owner-only actions stay locked away).
        if (await memberships.GetAsync(device.Id, demoUser.Id, ct) is null)
        {
            await memberships.AddAsync(new DeviceMembership
            {
                DeviceId = device.Id,
                UserId = demoUser.Id,
                Role = DeviceRole.Member,
                JoinedAtUtc = now
            }, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);

        // 4) History, generated once on an empty device.
        var telemetry = services.GetRequiredService<ITelemetryRepository>();

        if (await telemetry.GetLatestAsync(device.Id, ct) is null)
        {
            await BackfillAsync(services, device, now, ct);
        }
    }

    /// <summary>
    /// Writes BackfillDays of history: 10-minute readings (1-minute for the
    /// last hour, matching the live chart) plus the voltage events the domain
    /// tracker derives from the same signal. Bypasses ingestion on purpose -
    /// thousands of broadcasts/log lines would be pointless.
    /// </summary>
    private async Task BackfillAsync(IServiceProvider services, Device device, DateTime now, CancellationToken ct)
    {
        var telemetry = services.GetRequiredService<ITelemetryRepository>();
        var events = services.GetRequiredService<IVoltageEventRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var tracker = new VoltageEventTracker(_voltageOptions.ToThresholds());
        VoltageEvent? openEvent = null;
        var count = 0;

        var start = now.AddDays(-Math.Clamp(_options.BackfillDays, 1, 365));
        var lastHourStart = now.AddHours(-1);

        for (var t = start; t < now; t = t.Add(t < lastHourStart ? BackfillStep : LiveInterval))
        {
            var step = t < lastHourStart ? BackfillStep : LiveInterval;
            var sample = DemoSignalGenerator.At(t);

            await telemetry.AddAsync(new TelemetryReading
            {
                DeviceId = device.Id,
                TimestampUtc = t,
                VoltageIn = sample.VoltageIn,
                VoltageOut = sample.VoltageOut,
                CurrentAmps = sample.CurrentAmps,
                PowerWatts = sample.PowerWatts,
                EnergyWh = sample.PowerWatts * step.TotalHours,
                OutputOn = true
            }, ct);

            foreach (var transition in tracker.Process(sample.VoltageIn, t))
            {
                switch (transition)
                {
                    case VoltageEventStarted started:
                        openEvent = new VoltageEvent
                        {
                            DeviceId = device.Id,
                            Type = started.Type,
                            StartedAtUtc = started.StartedAtUtc,
                            ExtremeVoltage = started.Voltage,
                            SampleCount = 1
                        };
                        await events.AddAsync(openEvent, ct);
                        break;

                    case VoltageEventProgressed progressed when openEvent is not null:
                        openEvent.ExtremeVoltage = progressed.ExtremeVoltage;
                        openEvent.SampleCount = progressed.SampleCount;
                        break;

                    case VoltageEventEnded ended when openEvent is not null:
                        openEvent.EndedAtUtc = ended.EndedAtUtc;
                        openEvent.ExtremeVoltage = ended.ExtremeVoltage;
                        openEvent.SampleCount = ended.SampleCount;
                        openEvent = null;
                        break;
                }
            }

            if (++count % 1000 == 0)
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
        }

        device.IsOnline = true;
        device.LastSeenUtc = now;
        device.LastTelemetryUtc = now;

        await unitOfWork.SaveChangesAsync(ct);
        _logger.LogInformation("Demo backfill complete: {Count} readings over {Days} days",
            count, _options.BackfillDays);
    }

    private TelemetrySample ToTelemetrySample(DemoSample sample, DateTime timestampUtc) =>
        new(
            DeviceId: _options.DeviceId,
            TimestampUtc: timestampUtc,
            VoltageIn: sample.VoltageIn,
            VoltageOut: sample.VoltageOut,
            CurrentAmps: sample.CurrentAmps,
            PowerWatts: sample.PowerWatts,
            OutputOn: true,
            FirmwareVersion: "demo");
}
