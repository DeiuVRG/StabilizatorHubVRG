using System.Text.Json;
using StabilizatorHub.Domain.ValueObjects;

namespace StabilizatorHub.Infrastructure.Mqtt;

/// <summary>
/// Tolerant parser for the firmware telemetry JSON:
/// {"vin":228,"vout":230,"i":3.1,"p":713,"e":12.4,"out":1,"fw":"1.1"}
/// Optional fields may be missing in older firmware; malformed payloads yield null.
/// </summary>
public static class TelemetryPayloadParser
{
    private const double MaxPlausibleVoltage = 1000;
    private const double MaxPlausibleCurrent = 100;
    private const double MaxPlausiblePower = 50_000;

    public static TelemetrySample? Parse(string deviceId, string payload, DateTime receivedAtUtc)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var vin = ReadDouble(root, "vin");
            var vout = ReadDouble(root, "vout");

            if (vin is null || vout is null)
            {
                return null;
            }

            var current = ReadDouble(root, "i") ?? 0;
            var power = ReadDouble(root, "p") ?? 0;

            if (!IsPlausible(vin.Value, vout.Value, current, power))
            {
                return null;
            }

            return new TelemetrySample(
                DeviceId: deviceId,
                TimestampUtc: receivedAtUtc,
                VoltageIn: vin.Value,
                VoltageOut: vout.Value,
                CurrentAmps: current,
                PowerWatts: power,
                DeviceEnergyKwh: ReadDouble(root, "e"),
                OutputOn: ReadBool(root, "out"),
                FirmwareVersion: ReadString(root, "fw"));
        }
    }

    /// <summary>Payload of the info topic: {"pair":"7F3K9Q","fw":"1.0"}.</summary>
    public static (string? PairingCode, string? FirmwareVersion) ParseInfo(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            return (ReadString(document.RootElement, "pair"), ReadString(document.RootElement, "fw"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static bool IsPlausible(double vin, double vout, double current, double power) =>
        vin is >= 0 and <= MaxPlausibleVoltage &&
        vout is >= 0 and <= MaxPlausibleVoltage &&
        current is >= 0 and <= MaxPlausibleCurrent &&
        power is >= 0 and <= MaxPlausiblePower;

    private static double? ReadDouble(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static bool? ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.GetDouble() != 0,
            _ => null
        };
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
