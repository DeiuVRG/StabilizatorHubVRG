using System.Reflection;

namespace StabilizatorHub.Infrastructure.Update;

/// <summary>Resolves the running application version from the entry assembly.</summary>
public static class AppVersion
{
    public static string Current
    {
        get
        {
            var informational = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            // Informational versions can carry build metadata ("1.0.0+sha").
            return informational?.Split('+')[0] ?? "0.0.0";
        }
    }

    /// <summary>Parses "v1.2.3" / "1.2.3-beta" tags into a comparable Version.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Trim().TrimStart('v', 'V');
        var stable = cleaned.Split('-', '+')[0];

        return Version.TryParse(stable, out var version) ? version : null;
    }
}
