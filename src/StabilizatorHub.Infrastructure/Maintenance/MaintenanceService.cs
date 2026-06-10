using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Options;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Infrastructure.Maintenance;

/// <summary>
/// Periodic housekeeping:
///  - marks devices offline when telemetry stops without a Last Will
///    (e.g. router power cut takes device and broker connectivity down together);
///  - once per day prunes raw readings beyond the configured retention.
/// </summary>
public sealed class MaintenanceService : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TelemetryOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<MaintenanceService> _logger;

    private DateOnly _lastPruneDate = DateOnly.MinValue;

    public MaintenanceService(
        IServiceScopeFactory scopeFactory,
        IOptions<TelemetryOptions> options,
        IClock clock,
        ILogger<MaintenanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
                await MarkStaleDevicesOfflineAsync(stoppingToken);
                await PruneOldReadingsOncePerDayAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Maintenance cycle failed");
            }
        }
    }

    private async Task MarkStaleDevicesOfflineAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        var devices = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
        var registry = scope.ServiceProvider.GetRequiredService<IDeviceRegistryService>();

        var cutoff = _clock.UtcNow.AddSeconds(-_options.OfflineAfterSeconds);

        foreach (var device in await devices.GetAllAsync(ct))
        {
            var lastActivity = device.LastTelemetryUtc ?? device.LastSeenUtc;

            if (device.IsOnline && (lastActivity is null || lastActivity < cutoff))
            {
                _logger.LogInformation("Device {DeviceId} silent since {LastActivity:u} - marking offline",
                    device.Id, lastActivity);
                await registry.HandleStatusAsync(device.Id, online: false, ct);
            }
        }
    }

    private async Task PruneOldReadingsOncePerDayAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(_clock.UtcNow);

        if (_lastPruneDate == today)
        {
            return;
        }

        _lastPruneDate = today;

        using var scope = _scopeFactory.CreateScope();

        var invites = scope.ServiceProvider.GetRequiredService<IDeviceInviteRepository>();
        var expired = await invites.DeleteUnusableAsync(_clock.UtcNow, ct);

        if (expired > 0)
        {
            _logger.LogInformation("Removed {Count} expired/exhausted device invites", expired);
        }

        if (_options.RawRetentionDays <= 0)
        {
            return;
        }

        var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
        var cutoff = _clock.UtcNow.AddDays(-_options.RawRetentionDays);
        var removed = await telemetry.DeleteOlderThanAsync(cutoff, ct);

        if (removed > 0)
        {
            _logger.LogInformation("Pruned {Count} telemetry readings older than {Cutoff:u}", removed, cutoff);
        }
    }
}
