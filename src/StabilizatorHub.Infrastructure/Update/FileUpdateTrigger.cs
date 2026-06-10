using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Common;
using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Infrastructure.Update;

/// <summary>
/// Requests a self-update by writing a trigger file. A separate systemd path
/// unit (running with the needed privileges) notices the file, downloads the
/// latest release and restarts the service - privilege separation: the web app
/// itself never executes the update.
/// </summary>
public sealed class FileUpdateTrigger : IUpdateTrigger
{
    private readonly UpdateOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<FileUpdateTrigger> _logger;

    public FileUpdateTrigger(IOptions<UpdateOptions> options, IClock clock, ILogger<FileUpdateTrigger> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<OperationResult> RequestUpdateAsync(string requestedBy, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            return OperationResult.Fail("Self-update is disabled on this server.");
        }

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                requestedAtUtc = _clock.UtcNow,
                requestedBy,
                currentVersion = AppVersion.Current
            });

            var directory = Path.GetDirectoryName(_options.TriggerFilePath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(_options.TriggerFilePath, payload, ct);

            _logger.LogWarning("Update requested by {RequestedBy} - trigger written to {Path}",
                requestedBy, _options.TriggerFilePath);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not write the update trigger file");
            return OperationResult.Fail("Could not write the update trigger file.");
        }
    }
}
