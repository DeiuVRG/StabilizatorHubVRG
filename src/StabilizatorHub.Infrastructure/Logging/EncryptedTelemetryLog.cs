using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using StabilizatorHub.Application.Abstractions;
using StabilizatorHub.Domain.ValueObjects;

namespace StabilizatorHub.Infrastructure.Logging;

/// <summary>
/// Encrypted CSV telemetry log with daily rotation and retention:
/// one file per UTC day (telemetry-yyyyMMdd.csv.enc), every line sealed
/// individually with the injected <see cref="ILineCipher"/>. Files older than
/// the retention window are removed during the daily rollover.
/// Registered as a singleton; appends are serialized with a semaphore.
/// </summary>
public sealed class EncryptedTelemetryLog : ITelemetryLogWriter, IEncryptedLogReader
{
    public const string CsvHeader = "timestamp_utc,device_id,voltage_in,voltage_out,current_a,power_w,energy_wh,output_on";

    private const string FilePrefix = "telemetry-";
    private const string FileSuffix = ".csv.enc";

    private readonly string _directory;
    private readonly int _retentionDays;
    private readonly bool _enabled;
    private readonly ILineCipher _cipher;
    private readonly ILogger<EncryptedTelemetryLog> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private DateOnly _lastRolloverCheck = DateOnly.MinValue;

    public EncryptedTelemetryLog(
        TelemetryLogOptions options,
        string resolvedDirectory,
        ILineCipher cipher,
        ILogger<EncryptedTelemetryLog> logger)
    {
        _directory = resolvedDirectory;
        _retentionDays = options.RetentionDays;
        _enabled = options.Enabled;
        _cipher = cipher;
        _logger = logger;

        Directory.CreateDirectory(_directory);
    }

    public async Task AppendAsync(TelemetrySample sample, double intervalEnergyWh, CancellationToken ct = default)
    {
        if (!_enabled)
        {
            return;
        }

        var line = string.Join(',',
            sample.TimestampUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            sample.DeviceId,
            sample.VoltageIn.ToString("F1", CultureInfo.InvariantCulture),
            sample.VoltageOut.ToString("F1", CultureInfo.InvariantCulture),
            sample.CurrentAmps.ToString("F2", CultureInfo.InvariantCulture),
            sample.PowerWatts.ToString("F1", CultureInfo.InvariantCulture),
            intervalEnergyWh.ToString("F3", CultureInfo.InvariantCulture),
            sample.OutputOn == true ? "1" : "0");

        var day = DateOnly.FromDateTime(sample.TimestampUtc);
        var token = _cipher.Protect(line);

        await _writeLock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(PathFor(day), token + Environment.NewLine, ct);
            RunRetentionOncePerDay(day);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task<IReadOnlyList<DateOnly>> ListAvailableDatesAsync(CancellationToken ct = default)
    {
        var dates = Directory.EnumerateFiles(_directory, FilePrefix + "*" + FileSuffix)
            .Select(ParseDate)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .OrderDescending()
            .ToList();

        return Task.FromResult<IReadOnlyList<DateOnly>>(dates);
    }

    public async Task<string?> ReadDecryptedAsync(DateOnly date, CancellationToken ct = default)
    {
        var path = PathFor(date);

        if (!File.Exists(path))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(path, ct);
        var output = new List<string>(lines.Length + 1) { CsvHeader };

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                output.Add(_cipher.Unprotect(line.Trim()));
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                output.Add("# unreadable line (corrupted or tampered)");
            }
        }

        return string.Join('\n', output);
    }

    private string PathFor(DateOnly day) =>
        Path.Combine(_directory, FilePrefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + FileSuffix);

    private static DateOnly? ParseDate(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var datePart = name[FilePrefix.Length..^FileSuffix.Length];

        return DateOnly.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    private void RunRetentionOncePerDay(DateOnly today)
    {
        if (_retentionDays <= 0 || _lastRolloverCheck == today)
        {
            return;
        }

        _lastRolloverCheck = today;
        var cutoff = today.AddDays(-_retentionDays);

        foreach (var file in Directory.EnumerateFiles(_directory, FilePrefix + "*" + FileSuffix))
        {
            var date = ParseDate(file);

            if (date is not null && date < cutoff)
            {
                try
                {
                    File.Delete(file);
                    _logger.LogInformation("Deleted expired telemetry log {File}", Path.GetFileName(file));
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not delete expired telemetry log {File}", file);
                }
            }
        }
    }
}
