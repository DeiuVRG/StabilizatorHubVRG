using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StabilizatorHub.Application.Dtos;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Web.Contracts;
using StabilizatorHub.Web.Extensions;

namespace StabilizatorHub.Web.Controllers;

/// <summary>
/// Device endpoints. Everything requires authentication and every operation is
/// ownership-checked inside the application services.
/// </summary>
[ApiController]
[Route("api/devices")]
[Authorize]
public sealed class DevicesController : ControllerBase
{
    private readonly IDeviceQueryService _query;
    private readonly IDeviceClaimService _claim;
    private readonly IDeviceControlService _control;
    private readonly IConsumptionService _consumption;
    private readonly IEventQueryService _events;

    public DevicesController(
        IDeviceQueryService query,
        IDeviceClaimService claim,
        IDeviceControlService control,
        IConsumptionService consumption,
        IEventQueryService events)
    {
        _query = query;
        _claim = claim;
        _control = control;
        _consumption = consumption;
        _events = events;
    }

    [HttpGet]
    public async Task<IActionResult> GetMine(CancellationToken ct) =>
        Ok(await _query.GetMineAsync(UserId(), ct));

    /// <summary>Claims an additional device with the pairing code from its OLED.</summary>
    [HttpPost("claim")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> Claim(ClaimRequest request, CancellationToken ct)
    {
        var result = await _claim.ClaimAsync(
            UserId(), UserEmail(), request.PairingCode,
            rateLimitKey: $"claim:{UserId()}", ipAddress: ClientIp(), ct);

        return result.Succeeded ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    [HttpPut("{deviceId}")]
    public async Task<IActionResult> Rename(string deviceId, RenameRequest request, CancellationToken ct)
    {
        var result = await _claim.RenameAsync(UserId(), deviceId, request.Name, ct);
        return result.Succeeded ? Ok(result.Value) : BadRequest(new { error = result.Error });
    }

    /// <summary>Releases the device from this account (it becomes claimable again with a fresh code).</summary>
    [HttpDelete("{deviceId}")]
    public async Task<IActionResult> Release(string deviceId, CancellationToken ct)
    {
        var result = await _claim.ReleaseAsync(UserId(), UserEmail(), deviceId, ClientIp(), ct);
        return result.Succeeded ? NoContent() : BadRequest(new { error = result.Error });
    }

    /// <summary>Remote SSR on/off.</summary>
    [HttpPost("{deviceId}/control")]
    public async Task<IActionResult> Control(string deviceId, ControlRequest request, CancellationToken ct)
    {
        var result = await _control.SetOutputAsync(
            UserId(), UserEmail(), deviceId, request.On!.Value, ClientIp(), ct);

        return result.Succeeded ? Accepted(new { delivered = true }) : BadRequest(new { error = result.Error });
    }

    [HttpGet("{deviceId}/telemetry/latest")]
    public async Task<IActionResult> LatestTelemetry(string deviceId, CancellationToken ct)
    {
        var result = await _query.GetLatestTelemetryAsync(UserId(), deviceId, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpGet("{deviceId}/telemetry/recent")]
    public async Task<IActionResult> RecentTelemetry(
        string deviceId, [FromQuery] int minutes = 60, CancellationToken ct = default)
    {
        var result = await _query.GetRecentTelemetryAsync(UserId(), deviceId, minutes, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    /// <summary>Consumption chart data: range=day|week|month|year, tz=offset minutes from UTC.</summary>
    [HttpGet("{deviceId}/history")]
    public async Task<IActionResult> History(
        string deviceId, [FromQuery] string range = "day", [FromQuery] int tz = 0,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<HistoryRange>(range, ignoreCase: true, out var historyRange))
        {
            return BadRequest(new { error = "Range must be one of: day, week, month, year." });
        }

        var result = await _consumption.GetHistoryAsync(UserId(), deviceId, historyRange, tz, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpGet("{deviceId}/summary")]
    public async Task<IActionResult> Summary(
        string deviceId, [FromQuery] int tz = 0, CancellationToken ct = default)
    {
        var result = await _consumption.GetSummaryAsync(UserId(), deviceId, tz, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    [HttpGet("{deviceId}/events")]
    public async Task<IActionResult> Events(
        string deviceId, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        var result = await _events.GetRecentAsync(UserId(), deviceId, take, ct);
        return result.Succeeded ? Ok(result.Value) : NotFound(new { error = result.Error });
    }

    private string UserId() => User.GetUserId()!;

    private string? UserEmail() => User.GetEmail();

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}
