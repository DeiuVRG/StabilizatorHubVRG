using Microsoft.Extensions.Logging;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Common;

namespace StabilizatorHub.Application.Services;

public sealed class DeviceControlService : IDeviceControlService
{
    public const string DeviceOfflineError = "Device is offline - the command cannot be delivered right now.";

    private readonly IDeviceAccessService _access;
    private readonly IDeviceCommandPublisher _commands;
    private readonly IAuditService _audit;
    private readonly ILogger<DeviceControlService> _logger;

    public DeviceControlService(
        IDeviceAccessService access,
        IDeviceCommandPublisher commands,
        IAuditService audit,
        ILogger<DeviceControlService> logger)
    {
        _access = access;
        _commands = commands;
        _audit = audit;
        _logger = logger;
    }

    public async Task<OperationResult> SetOutputAsync(
        string userId, string? userEmail, string deviceId, bool on,
        string? ipAddress, CancellationToken ct = default)
    {
        // Any household member may switch the relay (not just the owner).
        var access = await _access.GetAccessibleDeviceAsync(userId, deviceId, ct: ct);

        if (!access.Succeeded)
        {
            return OperationResult.Fail(access.Error!);
        }

        if (!access.Value!.Device.IsOnline)
        {
            return OperationResult.Fail(DeviceOfflineError);
        }

        // The device remains the source of truth: its actual relay state comes
        // back through telemetry/status and only then updates the UI.
        if (!await _commands.PublishOutputCommandAsync(deviceId, on, ct))
        {
            return OperationResult.Fail("Could not deliver the command (broker unavailable). Try again.");
        }

        await _audit.LogAsync("device.control", userId, userEmail, deviceId,
            details: on ? "output=on" : "output=off", ipAddress: ipAddress, ct: ct);

        _logger.LogInformation("User {UserId} switched device {DeviceId} output {State}",
            userId, deviceId, on ? "ON" : "OFF");
        return OperationResult.Ok();
    }
}
