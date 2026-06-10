namespace StabilizatorHub.Infrastructure.Mqtt;

/// <summary>
/// Topic naming convention shared with the ESP32 firmware:
/// stabilizator/{deviceId}/{telemetrie|status|info|comanda|claimed}
/// </summary>
public static class MqttTopics
{
    public const string Telemetry = "telemetrie";
    public const string Status = "status";
    public const string Info = "info";
    public const string Command = "comanda";
    public const string Claimed = "claimed";

    public static string For(string root, string deviceId, string leaf) => $"{root}/{deviceId}/{leaf}";

    /// <summary>
    /// Splits "root/deviceId/leaf"; returns false for foreign or malformed topics.
    /// </summary>
    public static bool TryParse(string topic, string expectedRoot, out string deviceId, out string leaf)
    {
        deviceId = string.Empty;
        leaf = string.Empty;

        var parts = topic.Split('/');

        if (parts.Length != 3 || !string.Equals(parts[0], expectedRoot, StringComparison.Ordinal))
        {
            return false;
        }

        deviceId = parts[1].Trim().ToUpperInvariant();
        leaf = parts[2];

        return IsValidDeviceId(deviceId);
    }

    /// <summary>Device ids are MAC-derived: 4-32 chars, alphanumeric (plus - and _).</summary>
    public static bool IsValidDeviceId(string deviceId) =>
        deviceId.Length is >= 4 and <= 32 &&
        deviceId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
