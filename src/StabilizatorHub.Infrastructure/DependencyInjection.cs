using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Options;
using StabilizatorHub.Domain.Abstractions;
using StabilizatorHub.Infrastructure.Logging;
using StabilizatorHub.Infrastructure.Maintenance;
using StabilizatorHub.Infrastructure.Mqtt;
using StabilizatorHub.Infrastructure.Persistence;
using StabilizatorHub.Infrastructure.Persistence.Repositories;
using StabilizatorHub.Infrastructure.Security;
using StabilizatorHub.Infrastructure.Time;
using StabilizatorHub.Infrastructure.Update;

namespace StabilizatorHub.Infrastructure;

/// <summary>Registers the infrastructure implementations of the application ports.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Single writable location for DB, encrypted logs and the log key.
        var dataDirectory = Path.GetFullPath(configuration["Storage:DataDirectory"] ?? "data");
        Directory.CreateDirectory(dataDirectory);

        services.Configure<TelemetryOptions>(configuration.GetSection(TelemetryOptions.SectionName));
        services.Configure<VoltageMonitorOptions>(configuration.GetSection(VoltageMonitorOptions.SectionName));
        services.Configure<MqttOptions>(configuration.GetSection(MqttOptions.SectionName));
        services.Configure<UpdateOptions>(configuration.GetSection(UpdateOptions.SectionName));
        services.Configure<Demo.DemoOptions>(configuration.GetSection(Demo.DemoOptions.SectionName));
        services.PostConfigure<UpdateOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.TriggerFilePath))
            {
                options.TriggerFilePath = Path.Combine(dataDirectory, "update.requested");
            }
        });

        // Persistence: SQLite database in the data directory unless overridden.
        var connectionString = configuration.GetConnectionString("Default")
            ?? $"Data Source={Path.Combine(dataDirectory, "stabilizatorhub.db")}";

        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IDeviceMembershipRepository, DeviceMembershipRepository>();
        services.AddScoped<IDeviceInviteRepository, DeviceInviteRepository>();
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        services.AddScoped<IVoltageEventRepository, VoltageEventRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPairingCodeHasher, PairingCodeHasher>();
        services.AddSingleton<IClaimAttemptLimiter, InMemoryClaimAttemptLimiter>();

        // Encrypted telemetry log: singleton because it owns the cipher and the
        // append lock; the key is resolved once at startup.
        services.AddSingleton(provider =>
        {
            var options = configuration.GetSection(TelemetryLogOptions.SectionName)
                .Get<TelemetryLogOptions>() ?? new TelemetryLogOptions();

            var directory = Path.IsPathRooted(options.Directory)
                ? options.Directory
                : Path.Combine(dataDirectory, options.Directory);

            var key = TelemetryLogKeyProvider.Resolve(options, dataDirectory);

            return new EncryptedTelemetryLog(
                options,
                directory,
                new AesGcmLineCipher(key),
                provider.GetRequiredService<ILogger<EncryptedTelemetryLog>>());
        });
        services.AddSingleton<ITelemetryLogWriter>(p => p.GetRequiredService<EncryptedTelemetryLog>());
        services.AddSingleton<IEncryptedLogReader>(p => p.GetRequiredService<EncryptedTelemetryLog>());

        // MQTT: one connection owned by the hosted service, also used for publishing.
        services.AddSingleton<MqttConnectionService>();
        services.AddHostedService(p => p.GetRequiredService<MqttConnectionService>());
        services.AddSingleton<IDeviceCommandPublisher, MqttCommandPublisher>();

        // Self-update.
        services.AddHttpClient<IUpdateChecker, GitHubUpdateChecker>();
        services.AddSingleton<IUpdateTrigger, FileUpdateTrigger>();

        // Housekeeping.
        services.AddHostedService<MaintenanceService>();

        // Demo mode (no-op unless Demo:Enabled).
        services.AddHostedService<Demo.DemoDataService>();

        return services;
    }
}
