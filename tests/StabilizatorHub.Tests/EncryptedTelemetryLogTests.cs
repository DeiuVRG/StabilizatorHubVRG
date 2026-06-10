using Microsoft.Extensions.Logging.Abstractions;
using StabilizatorHub.Domain.ValueObjects;
using StabilizatorHub.Infrastructure.Logging;
using Xunit;

namespace StabilizatorHub.Tests;

public class EncryptedTelemetryLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "stabhub-tests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = new byte[32];

    public EncryptedTelemetryLogTests()
    {
        Random.Shared.NextBytes(_key);
    }

    private EncryptedTelemetryLog NewLog(int retentionDays = 90) =>
        new(new TelemetryLogOptions { RetentionDays = retentionDays },
            _directory,
            new AesGcmLineCipher(_key),
            NullLogger<EncryptedTelemetryLog>.Instance);

    private static TelemetrySample Sample(DateTime ts) =>
        new("A1B2C3D4E5F6", ts, 228.4, 230.1, 1.53, 352.0, OutputOn: true);

    [Fact]
    public async Task Append_ThenRead_RecoversThePlainCsvLine()
    {
        var log = NewLog();
        var day = new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc);

        await log.AppendAsync(Sample(day), 5.87);
        var content = await log.ReadDecryptedAsync(DateOnly.FromDateTime(day));

        Assert.NotNull(content);
        Assert.Contains(EncryptedTelemetryLog.CsvHeader, content);
        Assert.Contains("2026-06-11T10:00:00Z,A1B2C3D4E5F6,228.4,230.1,1.53,352.0,5.870,1", content);
    }

    [Fact]
    public async Task FileOnDisk_IsNotPlaintext()
    {
        var log = NewLog();
        var day = new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc);

        await log.AppendAsync(Sample(day), 5.87);

        var raw = await File.ReadAllTextAsync(Path.Combine(_directory, "telemetry-20260611.csv.enc"));
        Assert.DoesNotContain("A1B2C3D4E5F6", raw);
        Assert.DoesNotContain("228.4", raw);
    }

    [Fact]
    public async Task Rotation_WritesOneFilePerUtcDay()
    {
        var log = NewLog();

        await log.AppendAsync(Sample(new DateTime(2026, 6, 10, 23, 59, 0, DateTimeKind.Utc)), 1);
        await log.AppendAsync(Sample(new DateTime(2026, 6, 11, 0, 1, 0, DateTimeKind.Utc)), 1);

        var dates = await log.ListAvailableDatesAsync();

        Assert.Equal(2, dates.Count);
        Assert.True(File.Exists(Path.Combine(_directory, "telemetry-20260610.csv.enc")));
        Assert.True(File.Exists(Path.Combine(_directory, "telemetry-20260611.csv.enc")));
    }

    [Fact]
    public async Task Retention_DeletesFilesOlderThanTheWindow()
    {
        var log = NewLog(retentionDays: 7);

        await log.AppendAsync(Sample(new DateTime(2026, 5, 1, 10, 0, 0, DateTimeKind.Utc)), 1);
        // Appending on a much later day triggers the daily retention pass.
        await log.AppendAsync(Sample(new DateTime(2026, 6, 11, 10, 0, 0, DateTimeKind.Utc)), 1);

        var dates = await log.ListAvailableDatesAsync();

        Assert.Single(dates);
        Assert.Equal(new DateOnly(2026, 6, 11), dates[0]);
    }

    [Fact]
    public async Task ReadDecrypted_ReturnsNullForMissingDay()
    {
        var log = NewLog();

        Assert.Null(await log.ReadDecryptedAsync(new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Cipher_DetectsTampering()
    {
        var cipher = new AesGcmLineCipher(_key);
        var token = cipher.Protect("secret,line");

        var bytes = Convert.FromBase64String(token);
        bytes[^1] ^= 0xFF; // flip one ciphertext bit
        var tampered = Convert.ToBase64String(bytes);

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => cipher.Unprotect(tampered));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
