using System.Security.Cryptography;

namespace Mics.Client;

public sealed class AesGcmMessageCrypto : IMicsMessageCrypto
{
    private const byte Version = 1;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _key;

    public AesGcmMessageCrypto(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length is not (16 or 24 or 32))
        {
            throw new ArgumentException("AES key length must be 16/24/32 bytes.", nameof(key));
        }

        _key = key.ToArray();
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        if (plaintext.Length == 0)
        {
            return Array.Empty<byte>();
        }

        var nonce = new byte[NonceBytes];
        RandomNumberGenerator.Fill(nonce);

        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagBytes];

        using var aes = new AesGcm(_key, TagBytes);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[1 + NonceBytes + TagBytes + ciphertext.Length];
        output[0] = Version;
        Buffer.BlockCopy(nonce, 0, output, 1, NonceBytes);
        Buffer.BlockCopy(tag, 0, output, 1 + NonceBytes, TagBytes);
        Buffer.BlockCopy(ciphertext, 0, output, 1 + NonceBytes + TagBytes, ciphertext.Length);
        return output;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> ciphertext)
    {
        if (ciphertext.Length == 0)
        {
            return Array.Empty<byte>();
        }

        if (ciphertext.Length < 1 + NonceBytes + TagBytes)
        {
            throw new CryptographicException("Ciphertext too short.");
        }

        if (ciphertext[0] != Version)
        {
            throw new CryptographicException("Unsupported ciphertext version.");
        }

        var nonce = ciphertext.Slice(1, NonceBytes);
        var tag = ciphertext.Slice(1 + NonceBytes, TagBytes);
        var enc = ciphertext.Slice(1 + NonceBytes + TagBytes);

        var plaintext = new byte[enc.Length];
        using var aes = new AesGcm(_key, TagBytes);
        aes.Decrypt(nonce, enc, tag, plaintext);
        return plaintext;
    }
}

