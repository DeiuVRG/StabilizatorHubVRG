using System.Security.Cryptography;
using System.Text;

namespace StabilizatorHub.Infrastructure.Logging;

/// <summary>
/// AES-256-GCM per line: authenticated encryption, so log lines can neither be
/// read nor silently altered. Token layout: base64(nonce[12] | tag[16] | ciphertext).
/// </summary>
public sealed class AesGcmLineCipher : ILineCipher, IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly AesGcm _aes;

    public AesGcmLineCipher(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("AES-256 key must be exactly 32 bytes.", nameof(key));
        }

        _aes = new AesGcm(key, TagSize);
    }

    public string Protect(string plaintext)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + TagSize + plainBytes.Length];

        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);

        RandomNumberGenerator.Fill(nonce);
        _aes.Encrypt(nonce, plainBytes, cipher, tag);

        return Convert.ToBase64String(buffer);
    }

    public string Unprotect(string token)
    {
        var buffer = Convert.FromBase64String(token);

        if (buffer.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Token too short.");
        }

        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        _aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    public void Dispose() => _aes.Dispose();
}
