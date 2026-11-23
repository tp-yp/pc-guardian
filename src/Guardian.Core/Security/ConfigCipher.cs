using System.Security.Cryptography;

namespace Guardian.Core.Security;

/// <summary>Protects the data key at rest (DPAPI LocalMachine on Windows).</summary>
public interface IKeyProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

/// <summary>AES-256-GCM. Layout: magic(4) | nonce(12) | tag(16) | ciphertext.</summary>
public static class ConfigCipher
{
    private static readonly byte[] Magic = "GRD1"u8.ToArray();
    private const int NonceSize = 12, TagSize = 16;

    public static byte[] Encrypt(byte[] key, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ct = new byte[plaintext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ct, tag, Magic);
        return [.. Magic, .. nonce, .. tag, .. ct];
    }

    public static byte[] Decrypt(byte[] key, byte[] blob)
    {
        if (blob.Length < Magic.Length + NonceSize + TagSize || !blob.AsSpan(0, 4).SequenceEqual(Magic))
            throw new CryptographicException("Not a Guardian config file.");
        var nonce = blob.AsSpan(4, NonceSize);
        var tag = blob.AsSpan(4 + NonceSize, TagSize);
        var ct = blob.AsSpan(4 + NonceSize + TagSize);
        var pt = new byte[ct.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ct, tag, pt, Magic); // throws on tampering
        return pt;
    }
}
