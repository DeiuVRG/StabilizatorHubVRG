using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StabilizatorHub.Application;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Infrastructure;
using StabilizatorHub.Infrastructure.Persistence;
using StabilizatorHub.Web;
using StabilizatorHub.Web.Hubs;
using StabilizatorHub.Web.Identity;
using StabilizatorHub.Web.Middleware;
using StabilizatorHub.Web.Realtime;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Layers (composition root - the only place that knows every implementation)
// ---------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSingleton<ITelemetryBroadcaster, SignalRTelemetryBroadcaster>();

// ---------------------------------------------------------------------------
// Identity: accounts with hashed passwords (PBKDF2), lockout and roles
// ---------------------------------------------------------------------------
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;

        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;

        // Brute-force protection at account level (on top of IP rate limiting).
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services
    .AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddCookie(IdentityConstants.ApplicationScheme, options =>
    {
        options.Cookie.Name = "stabhub.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;

        // An API must answer 401/403, never redirect to an HTML login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// Persisted data-protection keys: auth cookies stay valid across restarts/updates.
var dataDirectory = Path.GetFullPath(builder.Configuration["Storage:DataDirectory"] ?? "data");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "dp-keys")))
    .SetApplicationName("StabilizatorHubVRG");

// ---------------------------------------------------------------------------
// CSRF: cookie-to-header pattern for the vanilla JS frontend
// ---------------------------------------------------------------------------
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "stabhub.xsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddControllers(options =>
{
    // Every state-changing controller action requires the antiforgery header.
    options.Filters.Add<ValidateAntiforgeryTokenFilter>();
});

builder.Services.AddSignalR();

// ---------------------------------------------------------------------------
// Rate limiting: global per-IP ceiling + strict policy for auth endpoints
// ---------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(context), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy(RateLimitPolicies.Auth, context =>
        RateLimitPartition.GetFixedWindowLimiter(ClientIp(context), _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    static string ClientIp(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
});

// Behind the Cloudflare tunnel (cloudflared on localhost) the client address
// arrives in X-Forwarded-For; trust only the loopback proxy.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Telemetry payloads and API bodies are tiny - cap request size defensively.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 64 * 1024);

var app = builder.Build();

// ---------------------------------------------------------------------------
// Database migration + admin seeding (idempotent at every start)
// ---------------------------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration,
        app.Services.GetRequiredService<ILogger<Program>>());
}

// ---------------------------------------------------------------------------
// Pipeline
// ---------------------------------------------------------------------------
app.UseForwardedHeaders();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { error = "Unexpected server error." });
}));

app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Issue the antiforgery request token as a readable cookie on page loads; the
// frontend echoes it back in the X-XSRF-TOKEN header. Runs after
// UseAuthentication because the token is bound to the signed-in identity.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method)
        && !context.Request.Path.StartsWithSegments("/api")
        && !context.Request.Path.StartsWithSegments("/hub"))
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);

        context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/"
        });
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();
app.MapHub<LiveHub>("/hub/live");
app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    version = StabilizatorHub.Infrastructure.Update.AppVersion.Current
}));

app.Run();
