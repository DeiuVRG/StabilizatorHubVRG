namespace StabilizatorHub.Infrastructure.Logging;

/// <summary>
/// Line-level encryption strategy for the telemetry log. Each line is sealed
/// independently so the file stays appendable and one corrupt line does not
/// destroy the rest (Open/Closed: swap the algorithm without touching the log).
/// </summary>
public interface ILineCipher
{
    /// <summary>Encrypts one plaintext line into a single-line token.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts one token; throws CryptographicException on tamper/corruption.</summary>
    string Unprotect(string token);
}
