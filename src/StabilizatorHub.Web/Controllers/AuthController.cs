using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Infrastructure.Demo;
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
    private readonly DemoOptions _demoOptions;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IDeviceClaimService claimService,
        IAuditService audit,
        IOptions<DemoOptions> demoOptions,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _claimService = claimService;
        _audit = audit;
        _demoOptions = demoOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Enters the shared read-only demo session (no credentials). Available
    /// only when demo mode is enabled; mutating endpoints reject demo users.
    /// </summary>
    [HttpPost("demo")]
    [AllowAnonymous]
    public async Task<IActionResult> DemoLogin(CancellationToken ct)
    {
        if (!_demoOptions.Enabled)
        {
            return NotFound(new { error = "Demo mode is not enabled." });
        }

        var demoUser = await _userManager.FindByEmailAsync(_demoOptions.Email);

        if (demoUser is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Demo data is still being prepared. Try again in a minute." });
        }

        // Non-persistent: the demo session ends with the browser.
        await _signInManager.SignInAsync(demoUser, isPersistent: false);
        await _audit.LogAsync("auth.demo", demoUser.Id, demoUser.Email, ipAddress: ClientIp(), ct: ct);

        return Ok(new MeResponse(demoUser.Email!, IsAdmin: false, IsDemo: true));
    }

    /// <summary>
    /// Creates an account and atomically attaches it to a device: a pairing
    /// code (from the device OLED) makes the user the Owner, an invite code
    /// (from the owner) makes them a household Member. When the code is wrong,
    /// the freshly created user is rolled back - no account exists without a
    /// stabilizer.
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

        var redeemed = await _claimService.RedeemCodeAsync(
            user.Id, user.Email, request.PairingCode,
            rateLimitKey: $"register:{ClientIp()}", ipAddress: ClientIp(), ct);

        if (!redeemed.Succeeded)
        {
            // Roll back: the code is the proof of purchase/household membership.
            await _userManager.DeleteAsync(user);
            return BadRequest(new { error = redeemed.Error });
        }

        await _signInManager.SignInAsync(user, isPersistent: true);
        await _audit.LogAsync("auth.register", user.Id, user.Email,
            deviceId: redeemed.Value!.Id, ipAddress: ClientIp(), ct: ct);

        _logger.LogInformation("New account {Email} attached to device {DeviceId} as {Role}",
            user.Email, redeemed.Value.Id, redeemed.Value.Role);
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

        return Ok(new MeResponse(user.Email!, await IsAdminAsync(user), IsDemoEmail(user.Email)));
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

        return Ok(new MeResponse(user.Email!, await IsAdminAsync(user), IsDemoEmail(user.Email)));
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

        if (IsDemoEmail(user.Email))
        {
            return BadRequest(new { error = "Not available in demo mode." });
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

    private bool IsDemoEmail(string? email) =>
        _demoOptions.Enabled
        && string.Equals(email, _demoOptions.Email, StringComparison.OrdinalIgnoreCase);

    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private static string FirstError(IdentityResult result) =>
        result.Errors.FirstOrDefault()?.Description ?? "The request could not be processed.";
}
