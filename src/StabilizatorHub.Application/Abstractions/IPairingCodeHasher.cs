namespace StabilizatorHub.Application.Abstractions;

/// <summary>
/// One-way hashing port for device pairing codes. Codes are never stored in
/// clear; verification is constant-time in the implementation.
/// </summary>
public interface IPairingCodeHasher
{
    string Hash(string pairingCode);

    bool Verify(string pairingCode, string storedHash);
}
