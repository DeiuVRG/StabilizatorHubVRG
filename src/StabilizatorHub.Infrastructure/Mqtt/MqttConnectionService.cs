using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using StabilizatorHub.Application.Services;
using StabilizatorHub.Domain.Abstractions;

namespace StabilizatorHub.Infrastructure.Mqtt;

/// <summary>
/// Owns the single MQTT client of the backend: keeps the connection alive,
/// subscribes to the device topics and routes incoming messages to the
/// application services (one DI scope per message). Also exposes publishing
/// for <see cref="MqttCommandPublisher"/>.
/// </summary>
public sealed class MqttConnectionService : BackgroundService
{
    private readonly MqttOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<MqttConnectionService> _logger;
    private readonly IMqttClient _client;

    public MqttConnectionService(
        IOptions<MqttOptions> options,
        IServiceScopeFactory scopeFactory,
        IClock clock,
        ILogger<MqttConnectionService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;

        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    public bool IsConnected => _client.IsConnected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning("MQTT is disabled (Mqtt:Enabled=false) - running without device connectivity");
            return;
        }

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithCredentials(_options.Username, _options.Password)
            .WithClientId(_options.ClientId)
            .WithCleanSession();

        if (_options.UseTls)
        {
            // Cloud brokers (HiveMQ Cloud, etc.) present a publicly trusted
            // certificate, so the default system CA validation is enough.
            optionsBuilder = optionsBuilder.WithTlsOptions(o => o.UseTls(true));
        }

        var clientOptions = optionsBuilder.Build();

        var reconnectDelay = TimeSpan.FromSeconds(Math.Max(1, _options.ReconnectDelaySeconds));

        // Self-healing connection loop: WiFi drops and broker restarts are
        // expected on a Raspberry Pi deployment.
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_client.IsConnected)
            {
                try
                {
                    await _client.ConnectAsync(clientOptions, stoppingToken);
                    await SubscribeAsync(stoppingToken);
                    _logger.LogInformation("Connected to MQTT broker at {Host}:{Port}", _options.Host, _options.Port);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("MQTT connection failed: {Message} - retrying in {Delay}s",
                        ex.Message, reconnectDelay.TotalSeconds);
                }
            }

            try
            {
                await Task.Delay(reconnectDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }
    }

    /// <summary>Publishes a message; returns false (never throws) when the broker is unreachable.</summary>
    public async Task<bool> TryPublishAsync(
        string topic, string payload, bool retain, CancellationToken ct = default)
    {
        if (!_options.Enabled || !_client.IsConnected)
        {
            return false;
        }

        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(retain)
                .Build();

            await _client.PublishAsync(message, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT publish to {Topic} failed", topic);
            return false;
        }
    }

    private async Task SubscribeAsync(CancellationToken ct)
    {
        var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f
                .WithTopic(MqttTopics.For(_options.TopicRoot, "+", MqttTopics.Telemetry))
                .WithAtLeastOnceQoS())
            .WithTopicFilter(f => f
                .WithTopic(MqttTopics.For(_options.TopicRoot, "+", MqttTopics.Status))
                .WithAtLeastOnceQoS())
            .WithTopicFilter(f => f
                .WithTopic(MqttTopics.For(_options.TopicRoot, "+", MqttTopics.Info))
                .WithAtLeastOnceQoS())
            .Build();

        await _client.SubscribeAsync(subscribeOptions, ct);
    }

    /// <summary>
    /// Messages are awaited sequentially, which both preserves per-device
    /// ordering and keeps the voltage tracker free of data races.
    /// </summary>
    private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            if (!MqttTopics.TryParse(e.ApplicationMessage.Topic, _options.TopicRoot, out var deviceId, out var leaf))
            {
                return;
            }

            var payload = e.ApplicationMessage.ConvertPayloadToString() ?? string.Empty;

            using var scope = _scopeFactory.CreateScope();

            switch (leaf)
            {
                case MqttTopics.Telemetry:
                    await HandleTelemetryAsync(scope.ServiceProvider, deviceId, payload);
                    break;

                case MqttTopics.Status:
                    await HandleStatusAsync(scope.ServiceProvider, deviceId, payload);
                    break;

                case MqttTopics.Info:
                    await HandleInfoAsync(scope.ServiceProvider, deviceId, payload);
                    break;
            }
        }
        catch (Exception ex)
        {
            // A poison message must never take down the MQTT pipeline.
            _logger.LogError(ex, "Failed to process MQTT message on {Topic}", e.ApplicationMessage.Topic);
        }
    }

    private async Task HandleTelemetryAsync(IServiceProvider services, string deviceId, string payload)
    {
        var sample = TelemetryPayloadParser.Parse(deviceId, payload, _clock.UtcNow);

        if (sample is null)
        {
            _logger.LogWarning("Discarded malformed telemetry from {DeviceId}", deviceId);
            return;
        }

        await services.GetRequiredService<ITelemetryIngestionService>().IngestAsync(sample);
    }

    private static async Task HandleStatusAsync(IServiceProvider services, string deviceId, string payload)
    {
        var status = payload.Trim().ToLowerInvariant();

        if (status is not ("online" or "offline"))
        {
            return;
        }

        await services.GetRequiredService<IDeviceRegistryService>()
            .HandleStatusAsync(deviceId, status == "online");
    }

    private static async Task HandleInfoAsync(IServiceProvider services, string deviceId, string payload)
    {
        var (pairingCode, firmwareVersion) = TelemetryPayloadParser.ParseInfo(payload);

        await services.GetRequiredService<IDeviceRegistryService>()
            .HandleInfoAsync(deviceId, pairingCode, firmwareVersion);
    }
}
