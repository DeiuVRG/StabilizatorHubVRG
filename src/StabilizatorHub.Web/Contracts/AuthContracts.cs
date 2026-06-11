using System.ComponentModel.DataAnnotations;

namespace StabilizatorHub.Web.Contracts;

/// <summary>
/// Account creation request. A valid pairing code is mandatory: accounts exist
/// only for people who physically own a stabilizer.
/// </summary>
public sealed record RegisterRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MinLength(10), MaxLength(128)]
    public string Password { get; init; } = string.Empty;

    [Required, MinLength(4), MaxLength(16)]
    public string PairingCode { get; init; } = string.Empty;
}

public sealed record LoginRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string Password { get; init; } = string.Empty;
}

public sealed record ChangePasswordRequest
{
    [Required, MaxLength(128)]
    public string CurrentPassword { get; init; } = string.Empty;

    [Required, MinLength(10), MaxLength(128)]
    public string NewPassword { get; init; } = string.Empty;
}

public sealed record MeResponse(string Email, bool IsAdmin, bool IsDemo = false);
