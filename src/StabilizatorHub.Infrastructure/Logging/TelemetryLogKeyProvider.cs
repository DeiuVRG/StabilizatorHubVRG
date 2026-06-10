using System.Security.Cryptography;

namespace StabilizatorHub.Infrastructure.Logging;

/// <summary>
/// Resolves the AES-256 key for the encrypted telemetry log:
/// explicit Base64 key from configuration when provided, otherwise a key file
/// generated on first run with owner-only permissions (0600).
/// </summary>
public static class TelemetryLogKeyProvider
{
    public static byte[] Resolve(TelemetryLogOptions options, string dataDirectory)
    {
        if (!string.IsNullOrWhiteSpace(options.KeyBase64))
        {
            var key = Convert.FromBase64String(options.KeyBase64);

            if (key.Length != 32)
            {
                throw new InvalidOperationException("TelemetryLog:KeyBase64 must decode to exactly 32 bytes.");
            }

            return key;
        }

        var keyPath = Path.Combine(dataDirectory, options.KeyFileName);

        if (File.Exists(keyPath))
        {
            var key = Convert.FromBase64String(File.ReadAllText(keyPath).Trim());

            if (key.Length != 32)
            {
                throw new InvalidOperationException($"Key file '{keyPath}' is corrupted (expected 32 bytes).");
            }

            return key;
        }

        var newKey = RandomNumberGenerator.GetBytes(32);
        File.WriteAllText(keyPath, Convert.ToBase64String(newKey));

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(keyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return newKey;
    }
}
