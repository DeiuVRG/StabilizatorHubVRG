namespace StabilizatorHub.Web;

/// <summary>Names of the rate limiting policies configured in Program.</summary>
public static class RateLimitPolicies
{
    /// <summary>Strict per-IP limit for authentication and claim attempts.</summary>
    public const string Auth = "auth";
}

/// <summary>Role names used across the application.</summary>
public static class Roles
{
    public const string Admin = "Admin";
}
