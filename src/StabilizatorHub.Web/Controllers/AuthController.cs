using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Infrastructure.Persistence;
using StabilizatorHub.Web.Contracts;
using StabilizatorHub.Web.Extensions;

namespace StabilizatorHub.Web.Controllers;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IDeviceClaimService _claimService;
    private readonly IAuditService _audit;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IDeviceClaimService claimService,
        IAuditService audit,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _claimService = claimService;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Creates an account and atomically claims the device whose pairing code
    /// was entered. When the code is wrong, the freshly created user is rolled
    /// back - no account exists without a stabilizer.
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            CreatedAtUtc = DateTime.UtcNow
        };

        var created = await _userManager.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            return BadRequest(new { error = FirstError(created) });
        }

        var claim = await _claimService.ClaimAsync(
            user.Id, user.Email, request.PairingCode,
            rateLimitKey: $"register:{ClientIp()}", ipAddress: ClientIp(), ct);

        if (!claim.Succeeded)
        {
            // Roll back: the pairing code is the proof of purchase.
            await _userManager.DeleteAsync(user);
            return BadRequest(new { error = claim.Error });
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        await _audit.LogAsync("auth.register", user.Id, user.Email,
            deviceId: claim.Value!.Id, ipAddress: ClientIp(), ct: ct);

        _logger.LogInformation("New account {Email} with device {DeviceId}", user.Email, claim.Value.Id);
        return Ok(new MeResponse(user.Email!, IsAdmin: false));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user is null)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }

        // lockoutOnFailure: wrong passwords count towards the account lockout.
        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            await _audit.LogAsync("auth.login.lockout", user.Id, user.Email, ipAddress: ClientIp(), ct: ct);
            return Unauthorized(new { error = "Too many failed attempts. The account is temporarily locked." });
        }

        if (!result.Succeeded)
        {
            await _audit.LogAsync("auth.login.failed", user.Id, user.Email, ipAddress: ClientIp(), ct: ct);
            return Unauthorized(new { error = "Invalid email or password." });
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        await _audit.LogAsync("auth.login", user.Id, user.Email, ipAddress: ClientIp(), ct: ct);

        return Ok(new MeResponse(user.Email!, await IsAdminAsync(user)));
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new MeResponse(user.Email!, await IsAdminAsync(user)));
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            return BadRequest(new { error = FirstError(result) });
        }

        await _audit.LogAsync("auth.password_changed", user.Id, user.Email, ipAddress: ClientIp(), ct: ct);
        return NoContent();
    }

    private async Task<bool> IsAdminAsync(ApplicationUser user) =>
        await _userManager.IsInRoleAsync(user, Roles.Admin);

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private static string FirstError(IdentityResult result) =>
        result.Errors.FirstOrDefault()?.Description ?? "The request could not be processed.";
}
