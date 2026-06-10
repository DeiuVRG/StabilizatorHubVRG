using Microsoft.Extensions.DependencyInjection;
using StabilizatorHub.Application.Services;

namespace StabilizatorHub.Application;

/// <summary>Registers the application-layer use cases in the DI container.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Singleton: carries per-device episode state across telemetry samples.
        services.AddSingleton<VoltageEventTrackerRegistry>();

        services.AddScoped<ITelemetryIngestionService, TelemetryIngestionService>();
        services.AddScoped<IDeviceRegistryService, DeviceRegistryService>();
        services.AddScoped<IDeviceClaimService, DeviceClaimService>();
        services.AddScoped<IDeviceControlService, DeviceControlService>();
        services.AddScoped<IConsumptionService, ConsumptionService>();
        services.AddScoped<IEventQueryService, EventQueryService>();
        services.AddScoped<IDeviceAccessService, DeviceAccessService>();
        services.AddScoped<IDeviceQueryService, DeviceQueryService>();
        services.AddScoped<IAuditService, AuditService>();

        return services;
    }
}
