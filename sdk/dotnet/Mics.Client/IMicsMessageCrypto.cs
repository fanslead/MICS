namespace Mics.Client;

public interface IMicsMessageCrypto
{
    byte[] Encrypt(ReadOnlySpan<byte> plaintext);
    byte[] Decrypt(ReadOnlySpan<byte> ciphertext);
}

