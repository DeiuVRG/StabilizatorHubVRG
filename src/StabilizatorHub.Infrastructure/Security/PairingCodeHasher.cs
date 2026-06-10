using System.Security.Cryptography;
using StabilizatorHub.Application.Abstractions;

namespace StabilizatorHub.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 hashing for pairing codes. Codes are short, so a slow KDF plus
/// the API rate limiting keeps brute force impractical. Verification uses a
/// constant-time comparison.
/// Stored format: PBKDF2.{iterations}.{saltBase64}.{hashBase64}
/// </summary>
public sealed class PairingCodeHasher : IPairingCodeHasher
{
    private const string Prefix = "PBKDF2";
    private const int Iterations = 100_000;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;

    public string Hash(string pairingCode)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hash = Derive(pairingCode, salt, Iterations);

        return string.Join('.', Prefix, Iterations,
            Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public bool Verify(string pairingCode, string storedHash)
    {
        var parts = storedHash.Split('.');

        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(pairingCode, salt, iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(code, salt, iterations, HashAlgorithmName.SHA256, HashSizeBytes);
}
