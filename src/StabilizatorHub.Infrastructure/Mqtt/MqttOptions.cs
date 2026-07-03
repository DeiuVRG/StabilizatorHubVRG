namespace StabilizatorHub.Infrastructure.Mqtt;

/// <summary>MQTT broker connection configuration (appsettings: "Mqtt").</summary>
public sealed class MqttOptions
{
    public const string SectionName = "Mqtt";

    /// <summary>Set false to run the backend without a broker (local development).</summary>
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 1883;

    /// <summary>Enable TLS - required by cloud brokers such as HiveMQ Cloud (port 8883).</summary>
    public bool UseTls { get; set; } = false;

    /// <summary>Broker user of the backend (full access to stabilizator/# per the ACL).</summary>
    public string Username { get; set; } = "backend";

    /// <summary>Set via environment/secrets file - never committed.</summary>
    public string Password { get; set; } = string.Empty;

    public string ClientId { get; set; } = "stabilizatorhub-backend";

    /// <summary>Root of the topic tree: {TopicRoot}/{deviceId}/{leaf}.</summary>
    public string TopicRoot { get; set; } = "stabilizator";

    public int ReconnectDelaySeconds { get; set; } = 5;
}
