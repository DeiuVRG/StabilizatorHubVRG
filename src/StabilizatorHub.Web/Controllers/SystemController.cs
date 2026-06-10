using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Application.Abstractions.Repositories;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Infrastructure.Update;
using StabilizatorHub.Web.Extensions;

namespace StabilizatorHub.Web.Controllers;

/// <summary>
/// System endpoints: version for everyone logged in, update management,
/// audit trail and encrypted log access for administrators only.
/// </summary>
[ApiController]
[Route("api/system")]
[Authorize]
public sealed class SystemController : ControllerBase
{
    private readonly IUpdateChecker _updateChecker;
    private readonly IUpdateTrigger _updateTrigger;
    private readonly IEncryptedLogReader _logReader;
    private readonly IAuditRepository _auditRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAuditService _audit;

    public SystemController(
        IUpdateChecker updateChecker,
        IUpdateTrigger updateTrigger,
        IEncryptedLogReader logReader,
        IAuditRepository auditRepository,
        IDeviceRepository deviceRepository,
        IAuditService audit)
    {
        _updateChecker = updateChecker;
        _updateTrigger = updateTrigger;
        _logReader = logReader;
        _auditRepository = auditRepository;
        _deviceRepository = deviceRepository;
        _audit = audit;
    }

    [HttpGet("version")]
    public IActionResult Version() => Ok(new { version = AppVersion.Current });

    [HttpPost("update/check")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> CheckUpdate(CancellationToken ct) =>
        Ok(await _updateChecker.CheckAsync(ct));

    /// <summary>
    /// Requests the self-update. The separate updater systemd unit performs the
    /// download/swap/restart - this process only writes the trigger.
    /// </summary>
    [HttpPost("update/apply")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ApplyUpdate(CancellationToken ct)
    {
        var result = await _updateTrigger.RequestUpdateAsync(User.GetEmail() ?? "admin", ct);

        if (!result.Succeeded)
        {
            return BadRequest(new { error = result.Error });
        }

        await _audit.LogAsync("system.update_requested", User.GetUserId(), User.GetEmail(),
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), ct: ct);

        return Accepted(new { message = "Update requested. The service will restart shortly." });
    }

    [HttpGet("logs")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> ListLogs(CancellationToken ct)
    {
        var dates = await _logReader.ListAvailableDatesAsync(ct);
        return Ok(dates.Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
    }

    /// <summary>Downloads one day of telemetry decrypted to plain CSV (admin only).</summary>
    [HttpGet("logs/{date}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DownloadLog(string date, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day))
        {
            return BadRequest(new { error = "Date must be in yyyy-MM-dd format." });
        }

        var content = await _logReader.ReadDecryptedAsync(day, ct);

        if (content is null)
        {
            return NotFound(new { error = "No log exists for that day." });
        }

        await _audit.LogAsync("system.log_decrypted", User.GetUserId(), User.GetEmail(),
            details: $"date={date}", ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), ct: ct);

        return File(System.Text.Encoding.UTF8.GetBytes(content), "text/csv", $"telemetry-{date}.csv");
    }

    [HttpGet("audit")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AuditTrail([FromQuery] int take = 100, CancellationToken ct = default) =>
        Ok(await _auditRepository.GetRecentAsync(Math.Clamp(take, 1, 500), ct));

    [HttpGet("devices")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AllDevices(CancellationToken ct)
    {
        var devices = await _deviceRepository.GetAllWithMemberCountAsync(ct);
        return Ok(devices.Select(d => AdminDeviceDto.FromEntity(d.Device, d.MemberCount)));
    }
}
