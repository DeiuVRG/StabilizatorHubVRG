using Microsoft.Extensions.Options;
using StabilizatorHub.Application.Abstractions;

namespace StabilizatorHub.Infrastructure.Mqtt;

/// <summary>
/// MQTT implementation of the device command port. Payload contract matches
/// the firmware: comanda = {"output":"on"|"off"}, claimed = "true"|"false" (retained).
/// </summary>
public sealed class MqttCommandPublisher : IDeviceCommandPublisher
{
    private readonly MqttConnectionService _connection;
    private readonly MqttOptions _options;

    public MqttCommandPublisher(MqttConnectionService connection, IOptions<MqttOptions> options)
    {
        _connection = connection;
        _options = options.Value;
    }

    public Task<bool> PublishOutputCommandAsync(string deviceId, bool on, CancellationToken ct = default) =>
        _connection.TryPublishAsync(
            MqttTopics.For(_options.TopicRoot, deviceId, MqttTopics.Command),
            on ? """{"output":"on"}""" : """{"output":"off"}""",
            retain: false,
            ct);

    public Task<bool> PublishClaimedAsync(string deviceId, bool claimed, CancellationToken ct = default) =>
        _connection.TryPublishAsync(
            MqttTopics.For(_options.TopicRoot, deviceId, MqttTopics.Claimed),
            claimed ? "true" : "false",
            retain: true,
            ct);
}
