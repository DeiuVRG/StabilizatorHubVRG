using StabilizatorHub.Infrastructure.Mqtt;
using Xunit;

namespace StabilizatorHub.Tests;

public class TelemetryPayloadParserTests
{
    private static readonly DateTime Received = new(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FullPayload_ParsesEveryField()
    {
        var sample = TelemetryPayloadParser.Parse(
            "A1B2C3D4E5F6",
            """{"vin":228,"vout":230.4,"i":3.1,"p":713,"e":12.4,"out":1,"fw":"1.1"}""",
            Received);

        Assert.NotNull(sample);
        Assert.Equal("A1B2C3D4E5F6", sample!.DeviceId);
        Assert.Equal(228, sample.VoltageIn);
        Assert.Equal(230.4, sample.VoltageOut);
        Assert.Equal(3.1, sample.CurrentAmps);
        Assert.Equal(713, sample.PowerWatts);
        Assert.Equal(12.4, sample.DeviceEnergyKwh);
        Assert.True(sample.OutputOn);
        Assert.Equal("1.1", sample.FirmwareVersion);
        Assert.Equal(Received, sample.TimestampUtc);
    }

    [Fact]
    public void MinimalPayload_FromOlderFirmware_StillParses()
    {
        var sample = TelemetryPayloadParser.Parse("A1B2C3D4E5F6", """{"vin":228,"vout":230}""", Received);

        Assert.NotNull(sample);
        Assert.Equal(0, sample!.CurrentAmps);
        Assert.Null(sample.OutputOn);
        Assert.Null(sample.FirmwareVersion);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"vout":230}""")]                       // missing vin
    [InlineData("""{"vin":"abc","vout":230}""")]           // wrong type
    [InlineData("""{"vin":99999,"vout":230}""")]           // implausible voltage
    [InlineData("""{"vin":228,"vout":230,"p":999999}""")]  // implausible power
    public void InvalidPayloads_ReturnNull(string payload)
    {
        Assert.Null(TelemetryPayloadParser.Parse("A1B2C3D4E5F6", payload, Received));
    }

    [Fact]
    public void InfoPayload_ParsesPairingCodeAndFirmware()
    {
        var (pair, fw) = TelemetryPayloadParser.ParseInfo("""{"pair":"7F3K9Q","fw":"1.0"}""");

        Assert.Equal("7F3K9Q", pair);
        Assert.Equal("1.0", fw);
    }

    [Fact]
    public void InfoPayload_Malformed_ReturnsNulls()
    {
        var (pair, fw) = TelemetryPayloadParser.ParseInfo("oops");

        Assert.Null(pair);
        Assert.Null(fw);
    }

    [Theory]
    [InlineData("stabilizator/A1B2C3D4E5F6/telemetrie", true, "A1B2C3D4E5F6", "telemetrie")]
    [InlineData("stabilizator/a1b2c3d4e5f6/status", true, "A1B2C3D4E5F6", "status")]
    [InlineData("alt/A1B2C3D4E5F6/telemetrie", false, "", "")]
    [InlineData("stabilizator/A1B2C3D4E5F6/extra/leaf", false, "", "")]
    [InlineData("stabilizator/../comanda", false, "", "")]
    public void TopicParsing_ValidatesShapeAndDeviceId(
        string topic, bool expectedOk, string expectedDevice, string expectedLeaf)
    {
        var ok = MqttTopics.TryParse(topic, "stabilizator", out var deviceId, out var leaf);

        Assert.Equal(expectedOk, ok);

        if (expectedOk)
        {
            Assert.Equal(expectedDevice, deviceId);
            Assert.Equal(expectedLeaf, leaf);
        }
    }
}
