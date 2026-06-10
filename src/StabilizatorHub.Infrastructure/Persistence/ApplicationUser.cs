using Microsoft.AspNetCore.Identity;

namespace StabilizatorHub.Infrastructure.Persistence;

/// <summary>
/// Identity user. Passwords are never stored - ASP.NET Core Identity persists
/// only a salted PBKDF2 hash in PasswordHash.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public DateTime CreatedAtUtc { get; set; }
}
