using Microsoft.AspNetCore.Identity;
using StabilizatorHub.Infrastructure.Persistence;

namespace StabilizatorHub.Web.Identity;

/// <summary>
/// Creates the Admin role and (optionally) the administrator account at startup.
/// Credentials come from configuration/environment (Admin:Email, Admin:Password)
/// so nothing sensitive lives in the repository.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        if (!await roleManager.RoleExistsAsync(Roles.Admin))
        {
            await roleManager.CreateAsync(new IdentityRole(Roles.Admin));
        }

        var email = configuration["Admin:Email"];
        var password = configuration["Admin:Password"];

        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogInformation("No Admin:Email configured - skipping admin account seeding");
            return;
        }

        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync(email);

        if (admin is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("Admin:Email set but Admin:Password missing - cannot create admin account");
                return;
            }

            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                CreatedAtUtc = DateTime.UtcNow
            };

            var created = await userManager.CreateAsync(admin, password);

            if (!created.Succeeded)
            {
                logger.LogError("Could not create admin account: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }

            logger.LogInformation("Admin account {Email} created", email);
        }

        if (!await userManager.IsInRoleAsync(admin, Roles.Admin))
        {
            await userManager.AddToRoleAsync(admin, Roles.Admin);
            logger.LogInformation("Admin role granted to {Email}", email);
        }
    }
}
