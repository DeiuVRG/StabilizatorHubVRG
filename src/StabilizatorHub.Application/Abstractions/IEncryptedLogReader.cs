namespace StabilizatorHub.Application.Abstractions;

/// <summary>
/// Admin-only port for reading back encrypted telemetry log files in clear text.
/// </summary>
public interface IEncryptedLogReader
{
    /// <summary>Dates for which an encrypted log file exists, newest first.</summary>
    Task<IReadOnlyList<DateOnly>> ListAvailableDatesAsync(CancellationToken ct = default);

    /// <summary>Decrypts the log of a given day; null when no file exists.</summary>
    Task<string?> ReadDecryptedAsync(DateOnly date, CancellationToken ct = default);
}
