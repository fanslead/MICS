using System.Security.Cryptography;
using Google.Protobuf;
using Mics.Contracts.Hook.V1;

namespace Mics.HookSdk;

public static class HookVerifier
{
    public static bool Verify(string tenantSecret, HookMeta meta, IMessage payloadForSign)
    {
        if (meta is null || string.IsNullOrWhiteSpace(meta.Sign))
        {
            return false;
        }

        byte[] actual;
        try
        {
            actual = Convert.FromBase64String(meta.Sign);
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = Convert.FromBase64String(HookSigner.ComputeBase64(tenantSecret, meta, payloadForSign));
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}

